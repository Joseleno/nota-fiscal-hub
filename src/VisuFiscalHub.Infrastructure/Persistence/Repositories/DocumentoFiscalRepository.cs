using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Persistence.Repositories;

public sealed class DocumentoFiscalRepository : IDocumentoFiscalRepository
{
    private readonly ApplicationDbContext _context;

    public DocumentoFiscalRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<DocumentoFiscal?> GetByIdAsync(DocumentoFiscalId id, CancellationToken ct = default)
        => _context.DocumentosFiscais.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

    // Retorna entidade rastreada — usar apenas quando o documento será mutado e persistido.
    public Task<DocumentoFiscal?> GetByIdForUpdateAsync(DocumentoFiscalId id, CancellationToken ct = default)
        => _context.DocumentosFiscais.FirstOrDefaultAsync(d => d.Id == id, ct);

    public Task<DocumentoFiscal?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        TenantId tenantId,
        CancellationToken ct = default)
        => _context.DocumentosFiscais.FirstOrDefaultAsync(
            d => d.IdempotencyKey == idempotencyKey && d.TenantId == tenantId, ct);

    public Task AddAsync(DocumentoFiscal documento, CancellationToken ct = default)
    {
        _context.DocumentosFiscais.Add(documento);
        return Task.CompletedTask;
    }

    // Para entidades rastreadas no mesmo escopo, o change tracker detecta mudanças automaticamente.
    // Update explícito cobre entidades desanexadas (reconstituídas em escopo diferente).
    public Task UpdateAsync(DocumentoFiscal documento, CancellationToken ct = default)
    {
        var entry = _context.Entry(documento);
        if (entry.State == EntityState.Detached)
            _context.DocumentosFiscais.Update(documento);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DocumentoFiscal>> GetProcessandoAntigoAsync(
        DateTimeOffset anteriorA,
        CancellationToken ct = default)
        => await _context.DocumentosFiscais
            .AsNoTracking()
            .Where(d => (d.Status == StatusDocumento.Processando || d.Status == StatusDocumento.Enfileirado)
                        && d.CreatedAt < anteriorA)
            .ToListAsync(ct);
}
