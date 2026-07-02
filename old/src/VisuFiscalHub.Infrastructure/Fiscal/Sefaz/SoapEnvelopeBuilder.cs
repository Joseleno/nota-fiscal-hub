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
    private const string WsEventoNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";

    /// <summary>
    /// Monta o envelope NfeAutorizacao4 para envio em lote de 1 NFC-e.
    /// cUF é obrigatório no cabeçalho conforme esquema nfeCabMsg.xsd.
    /// tpAmb vai dentro do XML NFC-e (construído por NfceXmlBuilder), não no cabeçalho SOAP.
    /// idLote deve ser fornecido pelo chamador (gerado via TimeProvider) para garantir testabilidade.
    /// </summary>
    public static string BuildAutorizacao(string xmlNfe, int cUF, string idLote)
    {
        var envXml = BuildEnviNFe(xmlNfe, idLote);

        var doc = new XmlDocument();
        var envelope = doc.CreateElement("soap12", "Envelope", SoapNs);

        var header = doc.CreateElement("soap12", "Header", SoapNs);
        var nfeCabMsg = doc.CreateElement("nfeCabMsg", WsNs);
        AddChild(doc, nfeCabMsg, WsNs, "cUF", cUF.ToString());
        AddChild(doc, nfeCabMsg, WsNs, "versaoDados", "4.00");
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

    /// <summary>
    /// Monta o envelope NFeRecepcaoEvento4 para envio de eventos (como cancelamento).
    /// cUF é obrigatório no cabeçalho conforme esquema nfeCabMsg.xsd.
    /// versaoDados = "1.00" conforme padrão de eventos SEFAZ.
    /// </summary>
    public static string BuildEvento(string xmlEvento, int cUF)
    {
        var doc = new XmlDocument();
        var envelope = doc.CreateElement("soap12", "Envelope", SoapNs);

        var header = doc.CreateElement("soap12", "Header", SoapNs);
        var nfeCabMsg = doc.CreateElement("nfeCabMsg", WsEventoNs);
        AddChild(doc, nfeCabMsg, WsEventoNs, "cUF", cUF.ToString());
        AddChild(doc, nfeCabMsg, WsEventoNs, "versaoDados", "1.00");
        header.AppendChild(nfeCabMsg);
        envelope.AppendChild(header);

        var body = doc.CreateElement("soap12", "Body", SoapNs);
        var nfeDadosMsg = doc.CreateElement("nfeDadosMsg", WsEventoNs);
        nfeDadosMsg.InnerXml = xmlEvento;
        body.AppendChild(nfeDadosMsg);
        envelope.AppendChild(body);

        doc.AppendChild(envelope);
        return doc.OuterXml;
    }

    // Monta o lote EnviNFe com 1 NFC-e. idLote (15 dígitos numéricos) fornecido pelo chamador.
    private static string BuildEnviNFe(string xmlNfe, string idLote)
    {
        // TDec_015: campo obrigatório com exatamente 15 dígitos numéricos no schema SEFAZ.
        if (idLote.Length != 15 || !idLote.All(char.IsDigit))
            throw new ArgumentException("idLote deve ter exatamente 15 dígitos numéricos.", nameof(idLote));

        var doc = new XmlDocument();
        doc.LoadXml(xmlNfe);

        var enviNFe = new XmlDocument();
        var env = enviNFe.CreateElement("enviNFe", NfeNs);
        env.SetAttribute("versao", "4.00");

        AddChild(enviNFe, env, NfeNs, "idLote", idLote);
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
