using System.Globalization;
using System.Xml;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

public sealed record CancelamentoRetorno(
    bool Aceito,
    string CStat,
    string XMotivo,
    string? NProtCancelamento,
    DateTimeOffset? DhRegEvento);

internal static class CancelamentoRetornoParser
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    // 135 = evento registrado, 155 = evento registrado fora do prazo (SEFAZ aceita ambos).
    // 573 = Duplicidade de Evento: SEFAZ já registrou este evento — idempotente, tratar como sucesso
    // para que Hangfire retries não revertam o documento para Autorizado indevidamente.
    private static readonly IReadOnlySet<string> CStatAceito = new HashSet<string> { "135", "155", "573" };

    public static Result<CancelamentoRetorno> Parse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", NfeNs);

            var infEvento = doc.SelectSingleNode("//nfe:retEvento/nfe:infEvento", ns);
            if (infEvento is null)
                return Result.Failure<CancelamentoRetorno>(
                    new Error("Sefaz.CancelamentoRetornoInvalido", "Resposta não contém infEvento."));

            var cStat   = infEvento.SelectSingleNode("nfe:cStat",       ns)?.InnerText ?? string.Empty;
            var xMotivo = infEvento.SelectSingleNode("nfe:xMotivo",     ns)?.InnerText ?? string.Empty;
            var nProt   = infEvento.SelectSingleNode("nfe:nProt",       ns)?.InnerText;
            var dhRaw   = infEvento.SelectSingleNode("nfe:dhRegEvento", ns)?.InnerText;

            DateTimeOffset? dhRegEvento = null;
            if (!string.IsNullOrEmpty(dhRaw))
                dhRegEvento = DateTimeOffset.Parse(dhRaw, CultureInfo.InvariantCulture, DateTimeStyles.None);

            return Result.Success(new CancelamentoRetorno(
                Aceito:            CStatAceito.Contains(cStat),
                CStat:             cStat,
                XMotivo:           xMotivo,
                NProtCancelamento: nProt,
                DhRegEvento:       dhRegEvento));
        }
        catch (XmlException ex)
        {
            return Result.Failure<CancelamentoRetorno>(
                new Error("Sefaz.CancelamentoXmlInvalido", $"Falha ao parsear retorno: {ex.Message}"));
        }
    }
}
