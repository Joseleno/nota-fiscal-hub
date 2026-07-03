using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Persistence;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Kernel.UnitTests.Tenancy;

public class TenantWriteInterceptorTests
{
    private static readonly Guid ContaA = Guid.NewGuid();
    private static readonly Guid ContaB = Guid.NewGuid();

    [Fact]
    public void SaveChanges_EntidadeDeOutraConta_LancaTenantMismatch()
    {
        using var db = CriarContextoDeTeste(EscopoDaContaA(out _));
        db.EntidadesDeTeste.Add(new TenantDbContextTests.EntidadeDeTeste { ContaId = ContaB });
        Assert.Throws<TenantMismatchException>(() => db.SaveChanges());
    }

    [Fact]
    public void SaveChanges_EntidadeNova_EstampaContaIdDoContexto()
    {
        using var db = CriarContextoDeTeste(EscopoDaContaA(out _));
        var entidade = new TenantDbContextTests.EntidadeDeTeste();
        db.EntidadesDeTeste.Add(entidade);
        db.SaveChanges();
        Assert.Equal(ContaA, entidade.ContaId);
    }

    private static ITenantContext EscopoDaContaA(out ITenantScope escopo)
    {
        var tenantContext = new AmbientTenantContext();
        escopo = tenantContext.BeginTenantScope(ContaA);
        return tenantContext;
    }

    private static TenantDbContextTests.ContextoDeTeste CriarContextoDeTeste(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<TenantDbContextTests.ContextoDeTeste>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TenantDbContextTests.ContextoDeTeste(options, tenantContext);
    }
}
