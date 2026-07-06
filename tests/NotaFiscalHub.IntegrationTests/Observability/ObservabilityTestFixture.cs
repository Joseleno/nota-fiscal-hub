using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Observability;
using NotaFiscalHub.BuildingBlocks.Persistence;
using Testcontainers.PostgreSql;

namespace NotaFiscalHub.IntegrationTests.Observability;

/// <summary>
/// DbContext mínimo de teste com o par Outbox/Inbox aplicado no schema <c>teste_obs</c> — usado pelos
/// testes de integração de observabilidade (Tarefa 7) para publicar/despachar eventos sem depender de um
/// módulo de negócio real. Mesmo padrão de <c>MessagingTestFixture</c> (Tarefa 3).
/// </summary>
public sealed class ObsDbContext(DbContextOptions<ObsDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("teste_obs");
        modelBuilder.AplicarOutboxInbox("teste_obs");
    }
}

/// <summary>
/// Fixture compartilhada dos testes de integração de observabilidade (Tarefa 7, spec B7): sobe um
/// PostgreSQL real via Testcontainers (mesmo padrão de <c>MessagingTestFixture</c>/<c>IdempotencyTestFixture</c>),
/// aplica o schema via <c>EnsureCreatedAsync</c> e monta um <see cref="IServiceProvider"/> com Outbox/Inbox
/// + <see cref="ICorrelationContext"/> registrados — a mesma composição usada pelos hosts reais via
/// <c>AddNfhObservability</c>.
///
/// NÃO roda neste ambiente de desenvolvimento (Docker Desktop indisponível — mesma lacuna já documentada
/// nas Tarefas 1/2/3/4/5/6): a fixture falha em <see cref="InitializeAsync"/> na construção/start do
/// <see cref="PostgreSqlContainer"/>.
/// </summary>
public sealed class ObservabilityTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginSystemScope("seed", nameof(ObservabilityTestFixture));
        await using var db = NovoDbContext(tenantContext);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public ObsDbContext NovoDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<ObsDbContext>().UseNpgsql(ConnectionString).Options;
        return new ObsDbContext(options, tenantContext);
    }

    /// <summary>
    /// Monta um <see cref="IServiceProvider"/> com Outbox/Inbox contra <see cref="ObsDbContext"/> e
    /// <see cref="ICorrelationContext"/> registrado — a MESMA peça que <c>AddNfhObservability</c> registra
    /// nos hosts reais, permitindo provar que <c>OutboxPublisher</c> lê o CorrelationId dela (fim da
    /// dívida técnica da Tarefa 3) sem precisar subir o host HTTP completo.
    /// </summary>
    public ServiceProvider ConstruirProvedor(Action<IServiceCollection> registrar)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton(new AmbientTenantContext());
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddSingleton<CorrelationContext>();
        services.AddSingleton<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        services.AddDbContext<ObsDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddOutboxInbox<ObsDbContext>("teste_obs", o =>
        {
            o.SeedDeJitter = 1;
            o.MaxTentativas = 10;
            o.TamanhoDoLote = 100;
        });

        // AddOutboxInbox<TDbContext> registra OutboxDispatcher<TDbContext> só como IHostedService
        // (AddHostedService<T>) — nesta versão do Microsoft.Extensions.Hosting, isso NÃO deixa o tipo
        // concreto resolvível via GetRequiredService<OutboxDispatcher<TDbContext>>() (comportamento
        // confirmado também nos testes pré-existentes da Tarefa 3, ex. InboxDedupeTests — não é uma
        // regressão desta tarefa). Testes que precisam disparar um ciclo de dispatch determinístico
        // (em vez de esperar o timer do BackgroundService) registram o tipo concreto explicitamente aqui.
        services.AddSingleton(sp => sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<OutboxDispatcher<ObsDbContext>>()
            .Single());

        registrar(services);

        return services.BuildServiceProvider();
    }
}
