using Serilog.Core;
using Serilog.Events;

namespace NotaFiscalHub.BuildingBlocks.Observability;

/// <summary>
/// Camada 1 da política anti-PII (spec B7 passo 2): enricher Serilog que substitui, por NOME de
/// propriedade estruturada (denylist central <see cref="PiiFields"/>, case-insensitive), qualquer valor
/// logado — nunca depende do autor do log lembrar de mascarar manualmente. Propriedade fora da denylist
/// passa intacta.
///
/// Só atua sobre propriedades ESTRUTURADAS (<c>{Cpf}</c> no template) — não reescreve o texto livre da
/// mensagem (essa é a responsabilidade da Camada 2, o analyzer CA2254 que proíbe interpolação, e da
/// Camada 3, o guardrail de teste <see cref="PiiLogAssertions"/>).
/// </summary>
public sealed class PiiMaskingEnricher : ILogEventEnricher
{
    private const string ValorMascarado = "***";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Materializa os nomes antes de mutar — AddOrUpdateProperty durante a enumeração do próprio
        // dicionário quebraria a iteração.
        var nomesDeProdiedadesSensiveis = logEvent.Properties.Keys
            .Where(PiiFields.EhSensivel)
            .ToList();

        foreach (var nome in nomesDeProdiedadesSensiveis)
        {
            var valorMascarado = MascarValor(nome, logEvent.Properties[nome]);
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(nome, valorMascarado));
        }
    }

    private static string MascarValor(string nomePropriedade, LogEventPropertyValue valorOriginal)
    {
        if (!PiiFields.MascaradosParcialmente.Contains(nomePropriedade))
            return ValorMascarado;

        var textoOriginal = (valorOriginal as ScalarValue)?.Value?.ToString();
        return MascararChaveDeAcessoParcialmente(textoOriginal);
    }

    /// <summary>
    /// Máscara parcial da chave de acesso NFe/NFCe: 6 primeiros + "..." + 4 últimos dígitos (ex.:
    /// <c>350906...0097</c> — design §4.3). Valor fora do formato esperado (nulo/curto demais) cai para
    /// o mascaramento total — nunca arrisca vazar mais do que o esperado por um formato inesperado.
    /// </summary>
    private static string MascararChaveDeAcessoParcialmente(string? valor)
    {
        if (string.IsNullOrEmpty(valor) || valor.Length < 10)
            return ValorMascarado;

        return $"{valor[..6]}...{valor[^4..]}";
    }
}
