namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

/// <summary>
/// Abre escopos de tenant. <see cref="BeginTenantScope"/> é o caminho normal (request HTTP,
/// job do Worker executando para uma conta específica). <see cref="BeginSystemScope"/> é o
/// bypass explícito para trabalho de sistema (migrations, seeds) — nunca silencioso, sempre
/// auditável via log estruturado.
/// </summary>
public interface ITenantScopeFactory
{
    ITenantScope BeginTenantScope(Guid contaId);
    ITenantScope BeginSystemScope(string motivo, string origem);
}
