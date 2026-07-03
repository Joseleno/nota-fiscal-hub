using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.Modules.EmpresasCertificados.Infrastructure;

public sealed class EmpresasCertificadosDbContext(DbContextOptions<EmpresasCertificadosDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("empresas");
    }
}
