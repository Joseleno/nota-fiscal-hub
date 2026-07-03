using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// Critério de aceite 7 (spec B3): dois dispatchers concorrentes (simulando dois hosts do Worker contra o
/// mesmo banco) nunca processam a mesma mensagem 2×. Prova o <c>SELECT ... FOR UPDATE SKIP LOCKED</c> do
/// <see cref="OutboxDispatcher{TDbContext}"/> — um mutex em processo NÃO cobriria este cenário, porque os
/// dois dispatchers abaixo são resolvidos de dois <see cref="IServiceProvider"/> independentes.
/// </summary>
public class OutboxConcurrencyTests : IAsyncLifetime
{
    private readonly MessagingTestFixture _fixture = new();
    private readonly Guid _contaId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task DoisDispatchersConcorrentes_MensagemProcessadaUmaVezSo()
    {
        var contador = new ContadorDeExecucoes();

        // Dois containers de DI INDEPENDENTES — cada um com seu próprio OutboxTypeRegistry e seu próprio
        // OutboxDispatcher — simulando dois processos/hosts distintos do Worker apontando para o mesmo
        // banco. Só a trava real de linha no Postgres (não um lock em memória) pode coordená-los.
        await using var provider1 = _fixture.ConstruirProvedor(services =>
        {
            services.AddSingleton(contador);
            services.AddInboxHandler<EventoDeTeste, HandlerQueIncrementaComAtraso>();
        });
        await using var provider2 = _fixture.ConstruirProvedor(services =>
        {
            services.AddSingleton(contador);
            services.AddInboxHandler<EventoDeTeste, HandlerQueIncrementaComAtraso>();
        });

        await PublicarUmEventoAsync(provider1);

        var dispatcher1 = provider1.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();
        var dispatcher2 = provider2.GetRequiredService<OutboxDispatcher<MensageriaDbContext>>();

        await Task.WhenAll(dispatcher1.ProcessarLoteAsync(), dispatcher2.ProcessarLoteAsync());

        Assert.Equal(1, contador.Total);
    }

    private async Task PublicarUmEventoAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MensageriaDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var registry = scope.ServiceProvider.GetRequiredService<OutboxTypeRegistry>();

        using var _ = ((AmbientTenantContext)tenantContext).BeginTenantScope(_contaId);
        var publisher = new OutboxPublisher<MensageriaDbContext>(db, tenantContext, registry);

        using var transacao = await db.Database.BeginTransactionAsync();
        publisher.Publicar(new EventoDeTeste { ContaId = _contaId, Rotulo = "concorrencia" });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    /// <summary>
    /// Atraso proposital: maximiza a janela em que os dois dispatchers concorrentes disputariam a mesma
    /// linha, tornando o teste um contra-exemplo real (sem o atraso, o segundo dispatcher poderia nunca
    /// alcançar o primeiro e o teste passaria "por sorte", mesmo com <c>SKIP LOCKED</c> ausente).
    /// </summary>
    private sealed class HandlerQueIncrementaComAtraso(ContadorDeExecucoes contador) : IInboxHandler<EventoDeTeste>
    {
        public async Task HandleAsync(EventoDeTeste evento, MensagemContexto ctx, CancellationToken ct)
        {
            await Task.Delay(200, ct);
            contador.Incrementar();
        }
    }
}
