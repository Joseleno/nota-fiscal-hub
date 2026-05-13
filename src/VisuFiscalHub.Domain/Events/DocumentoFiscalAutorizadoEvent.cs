using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalAutorizadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    string ChaveAcesso,
    string Protocolo,
    DateTimeOffset AuthorizedAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
