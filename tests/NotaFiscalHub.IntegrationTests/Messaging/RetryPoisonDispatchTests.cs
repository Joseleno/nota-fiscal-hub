using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// Critério de aceite 3 (spec B3): handler que lança exceção repetidamente faz a mensagem retornar a
/// <c>Pendente</c> com backoff crescente entre tentativas; na 10ª falha (default <c>MaxTentativas</c>)
/// vira <see cref="OutboxStatus.Poison"/> — e as DEMAIS mensagens do lote continuam sendo processadas
/// normalmente (poison de uma mensagem nunca aborta a fila).
/// </summary>
public class RetryPoisonDispatchTests : IAsyncLifetime
{
    private readonly MessagingTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Handler_FalhaRepetidamente_10aTentativaViraPoison_FilaContinua()
    {
        await using var provider = _fixture.ConstruirProvedor(services =>
        {
            services.AddInboxHandler<MensageriaDbContext, EventoDeTeste, HandlerQueSempreLanca>();
            services.AddInboxHandler<MensageriaDbContext, OutroEventoDeTeste, HandlerOk>();
        }, seedDeJitter: 123, maxTentativas: 10);

        var idA = await PublicarAsync(provider, new EventoDeTeste { ContaId = _contaId, Rotulo = "A" });
        var idB = await PublicarAsync(provider, new OutroEventoDeTeste { ContaId = _contaId, Rotulo = "B" });

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();

        // A "vencida" precisa ficar imediatamente re-elegível entre ciclos: como o backoff real (>=5s)
        // tornaria o teste lento, cada ciclo força proxima_tentativa_em de volta para "agora" antes de
        // reprocessar — isolando exclusivamente a contagem de tentativas/transição de estado (o valor do
        // backoff em si já está coberto por BackoffCalculatorTests, unitário e determinístico).
        for (var i = 0; i < 10; i++)
        {
            await dispatcher.ProcessarLoteAsync();
            await ForcarElegibilidadeImediataAsync(provider, idA);
        }

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        var mensagemA = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == idA);
        var mensagemB = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == idB);

        Assert.Equal(OutboxStatus.Poison, mensagemA.Status);
        Assert.Equal(OutboxStatus.Processada, mensagemB.Status);
    }

    [Fact]
    public async Task Handler_Falha_VoltaParaPendenteComProximaTentativaFutura()
    {
        await using var provider = _fixture.ConstruirProvedor(services =>
        {
            services.AddInboxHandler<MensageriaDbContext, EventoDeTeste, HandlerQueSempreLanca>();
        }, seedDeJitter: 7, maxTentativas: 10);

        var id = await PublicarAsync(provider, new EventoDeTeste { ContaId = _contaId, Rotulo = "retry-unico" });

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();
        var antes = DateTimeOffset.UtcNow;
        await dispatcher.ProcessarLoteAsync();

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        var mensagem = await db.Set<OutboxMessage>().SingleAsync(m => m.Id == id);

        Assert.Equal(OutboxStatus.Pendente, mensagem.Status);
        Assert.Equal(1, mensagem.Tentativas);
        Assert.True(mensagem.ProximaTentativaEm > antes.AddSeconds(4)); // backoff base 5s (BackoffCalculator)
        Assert.NotNull(mensagem.ErroUltimo);
    }

    private async Task<Guid> PublicarAsync<T>(IServiceProvider provider, T evento) where T : EventoIntegracao
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry<MensageriaDbContext>>();
        var correlationContext = scope.ServiceProvider.GetRequiredService<NotaFiscalHub.BuildingBlocks.Observability.CorrelationContext>();
        using var escopoDeCorrelacao = correlationContext.Definir("corr-teste-integracao");

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(_contaId);
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry, correlationContext);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(evento);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        return evento.MessageId;
    }

    private static async Task ForcarElegibilidadeImediataAsync(IServiceProvider provider, Guid messageId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();

        await db.Set<OutboxMessage>()
            .Where(m => m.Id == messageId && m.Status == OutboxStatus.Pendente)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProximaTentativaEm, DateTimeOffset.UtcNow));
    }

    private sealed class HandlerQueSempreLanca : IInboxHandler<EventoDeTeste>
    {
        public Task HandleAsync(EventoDeTeste evento, MensagemContexto ctx, CancellationToken ct) =>
            throw new InvalidOperationException("Falha proposital do teste de retry/poison.");
    }

    private sealed class HandlerOk : IInboxHandler<OutroEventoDeTeste>
    {
        public Task HandleAsync(OutroEventoDeTeste evento, MensagemContexto ctx, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
