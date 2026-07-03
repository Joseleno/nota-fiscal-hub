using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// Critério de aceite 4 (spec B3) — a lição do legado (MSG0005): evento publicado sem NENHUM
/// <see cref="NotaFiscalHub.BuildingBlocks.Messaging.Abstractions.IInboxHandler{T}"/> inscrito termina
/// <see cref="OutboxStatus.SemHandler"/>, NUNCA <see cref="OutboxStatus.Processada"/> — a mensagem
/// permanece consultável e redisparável, nunca desaparece em silêncio.
/// </summary>
public class OrphanEventDispatchTests : IAsyncLifetime
{
    private readonly MessagingTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Dispatcher_EventoSemHandlerRegistrado_TerminaSemHandler_NuncaProcessada()
    {
        await using var provider = _fixture.ConstruirProvedor(_ => { });

        await PublicarEventoSemHandlerRegistrado(provider);

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();
        await dispatcher.ProcessarLoteAsync();

        await using var db = provider.GetRequiredService<MensageriaDbContext>();
        var mensagem = await db.Set<OutboxMessage>().SingleAsync();

        Assert.Equal(OutboxStatus.SemHandler, mensagem.Status);
        Assert.NotEqual(OutboxStatus.Processada, mensagem.Status);
    }

    private async Task PublicarEventoSemHandlerRegistrado(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry>();

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(_contaId);
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(new EventoSemHandlerDeTeste { ContaId = _contaId });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }
}
