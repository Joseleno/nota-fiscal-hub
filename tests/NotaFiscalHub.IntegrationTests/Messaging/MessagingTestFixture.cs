using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Persistence;
using Testcontainers.PostgreSql;

namespace NotaFiscalHub.IntegrationTests.Messaging;

/// <summary>
/// DbContext mínimo de teste com o par Outbox/Inbox aplicado no schema <c>teste_msg</c> — usado por todos
/// os testes de integração da biblioteca de mensageria (Tarefa 3) em vez de um DbContext de módulo real,
/// para isolar o teste de detalhes de domínio de qualquer módulo específico.
/// </summary>
public sealed class MensageriaDbContext(DbContextOptions<MensageriaDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("teste_msg");
        modelBuilder.AplicarOutboxInbox("teste_msg");
    }
}

/// <summary>
/// Fixture compartilhada: sobe um PostgreSQL real via Testcontainers, aplica o schema via
/// <c>EnsureCreatedAsync</c> (mesmo padrão de <c>TenantIsolationTests</c>, Tarefa 2) e expõe um
/// <see cref="IServiceProvider"/> configurável por teste via <see cref="ConstruirProvedor"/>.
/// </summary>
public sealed class MessagingTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginSystemScope("seed", nameof(MessagingTestFixture));
        await using var db = NovoDbContext(tenantContext);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public MensageriaDbContext NovoDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<MensageriaDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new MensageriaDbContext(options, tenantContext);
    }

    /// <summary>
    /// Monta um <see cref="IServiceProvider"/> com a infraestrutura de Outbox/Inbox registrada contra
    /// <see cref="MensageriaDbContext"/>. <paramref name="registrar"/> permite que cada teste inscreva
    /// seus próprios handlers/eventos de plataforma antes do provider ser construído.
    /// </summary>
    public ServiceProvider ConstruirProvedor(Action<IServiceCollection> registrar, int seedDeJitter = 1, int maxTentativas = 10)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton(new AmbientTenantContext());
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddDbContext<MensageriaDbContext>(o => o.UseNpgsql(ConnectionString));

        services.AddOutboxInbox<MensageriaDbContext>("teste_msg", o =>
        {
            o.SeedDeJitter = seedDeJitter;
            o.MaxTentativas = maxTentativas;
            o.TamanhoDoLote = 100;
        });

        registrar(services);

        return services.BuildServiceProvider();
    }
}
