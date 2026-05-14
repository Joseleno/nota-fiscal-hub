using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalDenegadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    string Cnpj,
    string XMotivo,
    Guid EventId,
    DateTimeOffset OccurredAt) : IDomainEvent;
