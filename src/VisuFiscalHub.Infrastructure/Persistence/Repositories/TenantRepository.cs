using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Persistence.Repositories;

public sealed class TenantRepository : ITenantRepository
{
    private readonly ApplicationDbContext _context;

    public TenantRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken ct = default)
        => _context.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetByCnpjAsync(Cnpj cnpj, ClienteAppId clienteAppId, CancellationToken ct = default)
        => _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Cnpj == cnpj && t.ClienteAppId == clienteAppId, ct);

    public async Task<IReadOnlyList<Tenant>> GetByClienteAppIdAsync(
        ClienteAppId clienteAppId,
        int page,
        int pageSize,
        CancellationToken ct = default)
        => await _context.Tenants
            .AsNoTracking()
            .Where(t => t.ClienteAppId == clienteAppId)
            .OrderBy(t => t.RazaoSocial)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public Task<int> CountByClienteAppIdAsync(ClienteAppId clienteAppId, CancellationToken ct = default)
        => _context.Tenants
            .AsNoTracking()
            .CountAsync(t => t.ClienteAppId == clienteAppId, ct);

    public Task AddAsync(Tenant tenant, CancellationToken ct = default)
    {
        _context.Tenants.Add(tenant);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Tenant tenant, CancellationToken ct = default)
    {
        var entry = _context.Entry(tenant);
        if (entry.State == EntityState.Detached)
            _context.Tenants.Update(tenant);
        return Task.CompletedTask;
    }
}
