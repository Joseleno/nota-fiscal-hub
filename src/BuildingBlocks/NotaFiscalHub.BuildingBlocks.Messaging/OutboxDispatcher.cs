using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using NotaFiscalHub.BuildingBlocks.Observability;
using Serilog.Context;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Dispatcher da Outbox de um módulo (spec B3 passo 4). Loop de polling reivindica um lote de mensagens
/// <c>Pendente</c> vencidas com <c>SELECT ... FOR UPDATE SKIP LOCKED</c> (dois dispatchers concorrentes —
/// dois hosts do Worker, ou Worker+API se A2 exigir — nunca reivindicam a mesma linha) e processa cada
/// mensagem de forma independente: falha/poison de uma mensagem nunca aborta as demais do lote.
/// </summary>
public sealed class OutboxDispatcher<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxTypeRegistry<TDbContext> _registry;
    private readonly OutboxInboxOptions _options;
    private readonly ILogger<OutboxDispatcher<TDbContext>> _logger;
    private readonly IOutboxMetrics _metrics;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        OutboxTypeRegistry<TDbContext> registry,
        IOptions<OutboxInboxOptions> options,
        ILogger<OutboxDispatcher<TDbContext>> logger,
        IOutboxMetrics? metrics = null)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _options = options.Value;
        _logger = logger;
        _metrics = metrics ?? NullOutboxMetrics.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessarLoteAsync(stoppingToken);

            try
            {
                await Task.Delay(_options.IntervaloDePolling, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Executa um ciclo de dispatch: reivindica um lote e processa cada mensagem. Público para permitir
    /// que testes de integração disparem ciclos determinísticos (em vez de esperar o timer do
    /// <see cref="BackgroundService"/>).
    /// </summary>
    public async Task ProcessarLoteAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var mensagens = await ReivindicarLoteAsync(db, ct);

        foreach (var mensagem in mensagens)
        {
            await ProcessarMensagemAsync(mensagem.Id, ct);
        }
    }

    /// <summary>
    /// Duração da "posse" de uma mensagem reivindicada por este dispatcher antes de voltar a ser
    /// candidata para outro ciclo/dispatcher — auto-recuperação se o processo morrer entre a reivindicação
    /// e a conclusão (nunca fica travada em "reivindicada" para sempre; nenhum status novo é necessário,
    /// a mensagem simplesmente permanece <c>Pendente</c> com <c>proxima_tentativa_em</c> no futuro).
    /// </summary>
    private static readonly TimeSpan DuracaoDaPosse = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reivindica até <see cref="OutboxInboxOptions.TamanhoDoLote"/> mensagens <c>Pendente</c> vencidas
    /// com lock de linha real (<c>FOR UPDATE SKIP LOCKED</c>) — nunca um mutex em processo, que não
    /// protegeria contra um segundo host/dispatcher concorrente (spec B3 passo 4/critério 7).
    ///
    /// A reivindicação é UM ÚNICO statement atômico (<c>UPDATE ... FROM (SELECT ... FOR UPDATE SKIP
    /// LOCKED) ... RETURNING</c>): um <c>SELECT ... FOR UPDATE</c> solto, fora de uma transação explícita,
    /// teria seu lock liberado assim que a query terminasse (autocommit do Postgres) — antes mesmo do
    /// dispatcher processar a primeira mensagem — o que reabriria exatamente a janela de corrida entre
    /// dois dispatchers que o <c>SKIP LOCKED</c> deveria fechar. Empurrando <c>proxima_tentativa_em</c>
    /// para <see cref="DuracaoDaPosse"/> no futuro DENTRO do mesmo statement que faz o lock, a "posse" da
    /// linha já está gravada e visível a outras transações no instante em que o lock é liberado.
    /// </summary>
    private async Task<List<OutboxMessage>> ReivindicarLoteAsync(TDbContext db, CancellationToken ct)
    {
        var schema = db.Model.FindEntityType(typeof(OutboxMessage))!.GetSchema();
        var sql = OutboxSql.ReivindicarLote(schema!, DuracaoDaPosse);

        var ids = await db.Database
            .SqlQueryRaw<Guid>(sql, _options.TamanhoDoLote)
            .ToListAsync(ct);

        if (ids.Count == 0) return [];

        return await db.Set<OutboxMessage>()
            .Where(m => ids.Contains(m.Id))
            .ToListAsync(ct);
    }

    private async Task ProcessarMensagemAsync(Guid messageId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var mensagem = await db.Set<OutboxMessage>().FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (mensagem is null) return; // reivindicada e já concluída por outro dispatcher/ciclo — nada a fazer.

        var tipoClr = _registry.ResolverTipoClr(mensagem.TipoEvento);
        if (tipoClr is null)
        {
            await MarcarPoisonAsync(scope, mensagem.Id, $"Tipo de evento não registrado no registry: {mensagem.TipoEvento}", ct);
            return;
        }

        var evento = (EventoIntegracao?)JsonSerializer.Deserialize(mensagem.Payload, tipoClr);
        if (evento is null)
        {
            await MarcarPoisonAsync(scope, mensagem.Id, $"Payload não desserializável para {tipoClr.FullName}.", ct);
            return;
        }

        var ehEventoDePlataforma = _registry.EhEventoDePlataforma(tipoClr);
        var handlers = _registry.HandlersRegistrados(tipoClr);

        var decisao = OutboxDispatchRules.Classificar(mensagem.ContaId, ehEventoDePlataforma, handlers.Count);

        switch (decisao.Tipo)
        {
            case TipoDeDecisaoDeDispatch.PoisonImediato:
                await MarcarPoisonAsync(scope, mensagem.Id, decisao.MotivoDoPoison!, ct);
                return;
            case TipoDeDecisaoDeDispatch.SemHandler:
                await MarcarSemHandlerAsync(scope, mensagem.Id, mensagem.TipoEvento, ct);
                return;
        }

        var contexto = new MensagemContexto(mensagem.Id, mensagem.CorrelationId, mensagem.Tentativas + 1);

        try
        {
            foreach (var tipoHandler in handlers)
            {
                await ExecutarHandlerComDedupeAsync(tipoHandler, tipoClr, evento, contexto, mensagem, decisao.ContaIdParaEscopo, ct);
            }

            await MarcarProcessadaAsync(scope, mensagem.Id, ct);
        }
        catch (Exception ex)
        {
            await RegistrarFalhaAsync(scope, mensagem.Id, mensagem.Tentativas, ex, ct);
        }
    }

    /// <summary>
    /// Executa um handler dentro de sua PRÓPRIA transação: o <c>INSERT</c> de dedupe na Inbox e o efeito
    /// do handler commitam atomicamente (spec B3 passo 4) e a falha de UM handler não desfaz o trabalho
    /// já commitado de outro handler do mesmo evento.
    /// </summary>
    private async Task ExecutarHandlerComDedupeAsync(
        Type tipoHandler, Type tipoEvento, EventoIntegracao evento, MensagemContexto contexto,
        OutboxMessage mensagem, Guid? contaIdParaEscopo, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var tenantScopeFactory = scope.ServiceProvider.GetRequiredService<ITenantScopeFactory>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        var linhasAfetadas = await InserirInboxSeAusente(db, mensagem.Id, tipoHandler.FullName!, mensagem.TipoEvento, ct);
        if (linhasAfetadas == 0)
        {
            // Duplicata: já processada por este handler. Caminho normal do at-least-once — skip silencioso.
            _logger.LogDebug(
                "InboxDuplicata: MessageId={MessageId} Handler={Handler} — efeito já aplicado, pulando.",
                mensagem.Id, tipoHandler.FullName);
            await transacao.CommitAsync(ct);
            return;
        }

        // Tarefa 7 (spec B7 passo 3): abre o escopo de correlação (ICorrelationContext + LogContext + tag
        // no Activity) ANTES de invocar o handler — usando o CorrelationId da MENSAGEM (gravado pelo
        // publisher a partir do ICorrelationContext da requisição original), nunca herdado do ciclo de
        // dispatch anterior. Cada mensagem processada abre e fecha seu PRÓPRIO escopo (CorrelationContext
        // é opcional aqui — pode não estar registrado em composições de DI que não usam AddNfhObservability,
        // ex. testes que só montam Outbox/Inbox isoladamente).
        var correlationContext = scope.ServiceProvider.GetService<CorrelationContext>();
        using var escopoDeCorrelacao = correlationContext?.Definir(contexto.CorrelationId);
        using var escopoDeLogDeCorrelacao = LogContext.PushProperty("CorrelationId", contexto.CorrelationId);
        Activity.Current?.SetTag("correlation_id", contexto.CorrelationId);

        IDisposable? escopoDeTenant = null;
        try
        {
            if (contaIdParaEscopo is { } contaId)
            {
                escopoDeTenant = tenantScopeFactory.BeginTenantScope(contaId);
            }

            var handler = scope.ServiceProvider.GetRequiredService(tipoHandler);
            var handleAsync = tipoHandler.GetInterfaceMap(typeof(IInboxHandler<>).MakeGenericType(tipoEvento)).TargetMethods
                .Single(m => m.Name == nameof(IInboxHandler<EventoIntegracao>.HandleAsync));

            // Invoke() por reflection embrulha exceção SÍNCRONA (handler que lança antes de retornar a
            // Task, ex.: método não-async com validação no topo) em TargetInvocationException — sem
            // desembrulhar aqui, RegistrarFalhaAsync logaria/serializaria o wrapper genérico da reflection
            // em vez da exceção real do handler, dificultando o diagnóstico via erro_ultimo.
            Task tarefa;
            try
            {
                tarefa = (Task)handleAsync.Invoke(handler, [evento, contexto, ct])!;
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw; // inalcançável — Throw() já relança; satisfaz o compilador (Task tarefa não é definitely-assigned sem isso)
            }

            await tarefa;

            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
        }
        finally
        {
            escopoDeTenant?.Dispose();
        }
    }

    private static async Task<int> InserirInboxSeAusente(TDbContext db, Guid messageId, string handler, string tipoEvento, CancellationToken ct)
    {
        var schema = db.Model.FindEntityType(typeof(InboxMessage))!.GetSchema();
        var sql = OutboxSql.InserirInboxSeAusente(schema!);

        return await db.Database.ExecuteSqlRawAsync(sql, [messageId, handler, tipoEvento], ct);
    }

    private async Task MarcarProcessadaAsync(IServiceScope scope, Guid messageId, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Processada)
                .SetProperty(m => m.ProcessadaEm, DateTimeOffset.UtcNow), ct);
    }

    private async Task MarcarSemHandlerAsync(IServiceScope scope, Guid messageId, string tipoEvento, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, OutboxStatus.SemHandler), ct);

        _logger.LogWarning(
            "OutboxSemHandler: nenhum IInboxHandler registrado para o evento. TipoEvento={TipoEvento} MessageId={MessageId}",
            tipoEvento, messageId);
        _metrics.IncrementarSemHandler();
    }

    private async Task MarcarPoisonAsync(IServiceScope scope, Guid messageId, string erro, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Poison)
                .SetProperty(m => m.ErroUltimo, erro), ct);

        _logger.LogError(
            "OutboxPoison: MessageId={MessageId} Erro={Erro}", messageId, erro);
        _metrics.IncrementarPoison();
    }

    private async Task RegistrarFalhaAsync(IServiceScope scope, Guid messageId, int tentativasAnteriores, Exception ex, CancellationToken ct)
    {
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var decisao = OutboxDispatchRules.ClassificarFalha(tentativasAnteriores, _options.MaxTentativas, _options.SeedDeJitter);

        if (decisao.EhPoison)
        {
            await db.Set<OutboxMessage>()
                .Where(m => m.Id == messageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, OutboxStatus.Poison)
                    .SetProperty(m => m.Tentativas, decisao.NovaTentativa)
                    .SetProperty(m => m.ErroUltimo, ex.ToString()), ct);

            _logger.LogError(ex,
                "OutboxPoison: MessageId={MessageId} atingiu MaxTentativas={MaxTentativas}.", messageId, _options.MaxTentativas);
            _metrics.IncrementarPoison();
            return;
        }

        var backoff = decisao.Backoff!.Value;

        await db.Set<OutboxMessage>()
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, OutboxStatus.Pendente)
                .SetProperty(m => m.Tentativas, decisao.NovaTentativa)
                .SetProperty(m => m.ProximaTentativaEm, DateTimeOffset.UtcNow + backoff)
                .SetProperty(m => m.ErroUltimo, ex.ToString()), ct);

        _logger.LogWarning(ex,
            "OutboxRetry: MessageId={MessageId} Tentativa={Tentativa} ProximaTentativaEmSegundos={Segundos}",
            messageId, decisao.NovaTentativa, backoff.TotalSeconds);
    }
}
