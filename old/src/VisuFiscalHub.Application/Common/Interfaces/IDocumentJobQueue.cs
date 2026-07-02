using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IDocumentJobQueue
{
    Task EnqueueProcessingAsync(DocumentoFiscalId id, TipoDocumento tipo, CancellationToken ct = default);
}
