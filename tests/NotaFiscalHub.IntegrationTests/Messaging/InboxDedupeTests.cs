using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// Critério de aceite 2 (spec B3): mesma mensagem despachada 2× ⇒ handler executa o efeito 1× (dedupe
/// via <c>INSERT ... ON CONFLICT DO NOTHING</c> na Inbox — nunca "check-then-insert", que teria condição
/// de corrida). Com 2 handlers inscritos no mesmo evento, cada um executa 1× independentemente.
/// </summary>
public class InboxDedupeTests : IAsyncLifetime
{
    private readonly MessagingTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Handler_MesmaMensagem2Vezes_EfeitoExecutaUmaVez()
    {
        var contador = new ContadorDeExecucoes();

        await using var provider = _fixture.ConstruirProvedor(services =>
        {
            services.AddSingleton(contador);
            services.AddInboxHandler<MensageriaDbContext, EventoDeTeste, HandlerQueIncrementa>();
        });

        var messageId = await PublicarEventoAsync(provider);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();

        // 1º despacho: processa e marca Processada.
        await dispatcher.ProcessarLoteAsync();

        // Simula reentrega: devolve a mensagem para Pendente e imediatamente elegível (nunca deveria
        // acontecer em produção via fluxo normal, mas é exatamente o cenário que o dedupe da Inbox
        // precisa cobrir — crash entre commit do handler e marcação como Processada, ou redisparo manual
        // via runbook). ProximaTentativaEm também precisa voltar para "agora": o 1º despacho já a
        // empurrou ~30s para o futuro como parte da reivindicação atômica (lease de posse contra dois
        // dispatchers concorrentes) — sem resetar aqui, o 2º ProcessarLoteAsync não reivindicaria nada.
        await using (var db = provider.GetRequiredService<MensageriaDbContext>())
        {
            await db.Set<OutboxMessage>()
                .Where(m => m.Id == messageId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, OutboxStatus.Pendente)
                    .SetProperty(m => m.ProximaTentativaEm, DateTimeOffset.UtcNow));
        }

        // 2º despacho: Inbox já tem (messageId, handler) — dedupe deve pular a execução do handler.
        await dispatcher.ProcessarLoteAsync();

        Assert.Equal(1, contador.Total);
    }

    [Fact]
    public async Task Handler_DoisHandlersInscritos_CadaUmExecutaUmaVez()
    {
        var contadorA = new ContadorDeExecucoes();
        var contadorB = new ContadorDeExecucoes();

        await using var provider = _fixture.ConstruirProvedor(services =>
        {
            services.AddKeyedSingleton("A", contadorA);
            services.AddKeyedSingleton("B", contadorB);
            services.AddScoped<HandlerA>(sp => new HandlerA(sp.GetRequiredKeyedService<ContadorDeExecucoes>("A")));
            services.AddScoped<HandlerB>(sp => new HandlerB(sp.GetRequiredKeyedService<ContadorDeExecucoes>("B")));
            services.AddInboxHandler<MensageriaDbContext, EventoDeTeste, HandlerA>();
            services.AddInboxHandler<MensageriaDbContext, EventoDeTeste, HandlerB>();
        });

        await PublicarEventoAsync(provider);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        Assert.Equal(1, contadorA.Total);
        Assert.Equal(1, contadorB.Total);
    }

    private async Task<Guid> PublicarEventoAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry<MensageriaDbContext>>();
        var correlationContext = scope.ServiceProvider.GetRequiredService<NotaFiscalHub.BuildingBlocks.Observability.CorrelationContext>();
        using var escopoDeCorrelacao = correlationContext.Definir("corr-teste-integracao");

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(_contaId);
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry, correlationContext);

        var evento = new EventoDeTeste { ContaId = _contaId, Rotulo = "dedupe" };

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(evento);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        return evento.MessageId;
    }

    private sealed class HandlerQueIncrementa(ContadorDeExecucoes contador) : IInboxHandler<EventoDeTeste>
    {
        public Task HandleAsync(EventoDeTeste evento, MensagemContexto ctx, CancellationToken ct)
        {
            contador.Incrementar();
            return Task.CompletedTask;
        }
    }

    private sealed class HandlerA(ContadorDeExecucoes contador) : IInboxHandler<EventoDeTeste>
    {
        public Task HandleAsync(EventoDeTeste evento, MensagemContexto ctx, CancellationToken ct)
        {
            contador.Incrementar();
            return Task.CompletedTask;
        }
    }

    private sealed class HandlerB(ContadorDeExecucoes contador) : IInboxHandler<EventoDeTeste>
    {
        public Task HandleAsync(EventoDeTeste evento, MensagemContexto ctx, CancellationToken ct)
        {
            contador.Incrementar();
            return Task.CompletedTask;
        }
    }
}
