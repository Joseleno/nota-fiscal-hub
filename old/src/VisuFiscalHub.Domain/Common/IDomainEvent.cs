using Mediator;

namespace VisuFiscalHub.Domain.Common;

public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
