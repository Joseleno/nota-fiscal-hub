using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.Modules.ContasPlanos.Infrastructure;

public sealed class ContasPlanosDbContext(DbContextOptions<ContasPlanosDbContext> options) : ModuleDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("contas");
    }
}
