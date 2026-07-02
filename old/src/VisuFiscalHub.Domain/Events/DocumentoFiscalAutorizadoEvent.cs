using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalAutorizadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    string ChaveAcesso,
    string Protocolo,
    DateTimeOffset AuthorizedAt,
    Guid EventId,
    DateTimeOffset OccurredAt) : IDomainEvent;
