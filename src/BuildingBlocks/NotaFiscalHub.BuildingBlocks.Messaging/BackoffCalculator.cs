namespace NotaFiscalHub.BuildingBlocks.Messaging;

/// <summary>
/// Backoff exponencial com jitter para retry de mensagens da Outbox (spec B3 passo 6): base 5s, fator 2,
/// teto 1h. O jitter é estritamente ADITIVO — nunca reduz o atraso abaixo do valor exponencial "puro" —
/// para que o teto de 1h continue sendo o único limite superior e para que os testes possam afirmar um
/// mínimo determinístico (<c>&gt;=</c> base exponencial) mesmo com jitter ativo.
/// </summary>
public static class BackoffCalculator
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(5);
    private const double Fator = 2.0;
    private static readonly TimeSpan Teto = TimeSpan.FromHours(1);

    /// <summary>
    /// Calcula o atraso até a próxima tentativa. <paramref name="tentativa"/> é 1-based (primeira
    /// tentativa de retry = 1). <paramref name="seed"/> torna o jitter determinístico — mesma
    /// combinação (tentativa, seed) sempre produz o mesmo atraso, essencial para testes reprodutíveis.
    /// </summary>
    public static TimeSpan Calcular(int tentativa, int seed)
    {
        if (tentativa < 1)
            throw new ArgumentOutOfRangeException(nameof(tentativa), tentativa, "Tentativa deve ser >= 1.");

        var expoente = tentativa - 1;
        var baseSegundos = Base.TotalSeconds * Math.Pow(Fator, expoente);

        // Jitter determinístico por (tentativa, seed): até +20% do valor exponencial, sempre somado.
        var rng = new Random(HashCode.Combine(tentativa, seed));
        var jitterFator = rng.NextDouble() * 0.2;
        var comJitterSegundos = baseSegundos * (1.0 + jitterFator);

        var tetoSegundos = Teto.TotalSeconds;
        var segundosFinais = Math.Min(comJitterSegundos, tetoSegundos);

        return TimeSpan.FromSeconds(segundosFinais);
    }
}
