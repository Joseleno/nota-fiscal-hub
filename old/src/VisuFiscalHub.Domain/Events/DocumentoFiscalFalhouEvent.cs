using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalFalhouEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    DateTimeOffset FalhouAt,
    Guid EventId,
    DateTimeOffset OccurredAt) : IDomainEvent;
