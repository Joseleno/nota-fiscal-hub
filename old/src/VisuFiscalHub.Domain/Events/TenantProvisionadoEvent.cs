using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record TenantProvisionadoEvent(
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    string Cnpj,
    Guid EventId,
    DateTimeOffset OccurredAt) : IDomainEvent;
