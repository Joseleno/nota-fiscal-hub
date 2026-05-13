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
        => _context.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<Tenant?> GetByCnpjAsync(Cnpj cnpj, ClienteAppId clienteAppId, CancellationToken ct = default)
        => _context.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Cnpj == cnpj && t.ClienteAppId == clienteAppId, ct);

    public async Task<IReadOnlyList<Tenant>> GetByClienteAppIdAsync(
        ClienteAppId clienteAppId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "Página deve ser >= 1.");

        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize deve ser entre 1 e 100.");

        return await _context.Tenants
            .AsNoTracking()
            .Where(t => t.ClienteAppId == clienteAppId)
            .OrderBy(t => t.RazaoSocial)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

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

    public async Task<long> GetNextNumeracaoAsync(
        TenantId tenantId,
        string serie,
        CancellationToken ct = default)
    {
        // Guard: série deve ser 1-3 dígitos numéricos — mesma regra de ConfiguracaoFiscal
        if (!System.Text.RegularExpressions.Regex.IsMatch(serie, @"^[0-9]{1,3}$"))
            throw new ArgumentException("Série inválida para geração de numeração.", nameof(serie));

        // ToString("N") = UUID hex sem hífens (32 chars) — identificador PostgreSQL válido
        var tenantIdHex = tenantId.Value.ToString("N");
        var sequenceName = $"seq_nfe_{tenantIdHex}_{serie}";

#pragma warning disable EF1002 // nextval requer nome de sequence como literal — parametrização não é suportada pelo PostgreSQL
        return await _context.Database
            .SqlQueryRaw<long>($"SELECT nextval('{sequenceName}')")
            .FirstAsync(ct);
#pragma warning restore EF1002
    }
}
