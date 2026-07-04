using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.BuildingBlocks.Auditoria;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;
using NotaFiscalHub.BuildingBlocks.Persistence;
using Testcontainers.PostgreSql;

namespace NotaFiscalHub.IntegrationTests.Auditoria;

/// <summary>
/// DbContext mínimo de teste que combina o par Outbox/Inbox (schema <c>teste_auditoria_outbox</c>, papel de
/// "módulo produtor" simulado) com <see cref="AuditoriaDbContext"/> não podem compartilhar tabelas — por
/// isso este DbContext existe só para PUBLICAR o evento via outbox; a consulta/persistência da trilha em
/// si acontece em <see cref="AuditoriaDbContext"/>, separado (mesma separação de bancos/contextos do mundo
/// real: cada módulo publica no seu schema, o handler de auditoria escreve no schema <c>auditoria</c>).
/// </summary>
public sealed class ModuloProdutorDbContext(DbContextOptions<ModuloProdutorDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("teste_auditoria_outbox");
        modelBuilder.AplicarOutboxInbox("teste_auditoria_outbox");
    }
}

/// <summary>
/// Fixture compartilhada dos testes de integração de Auditoria (Tarefa 5, spec B5): sobe um PostgreSQL
/// real via Testcontainers (mesmo padrão de <c>MessagingTestFixture</c>, Tarefa 3), aplica o schema de
/// <see cref="ModuloProdutorDbContext"/> (outbox/inbox do "módulo produtor" simulado) E de
/// <see cref="AuditoriaDbContext"/> via <c>EnsureCreatedAsync</c>.
///
/// NÃO roda neste ambiente de desenvolvimento (Docker Desktop indisponível — mesma lacuna já documentada
/// nas Tarefas 1/2/3/4/6): a fixture falha em <see cref="InitializeAsync"/> na construção/start do
/// <see cref="PostgreSqlContainer"/>.
///
/// IMPORTANTE: <c>EnsureCreatedAsync</c> NÃO aplica migrations (não roda o trigger/REVOKE do Step 4) — só
/// cria as tabelas a partir do model. <see cref="ImmutabilityTests"/> precisa da migration real aplicada
/// (<c>MigrateAsync</c>) para o trigger existir; os demais testes desta pasta usam <c>EnsureCreatedAsync</c>
/// (mais rápido, suficiente para dedupe/tenant-isolation/PII).
/// </summary>
public sealed class AuditoriaTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginSystemScope("seed", nameof(AuditoriaTestFixture));

        await using var dbProdutor = NovoModuloProdutorDbContext(tenantContext);
        await dbProdutor.Database.EnsureCreatedAsync();

        await using var dbAuditoria = NovoAuditoriaDbContext();
        await dbAuditoria.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    public ModuloProdutorDbContext NovoModuloProdutorDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<ModuloProdutorDbContext>().UseNpgsql(ConnectionString).Options;
        return new ModuloProdutorDbContext(options, tenantContext);
    }

    public AuditoriaDbContext NovoAuditoriaDbContext()
    {
        var options = new DbContextOptionsBuilder<AuditoriaDbContext>().UseNpgsql(ConnectionString).Options;
        return new AuditoriaDbContext(options);
    }

    /// <summary>
    /// Aplica as migrations reais (não <c>EnsureCreatedAsync</c>) — necessário para
    /// <see cref="ImmutabilityTests"/> exercitar o trigger de bloqueio (Step 4), que só existe via
    /// <c>migrationBuilder.Sql(...)</c>, nunca via <c>EnsureCreatedAsync</c> (que só lê o model, não as
    /// migrations).
    /// </summary>
    public async Task AplicarMigrationsReaisAsync()
    {
        await using var db = NovoAuditoriaDbContext();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Monta um <see cref="IServiceProvider"/> com a infraestrutura de Outbox/Inbox (contra
    /// <see cref="ModuloProdutorDbContext"/>) e de Auditoria (contra <see cref="AuditoriaDbContext"/>)
    /// registradas — mesmo padrão de <c>MessagingTestFixture.ConstruirProvedor</c>.
    /// </summary>
    public ServiceProvider ConstruirProvedor()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton(new AmbientTenantContext());
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());
        services.AddSingleton<ITenantScopeFactory>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddDbContext<ModuloProdutorDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddOutboxInbox<ModuloProdutorDbContext>("teste_auditoria_outbox", o =>
        {
            o.SeedDeJitter = 1;
            o.MaxTentativas = 10;
            o.TamanhoDoLote = 100;
        });

        // AuditoriaDbContext precisa ser resolvido a partir do MESMO DbContext usado pelo
        // OutboxDispatcher<ModuloProdutorDbContext> para o handler poder ser injetado no escopo correto —
        // ambos coexistem no mesmo IServiceCollection (mesma composição do host real, onde o Worker
        // referencia todos os módulos + a fundação de auditoria no mesmo processo).
        services.AddDbContext<AuditoriaDbContext>(o => o.UseNpgsql(ConnectionString));
        services.AddAuditoria();

        // AddAuditoria() só inscreve AuditoriaEventHandler para os 6 eventos REAIS/sintéticos do catálogo
        // (ver comentário de classe de AuditoriaEventHandler: handlers são resolvidos por tipo CLR
        // concreto, não por EventoIntegracao base). Os test-doubles do brief precisam do MESMO registro
        // explícito contra ModuloProdutorDbContext, que é o DbContext que efetivamente publica/despacha
        // nestes testes.
        services.AddInboxHandler<ModuloProdutorDbContext, ApiKeyRevogadaDeTeste, AuditoriaEventHandler>();
        services.AddInboxHandler<ModuloProdutorDbContext, EventoComCpfDeTeste, AuditoriaEventHandler>();
        // EventoNaoCatalogadoDeTeste deliberadamente NÃO tem handler registrado no dispatcher — mas mesmo
        // que tivesse, o catálogo o rejeitaria (retorna false). Ver ConsumidoSemErro_SemRegistro.

        return services.BuildServiceProvider();
    }
}
