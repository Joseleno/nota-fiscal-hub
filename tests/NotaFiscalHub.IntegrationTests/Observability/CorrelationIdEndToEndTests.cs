using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using NotaFiscalHub.BuildingBlocks.Observability;
using Serilog;
using Serilog.Context;
using Serilog.Sinks.InMemory;

namespace NotaFiscalHub.IntegrationTests.Observability;

/// <summary>
/// T4 (spec B7) — prova que o CorrelationId atravessa API→outbox→handler com o MESMO valor, e que é
/// exatamente essa mudança que resolve a dívida técnica da Tarefa 3
/// (<c>OutboxPublisher.CorrelationIdAtual</c> antes usava <c>Activity.Current?.RootId</c> direto; agora lê
/// de <see cref="ICorrelationContext"/> injetado).
///
/// Requer PostgreSQL real via Testcontainers/Docker. Docker estava disponível neste ambiente quando este
/// teste foi escrito e verificado — ele roda de fato e passa contra o container real, ao contrário da
/// lacuna documentada nas Tarefas 1-6.
/// </summary>
public class CorrelationIdEndToEndTests : IClassFixture<ObservabilityTestFixture>
{
    private readonly ObservabilityTestFixture _fixture;

    public CorrelationIdEndToEndTests(ObservabilityTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CorrelationId_AtravessaOutboxEHandler_MesmoValorNosDois()
    {
        const string correlationId = "corr-teste-123";
        string? correlationIdObservadoNoHandler = null;

        // Logger LOCAL (não Log.Logger estático) — Log.Logger é um singleton por processo e este teste
        // roda em paralelo com outros da mesma suíte (xUnit paraleliza por classe por padrão); mutar o
        // estático causaria corrida entre testes. InMemorySink.Instance ainda é global (a própria
        // biblioteca Serilog.Sinks.InMemory não oferece uma instância isolada por logger), mas com o
        // logger local isolamos ao menos QUAL configuração de enrichment está ativa durante este teste.
        var sinkLocal = new InMemorySink();
        using var loggerLocal = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sinkLocal)
            .CreateLogger();

        var contaId = Guid.NewGuid();

        using var provider = _fixture.ConstruirProvedor(services =>
        {
            // Liga o ILoggerFactory resolvido via DI ao logger local acima — sem isso, o
            // HandlerDeTesteQueObservaCorrelationId (que usa ILogger<T> injetado) nunca alcançaria o sink.
            services.AddLogging(builder => builder.AddSerilog(loggerLocal, dispose: false));
            services.AddSingleton(new HandlerObservado(ctx => correlationIdObservadoNoHandler = ctx.CorrelationId));
            services.AddInboxHandler<ObsDbContext, EventoDeTesteObs, HandlerDeTesteQueObservaCorrelationId>();
        });

        // Simula a borda HTTP: o middleware (CorrelationIdMiddleware) definiria o CorrelationId no
        // ICorrelationContext + LogContext ANTES do handler/controller rodar — replicamos as duas partes
        // aqui manualmente, já que este teste não sobe um host HTTP completo.
        var correlationContext = provider.GetRequiredService<CorrelationContext>();
        using var escopoDeCorrelacao = correlationContext.Definir(correlationId);
        using var escopoDeLog = LogContext.PushProperty("CorrelationId", correlationId);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IOutboxPublisher>();

            using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(contaId);
            using var transacao = await db.Database.BeginTransactionAsync();
            publisher.Publicar(new EventoDeTesteObs { ContaId = contaId });
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
        }

        // Ponto crítico: o publisher rodou DENTRO do escopo de correlationId "corr-teste-123" — se a
        // dívida técnica da Tarefa 3 não tivesse sido resolvida (Activity.Current?.RootId em vez de
        // ICorrelationContext), este valor não teria relação alguma com o CorrelationId real da requisição.
        var dispatcher = provider.GetRequiredService<OutboxDispatcher<ObsDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        Assert.Equal(correlationId, correlationIdObservadoNoHandler);
        Assert.Contains(sinkLocal.LogEvents, e =>
            e.Properties.TryGetValue("CorrelationId", out var valor) && valor.ToString().Contains(correlationId));
    }
}

public sealed record EventoDeTesteObs : EventoIntegracao;

/// <summary>Callback que registra o CorrelationId visto pelo handler no momento em que ele executa.</summary>
public sealed class HandlerObservado(Action<MensagemContexto> aoReceber)
{
    public void Observar(MensagemContexto ctx) => aoReceber(ctx);
}

/// <summary>
/// Handler de teste que só repassa o <see cref="MensagemContexto"/> recebido para
/// <see cref="HandlerObservado"/> — prova que o CorrelationId da mensagem (gravado pelo publisher a partir
/// de <see cref="ICorrelationContext"/>) chega intacto ao handler via <c>OutboxDispatcher</c>.
/// </summary>
public sealed class HandlerDeTesteQueObservaCorrelationId(HandlerObservado observador) : IInboxHandler<EventoDeTesteObs>
{
    public Task HandleAsync(EventoDeTesteObs evento, MensagemContexto ctx, CancellationToken ct)
    {
        observador.Observar(ctx);
        return Task.CompletedTask;
    }
}
