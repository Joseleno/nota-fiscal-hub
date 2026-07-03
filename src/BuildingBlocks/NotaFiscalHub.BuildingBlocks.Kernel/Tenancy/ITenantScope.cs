namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

/// <summary>
/// Escopo de tenant aberto por <see cref="ITenantScopeFactory"/>. Dispose restaura o
/// estado de tenant anterior (permite aninhamento correto de escopos).
/// </summary>
public interface ITenantScope : IDisposable;
