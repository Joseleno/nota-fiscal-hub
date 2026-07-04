namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>Configuração do middleware de Idempotency-Key (spec B4 §Abordagem passos 4/5).</summary>
public sealed class IdempotencyOptions
{
    /// <summary>TTL do registro — spec B4 §Abordagem passo 4 ("cobre o ciclo de retry de PDV e a janela de contingência de 24h").</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Idade a partir da qual um registro <c>EmProcessamento</c> é considerado abandonado e pode sofrer
    /// takeover (spec B4 §Abordagem passo 5) — ordem de grandeza acima do timeout síncrono do Motor (~5s).
    /// </summary>
    public TimeSpan OrphanTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Segundos sugeridos no header <c>Retry-After</c> devolvido ao perdedor de uma corrida (spec B4 §Abordagem passo 3).</summary>
    public int RetryAfterSegundos { get; set; } = 2;

    /// <summary>Tamanho máximo do corpo de resposta armazenado para replay (spec B4 §Riscos "Corpo grande").</summary>
    public int TamanhoMaximoDoCorpoArmazenado { get; set; } = 256 * 1024;
}
