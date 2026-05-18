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
    private readonly ILogger<OutboxRelayJob> _logger;

    public OutboxRelayJob(
        ApplicationDbContext dbContext,
        IPublisher publisher,
        ILogger<OutboxRelayJob> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
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
                        "OutboxRelayJob: tipo desconhecido '{EventType}' (Id={MessageId}) — marcado como processado.",
                        message.EventType, message.Id);
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                    await _dbContext.SaveChangesAsync(ct);
                    continue;
                }

                var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType);
                if (domainEvent is null)
                {
                    _logger.LogError(
                        "OutboxRelayJob: falha ao desserializar Id={MessageId}, Type={EventType}.",
                        message.Id, message.EventType);
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                    await _dbContext.SaveChangesAsync(ct);
                    continue;
                }

                await _publisher.Publish(domainEvent, ct);

                // SaveChanges por mensagem — falha no próximo item não afeta este.
                message.ProcessedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogDebug(
                    "OutboxRelayJob: Id={MessageId} Type={EventType} publicado.",
                    message.Id, message.EventType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OutboxRelayJob: erro ao processar Id={MessageId}, Type={EventType}. Permanece na fila.",
                    message.Id, message.EventType);
            }
        }

        await transaction.CommitAsync(ct);
    }

    // Type.GetType é mais robusto que AppDomain.GetAssemblies() para assemblies lazy-loaded.
    // Fallback para busca em assemblies carregados se GetType direto falhar.
    private static Type? ResolveEventType(string typeName)
    {
        var t = Type.GetType(typeName);
        if (t is not null) return t;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName))
            .FirstOrDefault(x => x is not null);
    }
}
