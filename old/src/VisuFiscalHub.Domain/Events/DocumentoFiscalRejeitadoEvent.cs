using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalRejeitadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    string MotivoRejeicao,
    Guid EventId,
    DateTimeOffset OccurredAt) : IDomainEvent;
