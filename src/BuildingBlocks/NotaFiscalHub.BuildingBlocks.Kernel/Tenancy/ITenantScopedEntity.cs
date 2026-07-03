namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

/// <summary>
/// Marker que toda entidade persistida em tabela tenant-scoped deve implementar.
/// Usado pelo <c>TenantDbContext</c> para aplicar o filtro global de query e pelo
/// teste de arquitetura <c>TenantScoped_ExigeContaId</c> para garantir que nenhuma
/// entidade nova escape do isolamento por tenant sem entrar deliberadamente na allowlist.
/// </summary>
public interface ITenantScopedEntity
{
    Guid ContaId { get; }
}
