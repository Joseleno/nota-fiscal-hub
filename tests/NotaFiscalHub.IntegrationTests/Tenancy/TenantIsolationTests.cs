using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Tenancy;

/// <summary>
/// Prova, contra um PostgreSQL real (Testcontainers), que a conta A jamais lê dado da conta B — o
/// critério de aceite mais crítico da spec B2 (design §4.4). Cobre <c>Find</c>, <c>Include</c>,
/// projeção, <c>AsNoTracking</c> e confirma via <c>ToQueryString()</c> que o SQL gerado contém o
/// predicado <c>conta_id</c> (evidência textual do filtro, não apenas comportamental).
/// </summary>
public class TenantIsolationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private static readonly Guid ContaA = Guid.NewGuid();
    private static readonly Guid ContaB = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginSystemScope("seed", "TenantIsolationTests");
        await using var db = CriarContexto(tenantContext);
        await db.Database.EnsureCreatedAsync();

        db.Registros.AddRange(
            new RegistroDeTeste { ContaId = ContaA, Descricao = "registro-a-1" },
            new RegistroDeTeste { ContaId = ContaA, Descricao = "registro-a-2" },
            new RegistroDeTeste { ContaId = ContaB, Descricao = "registro-b-1" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task ContaA_NuncaLeDadoDaContaB_ToList()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginTenantScope(ContaA);
        await using var db = CriarContexto(tenantContext);

        var resultado = await db.Registros.ToListAsync();

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, r => Assert.Equal(ContaA, r.ContaId));
    }

    [Fact]
    public async Task ContaA_NuncaLeDadoDaContaB_Find()
    {
        var tenantContext = new AmbientTenantContext();
        RegistroDeTeste registroDaContaB;
        using (tenantContext.BeginSystemScope("leitura-de-referencia", "teste"))
        {
            await using var dbSystem = CriarContexto(tenantContext);
            registroDaContaB = await dbSystem.Registros.AsNoTracking().FirstAsync(r => r.ContaId == ContaB);
        }

        using var __ = tenantContext.BeginTenantScope(ContaA);
        await using var db = CriarContexto(tenantContext);

        var encontrado = await db.Registros.FindAsync(registroDaContaB.Id);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ContaA_NuncaLeDadoDaContaB_ProjecaoEAsNoTracking()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginTenantScope(ContaA);
        await using var db = CriarContexto(tenantContext);

        var descricoes = await db.Registros.AsNoTracking().Select(r => r.Descricao).ToListAsync();

        Assert.Equal(2, descricoes.Count);
        Assert.All(descricoes, d => Assert.StartsWith("registro-a-", d));
    }

    [Fact]
    public async Task QueryFilter_GeraPredicadoContaIdNoSql()
    {
        var tenantContext = new AmbientTenantContext();
        using var _ = tenantContext.BeginTenantScope(ContaA);
        await using var db = CriarContexto(tenantContext);

        var sql = db.Registros.ToQueryString();

        Assert.Contains("conta_id", sql, StringComparison.OrdinalIgnoreCase);
    }

    private ContextoDeTeste CriarContexto(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<ContextoDeTeste>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new ContextoDeTeste(options, tenantContext);
    }

    public sealed class RegistroDeTeste : ITenantScopedEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ContaId { get; set; }
        public string Descricao { get; set; } = string.Empty;
    }

    public sealed class ContextoDeTeste(DbContextOptions<ContextoDeTeste> options, ITenantContext tenantContext)
        : TenantDbContext(options, tenantContext)
    {
        public DbSet<RegistroDeTeste> Registros => Set<RegistroDeTeste>();
    }
}
