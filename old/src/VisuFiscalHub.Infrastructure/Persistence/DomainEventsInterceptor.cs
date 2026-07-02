using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Infrastructure.Persistence;

// Registrado como Singleton junto com DbContext. NÃO injetar dependências Scoped aqui —
// Scoped services têm lifetime menor que o interceptor e causarão ObjectDisposedException.
// Para lógica que precisa de Scoped (ex: IPublisher), use o OutboxProcessor separado.
public sealed class DomainEventsInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        var entities = eventData.Context.ChangeTracker
            .Entries<IDomainEventSource>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count != 0)
            .ToList();

        var correlationId = CorrelationIdAmbient.Current;

        var outboxMessages = entities
            .SelectMany(e => e.DomainEvents)
            .Select(domainEvent => new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                // FullName omite versão do assembly — sobrevive a upgrades sem quebrar desserialização
                EventType = domainEvent.GetType().FullName!,
                Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                OccurredAt = domainEvent.OccurredAt,
                CorrelationId = correlationId
            })
            .ToList();

        if (outboxMessages.Count != 0)
            eventData.Context.Set<OutboxMessage>().AddRange(outboxMessages);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        // ClearDomainEvents só após commit confirmado — evita perda silenciosa em caso de constraint violation
        if (eventData.Context is not null)
        {
            eventData.Context.ChangeTracker
                .Entries<IDomainEventSource>()
                .Select(e => e.Entity)
                .ToList()
                .ForEach(e => e.ClearDomainEvents());
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }
}
