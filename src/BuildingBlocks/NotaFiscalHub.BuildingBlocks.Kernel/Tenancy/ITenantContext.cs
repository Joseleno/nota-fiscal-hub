namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

/// <summary>
/// Representa o tenant ambiente da execução corrente (request HTTP, job do Worker, etc.).
/// Fail-closed: ler <see cref="ContaId"/> sem escopo ativo lança <see cref="TenantNaoResolvidoException"/>
/// em vez de retornar um valor "vazio" que poderia ser mal interpretado como "todos os tenants".
/// </summary>
public interface ITenantContext
{
    Guid ContaId { get; }
    bool HasTenant { get; }
    bool IsSystemScope { get; }
    IReadOnlyCollection<Guid>? EmpresasPermitidas { get; }
}
