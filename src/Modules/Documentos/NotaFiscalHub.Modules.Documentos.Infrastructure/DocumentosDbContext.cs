using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.Modules.Documentos.Infrastructure;

public sealed class DocumentosDbContext(DbContextOptions<DocumentosDbContext> options) : ModuleDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("documentos");
    }
}
