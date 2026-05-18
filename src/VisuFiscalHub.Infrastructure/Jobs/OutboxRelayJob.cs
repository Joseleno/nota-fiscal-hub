using System.Text.Json;
using Hangfire;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 30)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 30, 60])]
public sealed class OutboxRelayJob
{
    private const int BatchSize = 50;

    private readonly ApplicationDbContext _dbContext;
    private readonly IPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxRelayJob> _logger;

    public OutboxRelayJob(
        ApplicationDbContext dbContext,
        IPublisher publisher,
        TimeProvider timeProvider,
        ILogger<OutboxRelayJob> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // FOR UPDATE SKIP LOCKED: garante que múltiplas instâncias não processem o mesmo batch.
        // Executado dentro de transação explícita para que o UPDATE de processed_at seja atômico
        // com a leitura — sem janela de duplicação entre instâncias.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        var messages = await _dbContext.OutboxMessages
            .FromSqlRaw(@"
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL
                ORDER BY occurred_at
                LIMIT {0}
                FOR UPDATE SKIP LOCKED", BatchSize)
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            return;
        }

        _logger.LogDebug("OutboxRelayJob: processando {Count} mensagens.", messages.Count);

        foreach (var message in messages)
        {
            if (message.ProcessedAt is not null)
                continue;

            try
            {
                var eventType = ResolveEventType(message.EventType);
                if (eventType is null)
                {
                    _logger.LogWarning(
                        "OutboxRelayJob: tipo '{EventType}' não encontrado ou não implementa INotification (Id={MessageId}) — marcado como processado.",
                        message.EventType, message.Id);
                    message.ProcessedAt = _timeProvider.GetUtcNow();
                    await _dbContext.SaveChangesAsync(ct);
                    continue;
                }

                var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType);
                if (domainEvent is null)
                {
                    _logger.LogError(
                        "OutboxRelayJob: falha ao desserializar Id={MessageId}, Type={EventType}.",
                        message.Id, message.EventType);
                    message.ProcessedAt = _timeProvider.GetUtcNow();
                    await _dbContext.SaveChangesAsync(ct);
                    continue;
                }

                await _publisher.Publish(domainEvent, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isola falha de Publish ou de deserialização: não cancela as demais mensagens do batch.
                _logger.LogError(ex,
                    "OutboxRelayJob: erro ao processar Id={MessageId}, Type={EventType}. Permanece na fila.",
                    message.Id, message.EventType);
                continue;
            }

            // SaveChangesAsync flushes ProcessedAt para o buffer da transação — não é um commit independente.
            // O commit real ocorre em CommitAsync no fim do batch. Se CommitAsync falhar, todas as mensagens
            // do batch são re-processadas (at-least-once). Handlers de evento devem ser idempotentes.
            message.ProcessedAt = _timeProvider.GetUtcNow();
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogDebug(
                "OutboxRelayJob: Id={MessageId} Type={EventType} publicado.",
                message.Id, message.EventType);
        }

        await transaction.CommitAsync(ct);
    }

    // Type.GetType é mais robusto que AppDomain.GetAssemblies() para assemblies lazy-loaded.
    // Fallback para busca em assemblies carregados se GetType direto falhar.
    // Retorna null se o tipo não for encontrado OU não implementar INotification —
    // evita Publish silencioso de tipos incompatíveis desserializados como object.
    private static Type? ResolveEventType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        var t = Type.GetType(typeName)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName))
                .FirstOrDefault(x => x is not null);

        if (t is null) return null;

        return typeof(INotification).IsAssignableFrom(t) ? t : null;
    }
}
