using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Domain.Events;

public sealed record ClienteAppWebhookSecretRotadoEvent(
    ClienteAppId ClienteAppId,
    Guid EventId,
    DateTimeOffset OccurredAt) : IDomainEvent;
