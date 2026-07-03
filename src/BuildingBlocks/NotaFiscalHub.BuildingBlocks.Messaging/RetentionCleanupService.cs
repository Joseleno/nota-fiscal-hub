using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Hosted service de limpeza da Outbox/Inbox de um módulo (spec B3 passo 8). Remove em lotes:
/// <c>Outbox</c> <see cref="OutboxStatus.Processada"/> com mais de <see cref="OutboxInboxOptions.RetencaoDeProcessadas"/>
/// (default 7 dias); <c>Inbox</c> com mais de <see cref="OutboxInboxOptions.RetencaoDeInbox"/> (default 30 dias).
/// <see cref="OutboxStatus.Poison"/> e <see cref="OutboxStatus.SemHandler"/> NUNCA são removidos por idade —
/// apagar um evento órfão por idade recriaria o "evento sumindo em silêncio" (MSG0005) que a B3 elimina;
/// eles só saem da tabela por ação humana explícita (redisparo/resolução via runbook).
/// </summary>
public sealed class RetentionCleanupService<TDbContext> : BackgroundService
    where TDbContext : DbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxInboxOptions _options;
    private readonly ILogger<RetentionCleanupService<TDbContext>> _logger;

    public RetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxInboxOptions> options,
        ILogger<RetentionCleanupService<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ExecutarUmCicloAsync(stoppingToken);

            try
            {
                await Task.Delay(_options.IntervaloDeRetencao, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Executa um ciclo de limpeza. Público para permitir disparo determinístico em testes.</summary>
    public async Task ExecutarUmCicloAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var corteProcessadas = DateTimeOffset.UtcNow - _options.RetencaoDeProcessadas;
        var corteInbox = DateTimeOffset.UtcNow - _options.RetencaoDeInbox;

        var totalOutboxRemovidas = 0;
        int removidosOutbox;
        do
        {
            removidosOutbox = await db.Set<OutboxMessage>()
                .Where(m => m.Status == OutboxStatus.Processada && m.ProcessadaEm != null && m.ProcessadaEm < corteProcessadas)
                .OrderBy(m => m.ProcessadaEm)
                .Take(_options.TamanhoDoLoteDeRetencao)
                .ExecuteDeleteAsync(ct);
            totalOutboxRemovidas += removidosOutbox;
        }
        while (removidosOutbox == _options.TamanhoDoLoteDeRetencao);

        var totalInboxRemovidas = 0;
        int removidosInbox;
        do
        {
            removidosInbox = await db.Set<InboxMessage>()
                .Where(m => m.ProcessadaEm < corteInbox)
                .OrderBy(m => m.ProcessadaEm)
                .Take(_options.TamanhoDoLoteDeRetencao)
                .ExecuteDeleteAsync(ct);
            totalInboxRemovidas += removidosInbox;
        }
        while (removidosInbox == _options.TamanhoDoLoteDeRetencao);

        _logger.LogInformation(
            "OutboxRetentionCiclo: {OutboxRemovidas} linhas de outbox (Processada > {DiasOutbox}d) e {InboxRemovidas} linhas de inbox (> {DiasInbox}d) removidas.",
            totalOutboxRemovidas, _options.RetencaoDeProcessadas.TotalDays, totalInboxRemovidas, _options.RetencaoDeInbox.TotalDays);
    }
}
