using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICancelamentoJobQueue
{
    Task EnqueueCancelamentoAsync(
        DocumentoFiscalId id,
        string justificativa,
        CancellationToken ct = default);
}
