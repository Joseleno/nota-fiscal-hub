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

    /// <summary>
    /// Retorna documentos com status Processando cuja <c>CreatedAt</c> seja anterior a <paramref name="anteriorA"/>.
    /// O caller calcula o threshold usando <c>TimeProvider</c> antes de chamar o repositório.
    /// </summary>
    Task<IReadOnlyList<DocumentoFiscal>> GetProcessandoAntigoAsync(
        DateTimeOffset anteriorA,
        CancellationToken ct = default);
}
