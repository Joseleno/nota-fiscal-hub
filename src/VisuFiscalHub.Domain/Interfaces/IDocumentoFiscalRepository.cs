using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Interfaces;

public interface IDocumentoFiscalRepository
{
    Task<DocumentoFiscal?> GetByIdAsync(DocumentoFiscalId id, CancellationToken ct = default);
    Task<DocumentoFiscal?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        TenantId tenantId,
        CancellationToken ct = default);
    Task AddAsync(DocumentoFiscal documento, CancellationToken ct = default);
    Task UpdateAsync(DocumentoFiscal documento, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentoFiscal>> GetProcessandoAntigoAsync(
        TimeSpan timeout,
        CancellationToken ct = default);
}
