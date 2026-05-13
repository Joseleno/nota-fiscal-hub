using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalRejeitadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    string MotivoRejeicao) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
