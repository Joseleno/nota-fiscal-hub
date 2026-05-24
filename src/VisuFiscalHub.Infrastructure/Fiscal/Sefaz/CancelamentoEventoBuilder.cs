using System.Xml;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Constrói o XML do evento de cancelamento NFC-e (tpEvento=110111).
/// Retorna XmlDocument NÃO assinado — a assinatura é aplicada pelo CancelamentoJob via XmlSigner.
/// </summary>
internal static class CancelamentoEventoBuilder
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    public static XmlDocument ConstruirEvento(
        DocumentoFiscal documento,
        Tenant tenant,
        string justificativa,
        string nProt,
        DateTimeOffset utcNow,
        string idLote)
    {
        var chave    = documento.ChaveAcesso.Valor;
        var ufCodigo = tenant.ConfiguracaoFiscal.UfCodigo;
        var tpAmb    = (int)tenant.ConfiguracaoFiscal.Ambiente;
        var cnpj     = tenant.Cnpj.Valor;

        // Converte UTC para o fuso da UF para conformidade com o schema SEFAZ
        var fusoUf   = UfFusoHorario.Mapa[ufCodigo];
        var dhEvento = utcNow.ToOffset(fusoUf).ToString("yyyy-MM-ddTHH:mm:sszzz");

        var doc = new XmlDocument();
        var envEvento = doc.CreateElement("envEvento", NfeNs);
        envEvento.SetAttribute("versao", "1.00");

        Add(doc, envEvento, "idLote", idLote);

        var evento = doc.CreateElement("evento", NfeNs);
        evento.SetAttribute("versao", "1.00");

        var infEvento = doc.CreateElement("infEvento", NfeNs);
        infEvento.SetAttribute("Id", $"ID110111{chave}01");

        Add(doc, infEvento, "cOrgao",     ufCodigo.ToString());
        Add(doc, infEvento, "tpAmb",      tpAmb.ToString());
        Add(doc, infEvento, "CNPJ",       cnpj);
        Add(doc, infEvento, "chNFe",      chave);
        Add(doc, infEvento, "dhEvento",   dhEvento);
        Add(doc, infEvento, "tpEvento",   "110111");
        Add(doc, infEvento, "nSeqEvento", "1");
        Add(doc, infEvento, "verEvento",  "1.00");

        var detEvento = doc.CreateElement("detEvento", NfeNs);
        detEvento.SetAttribute("versao", "1.00");
        Add(doc, detEvento, "descEvento", "Cancelamento");
        Add(doc, detEvento, "nProt",      nProt);
        Add(doc, detEvento, "xJust",      justificativa);

        infEvento.AppendChild(detEvento);
        evento.AppendChild(infEvento);
        envEvento.AppendChild(evento);
        doc.AppendChild(envEvento);

        return doc;
    }

    private static void Add(XmlDocument doc, XmlElement parent, string tag, string value)
    {
        var el = doc.CreateElement(tag, NfeNs);
        el.InnerText = value;
        parent.AppendChild(el);
    }
}
