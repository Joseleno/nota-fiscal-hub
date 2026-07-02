using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Persistence.Repositories;

public sealed class ClienteAppRepository : IClienteAppRepository
{
    private readonly ApplicationDbContext _context;

    public ClienteAppRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ClienteApp?> GetByIdAsync(ClienteAppId id, CancellationToken ct = default)
        => _context.ClienteApps.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<ClienteApp?> GetByClientIdAsync(string clientId, CancellationToken ct = default)
        => _context.ClienteApps
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId, ct);

    public Task AddAsync(ClienteApp clienteApp, CancellationToken ct = default)
    {
        _context.ClienteApps.Add(clienteApp);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ClienteApp clienteApp, CancellationToken ct = default)
    {
        var entry = _context.Entry(clienteApp);
        if (entry.State == EntityState.Detached)
            _context.ClienteApps.Update(clienteApp);
        return Task.CompletedTask;
    }
}
