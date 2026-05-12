using System.Collections.Generic;

namespace VisuFiscalHub.Domain.Common;

public interface IDomainEventSource
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
