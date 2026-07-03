using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

namespace NotaFiscalHub.BuildingBlocks.Persistence;

/// <summary>
/// Segunda linha de defesa do isolamento de tenant (a primeira é o query filter global).
/// Em <see cref="EntityState.Added"/>, estampa <c>ContaId</c> a partir do contexto ambiente quando
/// a entidade ainda não tem um valor (<see cref="Guid.Empty"/>). Em qualquer estado de escrita, fora
/// de escopo de sistema, uma entidade cujo <c>ContaId</c> diverge do tenant ativo faz o <c>SaveChanges</c>
/// falhar com <see cref="TenantMismatchException"/> antes de qualquer I/O — nada é persistido.
/// Em escopo de sistema o interceptor não estampa nada: código de sistema deve fornecer o <c>ContaId</c>
/// explicitamente (nunca existe um "ContaId do sistema").
/// </summary>
public sealed class TenantWriteInterceptor(ITenantContext tenantContext) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AplicarRegrasDeTenant(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        AplicarRegrasDeTenant(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AplicarRegrasDeTenant(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not ITenantScopedEntity) continue;
            if (entry.State is EntityState.Detached or EntityState.Unchanged) continue;

            AplicarRegraNaEntrada(entry);
        }
    }

    private void AplicarRegraNaEntrada(EntityEntry entry)
    {
        var contaIdProperty = entry.Property(nameof(ITenantScopedEntity.ContaId));
        var contaIdAtual = (Guid)contaIdProperty.CurrentValue!;

        if (tenantContext.IsSystemScope)
        {
            // Escopo de sistema não estampa: exige ContaId explícito, já validado pelo código de sistema.
            return;
        }

        if (entry.State == EntityState.Added && contaIdAtual == Guid.Empty)
        {
            contaIdProperty.CurrentValue = tenantContext.ContaId;
            return;
        }

        var contaIdEsperada = tenantContext.ContaId;
        if (contaIdAtual != contaIdEsperada)
        {
            throw new TenantMismatchException(contaIdEsperada, contaIdAtual);
        }
    }
}
