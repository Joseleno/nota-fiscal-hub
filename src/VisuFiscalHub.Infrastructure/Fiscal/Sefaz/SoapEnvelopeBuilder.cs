using System.Xml;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Monta envelopes SOAP 1.2 para os serviços NF-e conforme NT SEFAZ.
/// Os namespaces e a estrutura seguem o WSDL publicado pelo Portal NF-e.
/// </summary>
internal static class SoapEnvelopeBuilder
{
    private const string SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private const string NfeNs  = "http://www.portalfiscal.inf.br/nfe";
    private const string WsNs   = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4";

    /// <summary>
    /// Monta o envelope NfeAutorizacao4 para envio em lote de 1 NFC-e.
    /// cUF e tpAmb são obrigatórios no cabeçalho conforme esquema nfeCabMsg.xsd.
    /// </summary>
    public static string BuildAutorizacao(string xmlNfe, int cUF, int tpAmb)
    {
        var envXml = BuildEnviNFe(xmlNfe, cUF, tpAmb);

        var doc = new XmlDocument();
        var envelope = doc.CreateElement("soap12", "Envelope", SoapNs);

        var header = doc.CreateElement("soap12", "Header", SoapNs);
        var nfeCabMsg = doc.CreateElement("nfeCabMsg", NfeNs);
        AddChild(doc, nfeCabMsg, NfeNs, "cUF", cUF.ToString());
        AddChild(doc, nfeCabMsg, NfeNs, "versaoDados", "4.00");
        header.AppendChild(nfeCabMsg);
        envelope.AppendChild(header);

        var body = doc.CreateElement("soap12", "Body", SoapNs);
        var nfeDadosMsg = doc.CreateElement("nfeDadosMsg", WsNs);
        nfeDadosMsg.InnerXml = envXml;
        body.AppendChild(nfeDadosMsg);
        envelope.AppendChild(body);

        doc.AppendChild(envelope);
        return doc.OuterXml;
    }

    // Monta o lote EnviNFe com 1 NFC-e (idLote gerado com 15 dígitos aleatórios).
    private static string BuildEnviNFe(string xmlNfe, int cUF, int tpAmb)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xmlNfe);

        var enviNFe = new XmlDocument();
        var env = enviNFe.CreateElement("enviNFe", NfeNs);
        env.SetAttribute("versao", "4.00");

        var idLote = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "0";
        AddChild(enviNFe, env, NfeNs, "idLote", idLote.PadRight(15, '0')[..15]);
        AddChild(enviNFe, env, NfeNs, "indSinc", "1"); // Síncrono — recomendado para NFC-e

        env.AppendChild(enviNFe.ImportNode(doc.DocumentElement!, true));
        enviNFe.AppendChild(env);

        return enviNFe.OuterXml;
    }

    private static void AddChild(XmlDocument doc, XmlElement parent, string ns, string tag, string value)
    {
        var el = doc.CreateElement(tag, ns);
        el.InnerText = value;
        parent.AppendChild(el);
    }
}
