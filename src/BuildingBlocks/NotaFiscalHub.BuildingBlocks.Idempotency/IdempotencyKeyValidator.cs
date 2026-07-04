namespace NotaFiscalHub.BuildingBlocks.Idempotency;

/// <summary>
/// Validação da <c>Idempotency-Key</c> enviada pelo integrador (spec B4 §Abordagem passo 3): 1–255
/// caracteres VISÍVEIS (sem controle/whitespace) — função pura, sem I/O, testável isoladamente do filter.
/// </summary>
public static class IdempotencyKeyValidator
{
    private const int TamanhoMaximo = 255;

    public static bool EhValida(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key.Length > TamanhoMaximo) return false;

        foreach (var c in key)
        {
            // "Caractere visível" = não é controle e não é whitespace (space, tab, etc).
            if (char.IsControl(c) || char.IsWhiteSpace(c)) return false;
        }

        return true;
    }
}
