using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IDocumentJobQueue
{
    Task EnqueueProcessingAsync(DocumentoFiscalId id, CancellationToken ct = default);
}
