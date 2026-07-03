using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Persistence;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Kernel.UnitTests.Tenancy;

public class TenantDbContextTests
{
    private static readonly Guid ContaA = Guid.NewGuid();
    private static readonly Guid ContaB = Guid.NewGuid();

    [Fact]
    public void QueryFilter_SemEscopo_LancaAoConsultar()
    {
        var tenantContext = new AmbientTenantContext();
        var dbName = nameof(QueryFilter_SemEscopo_LancaAoConsultar);

        // Semeia ao menos uma linha em escopo de sistema para que o filtro seja
        // efetivamente avaliado (contra coleção vazia o predicado nunca seria invocado).
        using (tenantContext.BeginSystemScope("seed", "teste"))
        {
            using var dbSeed = CriarContextoDeTeste(tenantContext, dbName);
            dbSeed.EntidadesDeTeste.Add(new EntidadeDeTeste { ContaId = ContaA });
            dbSeed.SaveChanges();
        }

        using var db = CriarContextoDeTeste(new AmbientTenantContext(), dbName);
        Assert.Throws<TenantNaoResolvidoException>(() => db.EntidadesDeTeste.ToList());
    }

    [Fact]
    public void QueryFilter_ComEscopo_RetornaSomenteDaContaAtiva()
    {
        var tenantContext = new AmbientTenantContext();
        using (tenantContext.BeginSystemScope("seed", "teste"))
        {
            using var dbSeed = CriarContextoDeTeste(tenantContext, dbName: nameof(QueryFilter_ComEscopo_RetornaSomenteDaContaAtiva));
            dbSeed.EntidadesDeTeste.Add(new EntidadeDeTeste { ContaId = ContaA });
            dbSeed.EntidadesDeTeste.Add(new EntidadeDeTeste { ContaId = ContaB });
            dbSeed.SaveChanges();
        }

        using (tenantContext.BeginTenantScope(ContaA))
        {
            using var db = CriarContextoDeTeste(tenantContext, dbName: nameof(QueryFilter_ComEscopo_RetornaSomenteDaContaAtiva));
            var resultado = db.EntidadesDeTeste.ToList();
            Assert.Single(resultado);
            Assert.Equal(ContaA, resultado[0].ContaId);
        }
    }

    private static ContextoDeTeste CriarContextoDeTeste(ITenantContext tenantContext, string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<ContextoDeTeste>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new ContextoDeTeste(options, tenantContext);
    }

    public sealed class EntidadeDeTeste : ITenantScopedEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ContaId { get; set; }
    }

    public sealed class ContextoDeTeste(DbContextOptions<ContextoDeTeste> options, ITenantContext tenantContext)
        : TenantDbContext(options, tenantContext)
    {
        public DbSet<EntidadeDeTeste> EntidadesDeTeste => Set<EntidadeDeTeste>();
    }
}
