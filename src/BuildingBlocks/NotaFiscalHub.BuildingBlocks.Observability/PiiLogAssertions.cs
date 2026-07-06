using System.Text.RegularExpressions;
using Serilog.Events;

namespace NotaFiscalHub.BuildingBlocks.Observability;

/// <summary>
/// Camada 3 da política anti-PII (spec B7 passo 2): guardrail de TESTE reutilizável pelos módulos de
/// negócio a partir da Fase 1 — renderiza cada <see cref="LogEvent"/> (mensagem + todas as propriedades,
/// incluindo as de exceção) e falha se casar um padrão de PII conhecido. Não substitui as Camadas 1/2
/// (pipeline + analyzer) — é a rede de segurança para o que escapar delas.
/// </summary>
public static partial class PiiLogAssertions
{
    [GeneratedRegex(@"\d{3}\.?\d{3}\.?\d{3}-?\d{2}")]
    private static partial Regex PadraoDeCpf();

    private static readonly string[] FragmentosXmlFiscal = ["<infNFe", "<NFe", "<dest>"];

    /// <summary>
    /// Falha (via exceção) se qualquer evento em <paramref name="eventos"/> contiver, na mensagem
    /// renderizada OU em qualquer propriedade (incl. as de uma exceção anexada), um padrão de CPF ou um
    /// fragmento de XML fiscal.
    /// </summary>
    public static void AssertNoPii(IEnumerable<LogEvent> eventos)
    {
        foreach (var evento in eventos)
        {
            var textoRenderizado = evento.RenderMessage();
            VerificarTexto(textoRenderizado, origem: "mensagem renderizada");

            foreach (var (nome, valor) in evento.Properties)
            {
                VerificarTexto(valor.ToString(), origem: $"propriedade '{nome}'");
            }

            if (evento.Exception is not null)
            {
                VerificarTexto(evento.Exception.ToString(), origem: "Exception.ToString()");
            }
        }
    }

    private static void VerificarTexto(string? texto, string origem)
    {
        if (string.IsNullOrEmpty(texto)) return;

        if (PadraoDeCpf().IsMatch(texto))
        {
            throw new InvalidOperationException(
                $"PiiLogAssertions.AssertNoPii: {origem} contém um padrão de CPF. Texto: {texto}");
        }

        foreach (var fragmento in FragmentosXmlFiscal)
        {
            if (texto.Contains(fragmento, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"PiiLogAssertions.AssertNoPii: {origem} contém fragmento de XML fiscal ('{fragmento}'). Texto: {texto}");
            }
        }
    }
}
