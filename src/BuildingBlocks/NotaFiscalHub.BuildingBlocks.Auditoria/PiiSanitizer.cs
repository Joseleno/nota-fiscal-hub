namespace NotaFiscalHub.BuildingBlocks.Auditoria;

/// <summary>
/// Sanitizador defensivo anti-PII (spec B5 §Abordagem passo 2): remove, do payload de um evento antes de
/// persistir, qualquer chave cujo NOME (case-insensitive) case com a denylist. Defesa em profundidade — a
/// garantia primária é a regra de fronteira §2.3.6 (eventos de integração nunca carregam PII/XML nos
/// produtores, ver <c>EventPayloadRules</c> da Tarefa 3); esta sanitização existe para o caso de um
/// produtor violar essa regra sem ser pego em build.
///
/// Método PURO — não loga nada e não recebe <c>ILogger</c>: emitir o <c>WARN</c> quando <c>alertouWarn</c>
/// é <see langword="true"/> é responsabilidade do CHAMADO (<c>AuditoriaEventHandler</c>), que decide a
/// mensagem final sem nunca incluir o valor removido (nem o nome da chave, por precaução extra).
/// </summary>
public static class PiiSanitizer
{
    private static readonly HashSet<string> Denylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "cpf", "cnpjConsumidor", "nome", "senha", "xml", "pfx", "csc", "token", "secret",
    };

    public static IReadOnlyDictionary<string, object?> Sanitizar(
        IReadOnlyDictionary<string, object?> payload, out bool alertouWarn)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var resultado = new Dictionary<string, object?>();
        var removeuAlgumaChave = false;

        foreach (var (chave, valor) in payload)
        {
            if (Denylist.Contains(chave))
            {
                removeuAlgumaChave = true;
                continue;
            }

            resultado[chave] = valor;
        }

        alertouWarn = removeuAlgumaChave;
        return resultado;
    }
}
