using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalCanceladoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    DateTimeOffset CanceladoAt,
    Guid EventId,
    DateTimeOffset OccurredAt) : IDomainEvent;
