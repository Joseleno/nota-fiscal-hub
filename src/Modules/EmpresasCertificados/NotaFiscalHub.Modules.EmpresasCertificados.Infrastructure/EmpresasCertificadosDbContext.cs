using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.Modules.EmpresasCertificados.Infrastructure;

public sealed class EmpresasCertificadosDbContext(DbContextOptions<EmpresasCertificadosDbContext> options) : ModuleDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("empresas");
    }
}
