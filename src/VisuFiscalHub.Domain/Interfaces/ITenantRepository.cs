using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Domain.Interfaces;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken ct = default);
    Task<Tenant?> GetByIdForUpdateAsync(TenantId id, CancellationToken ct = default);
    Task<Tenant?> GetByCnpjAsync(Cnpj cnpj, ClienteAppId clienteAppId, CancellationToken ct = default);
    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> GetPagedByClienteAppIdAsync(
        ClienteAppId clienteAppId,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    Task UpdateAsync(Tenant tenant, CancellationToken ct = default);
}
