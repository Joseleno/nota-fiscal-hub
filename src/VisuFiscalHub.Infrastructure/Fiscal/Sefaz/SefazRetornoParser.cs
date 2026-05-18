using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Interpreta o XML de retorno do webservice NFeAutorizacao4.
/// Códigos cStat relevantes (Ato COTEPE/ICMS 44/2018 e NT):
///   100       = Autorizado o uso da NF-e
///   110/301/302 = Uso denegado (fraude/irregularidade fiscal — definitivo)
///   204/572   = Duplicidade (documento já existe na SEFAZ — NProt pode ser null, não tratar como Autorizado)
///   1xx (≠110) = Rejeição recuperável (schema, ambiente, serviço) — reenviar
///   4xx/5xx   = Rejeição fiscal definitiva — não reenviar
/// </summary>
internal static class SefazRetornoParser
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    // cStat=100: autorizado com protocolo.
    private static readonly IReadOnlySet<string> CStatAutorizado = new HashSet<string> { "100" };

    // cStat que indicam denegação definitiva (não tentar novamente).
    private static readonly IReadOnlySet<string> CStatDenegado = new HashSet<string>
        { "110", "301", "302" };

    // cStat=204: NF-e em duplicidade (já autorizada). cStat=572: transmissão duplicada.
    // Ambos indicam que o documento JÁ existe na SEFAZ — NProt pode ser null, não tratar como Autorizado.
    private static readonly IReadOnlySet<string> CStatDuplicidade = new HashSet<string>
        { "204", "572" };

    public static Result<SefazRetorno> Parse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", NfeNs);

            // Extrai o retEnviNFe do body SOAP
            var retNode = doc.SelectSingleNode("//nfe:retEnviNFe", ns);
            if (retNode is null)
                return Result.Failure<SefazRetorno>(
                    new Error("Sefaz.RetornoInvalido", "Resposta SOAP não contém retEnviNFe."));

            var cStat  = retNode.SelectSingleNode("nfe:cStat",  ns)?.InnerText ?? string.Empty;
            var xMotivo = retNode.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? string.Empty;

            // Protocolo de autorização — pode estar em retNFe/infRec/nProt ou protNFe/infProt/nProt
            var nProt      = retNode.SelectSingleNode(".//nfe:nProt", ns)?.InnerText;
            var xmlProtNode = retNode.SelectSingleNode(".//nfe:protNFe", ns);
            var xmlProt     = xmlProtNode?.OuterXml;

            var autorizado = CStatAutorizado.Contains(cStat);
            var denegado   = CStatDenegado.Contains(cStat);

            return Result.Success(new SefazRetorno(
                Autorizado:    autorizado,
                CStat:         cStat,
                XMotivo:       xMotivo,
                NProt:         nProt,
                XmlAutorizado: autorizado ? xmlProt : null));
        }
        catch (XmlException ex)
        {
            return Result.Failure<SefazRetorno>(
                new Error("Sefaz.XmlInvalido", $"Falha ao parsear retorno SEFAZ: {ex.Message}"));
        }
    }

    public static bool IsDuplicidade(string cStat) => CStatDuplicidade.Contains(cStat);

    public static bool IsDenegado(string cStat) => CStatDenegado.Contains(cStat);

    /// <summary>
    /// Verifica se o cStat é uma rejeição recuperável (tentar novamente) vs definitiva.
    /// Códigos &lt; 200 são erros de infraestrutura/serviço — recuperáveis.
    /// Códigos 2xx-9xx são rejeições fiscais — definitivas, não devem ser reenviadas.
    /// </summary>
    public static bool IsRecuperavel(string cStat)
    {
        if (!int.TryParse(cStat, out var code))
            return true; // Resposta mal-formada — trata como transiente

        // 1xx: ambiente, serviço, schema — recuperáveis (exceto 110 que é denegação)
        return code < 200 && !CStatDenegado.Contains(cStat);
    }
}
