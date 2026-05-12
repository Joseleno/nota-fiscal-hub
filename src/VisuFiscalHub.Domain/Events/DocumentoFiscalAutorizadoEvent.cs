using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalAutorizadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    string ChaveAcesso,
    string Protocolo,
    DateTime AuthorizedAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
