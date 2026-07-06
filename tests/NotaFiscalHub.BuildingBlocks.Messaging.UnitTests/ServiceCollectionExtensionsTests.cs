using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

/// <summary>
/// <c>AddOutboxInbox&lt;TDbContext&gt;</c> precisa deixar <see cref="OutboxDispatcher{TDbContext}"/> e
/// <see cref="RetentionCleanupService{TDbContext}"/> resolvíveis diretamente via
/// <c>GetRequiredService&lt;T&gt;()</c> — não só como <see cref="IHostedService"/> — porque testes de
/// integração precisam disparar um ciclo determinístico via <c>ProcessarLoteAsync</c>/<c>ExecutarUmCicloAsync</c>
/// em vez de esperar o timer do <see cref="BackgroundService"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOutboxInbox_OutboxDispatcher_ResolveDiretoPeloTipoConcreto()
    {
        var provider = ConstruirProvedor();

        var dispatcher = provider.GetRequiredService<OutboxDispatcher<DbContextDeTeste>>();

        Assert.NotNull(dispatcher);
    }

    [Fact]
    public void AddOutboxInbox_RetentionCleanupService_ResolveDiretoPeloTipoConcreto()
    {
        var provider = ConstruirProvedor();

        var retention = provider.GetRequiredService<RetentionCleanupService<DbContextDeTeste>>();

        Assert.NotNull(retention);
    }

    private static ServiceProvider ConstruirProvedor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DbContextDeTeste>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddOutboxInbox<DbContextDeTeste>("teste_di");
        return services.BuildServiceProvider();
    }

    public sealed class DbContextDeTeste(DbContextOptions<DbContextDeTeste> options) : ModuleDbContext(options);
}
