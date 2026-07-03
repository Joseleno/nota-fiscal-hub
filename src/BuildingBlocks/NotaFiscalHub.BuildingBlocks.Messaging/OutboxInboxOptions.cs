namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>Opções de configuração por módulo de <c>AddOutboxInbox&lt;TDbContext&gt;</c> (spec B3 passos 4/6/8).</summary>
public sealed class OutboxInboxOptions
{
    /// <summary>Intervalo entre ciclos de polling do dispatcher. Default 2s (spec B3, "Riscos" — polling×latência).</summary>
    public TimeSpan IntervaloDePolling { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Tamanho do lote reivindicado por ciclo (<c>LIMIT</c> do <c>SELECT ... FOR UPDATE SKIP LOCKED</c>).</summary>
    public int TamanhoDoLote { get; set; } = 50;

    /// <summary>Número máximo de tentativas antes de marcar a mensagem como <see cref="OutboxStatus.Poison"/>.</summary>
    public int MaxTentativas { get; set; } = 10;

    /// <summary>Seed do <see cref="BackoffCalculator"/> — fixa por módulo para tornar o backoff testável.</summary>
    public int SeedDeJitter { get; set; } = Environment.TickCount;

    /// <summary>Intervalo entre ciclos de limpeza do <see cref="RetentionCleanupService{TDbContext}"/>. Default 1h.</summary>
    public TimeSpan IntervaloDeRetencao { get; set; } = TimeSpan.FromHours(1);

    /// <summary><see cref="OutboxStatus.Processada"/> mais antiga que isso é elegível para limpeza. Default 7 dias.</summary>
    public TimeSpan RetencaoDeProcessadas { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Linhas de Inbox mais antigas que isso são elegíveis para limpeza. Default 30 dias.</summary>
    public TimeSpan RetencaoDeInbox { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Tamanho do lote de exclusão por ciclo de retenção (spec B3 passo 8).</summary>
    public int TamanhoDoLoteDeRetencao { get; set; } = 1000;
}
