using Microsoft.EntityFrameworkCore;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using NotaFiscalHub.BuildingBlocks.Messaging;
using NotaFiscalHub.BuildingBlocks.Persistence;

namespace NotaFiscalHub.Modules.Emissao.Infrastructure;

public sealed class EmissaoDbContext(DbContextOptions<EmissaoDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    // O schema "leitura" NÃO nasce aqui: pertence à Emissão (D-2026-07-01-10) e sua migration
    // chega apenas com a entidade NotaConsulta, na fase correspondente.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("emissao");
        modelBuilder.AplicarOutboxInbox("emissao");
    }
}
