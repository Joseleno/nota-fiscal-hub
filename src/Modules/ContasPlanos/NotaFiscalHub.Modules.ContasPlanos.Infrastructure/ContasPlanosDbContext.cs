using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.Modules.ContasPlanos.Infrastructure;

public sealed class ContasPlanosDbContext(DbContextOptions<ContasPlanosDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("contas");
    }
}
