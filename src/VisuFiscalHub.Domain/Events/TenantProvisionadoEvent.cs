using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record TenantProvisionadoEvent(
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    string Cnpj) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
