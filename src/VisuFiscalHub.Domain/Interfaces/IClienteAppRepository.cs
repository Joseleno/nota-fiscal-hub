using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Interfaces;

public interface IClienteAppRepository
{
    Task<ClienteApp?> GetByClientIdAsync(string clientId, CancellationToken ct = default);
    Task<ClienteApp?> GetByIdAsync(ClienteAppId id, CancellationToken ct = default);
    Task AddAsync(ClienteApp clienteApp, CancellationToken ct = default);
}
