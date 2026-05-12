using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IDocumentJobQueue
{
    void EnqueueProcessing(DocumentoFiscalId id);
}
