using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

internal static class PrefeituraRetornoParser
{
    private const string NfseNs = "http://www.abrasf.org.br/nfse.xsd";

    public static Result<PrefeituraRetorno> ParseGerarNfse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfse", NfseNs);

            var listaErros = doc.SelectSingleNode("//nfse:ListaMensagemRetorno", ns);
            if (listaErros is not null)
            {
                var codigo   = listaErros.SelectSingleNode(".//nfse:Codigo", ns)?.InnerText ?? "E99";
                var mensagem = listaErros.SelectSingleNode(".//nfse:Mensagem", ns)?.InnerText ?? "Erro desconhecido";
                return Result.Success(new PrefeituraRetorno(
                    Autorizado: false,
                    NumeroNfse: null,
                    Protocolo: null,
                    XmlNfse: null,
                    MotivoErro: $"[{codigo}] {mensagem}",
                    ElapsedMs: 0L));
            }

            var nfseNode = doc.SelectSingleNode("//nfse:CompNfse/nfse:Nfse/nfse:InfNfse", ns);
            if (nfseNode is null)
                return Result.Failure<PrefeituraRetorno>(
                    new Error("Prefeitura.RetornoInvalido", "Retorno ABRASF sem CompNfse/InfNfse."));

            var numero  = nfseNode.SelectSingleNode("nfse:Numero", ns)?.InnerText;
            var xmlNfse = doc.SelectSingleNode("//nfse:CompNfse", ns)?.OuterXml;

            return Result.Success(new PrefeituraRetorno(
                Autorizado: !string.IsNullOrWhiteSpace(numero),
                NumeroNfse: numero,
                Protocolo: numero,
                XmlNfse: xmlNfse,
                MotivoErro: null,
                ElapsedMs: 0L));
        }
        catch (XmlException ex)
        {
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.XmlInvalido", $"Falha ao parsear retorno ABRASF: {ex.Message}"));
        }
    }

    public static Result<PrefeituraConsultaRetorno> ParseConsultarNfse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfse", NfseNs);

            var listaErros = doc.SelectSingleNode("//nfse:ListaMensagemRetorno", ns);
            if (listaErros is not null)
            {
                var codigo   = listaErros.SelectSingleNode(".//nfse:Codigo", ns)?.InnerText ?? "E99";
                var mensagem = listaErros.SelectSingleNode(".//nfse:Mensagem", ns)?.InnerText ?? "Erro";
                var naoEncontrado = codigo is "E10" or "E4";
                return Result.Success(new PrefeituraConsultaRetorno(
                    Encontrado: !naoEncontrado,
                    Autorizado: false,
                    NumeroNfse: null,
                    XmlNfse: null,
                    MotivoErro: $"[{codigo}] {mensagem}",
                    ElapsedMs: 0L));
            }

            var nfseNode = doc.SelectSingleNode("//nfse:CompNfse/nfse:Nfse/nfse:InfNfse", ns);
            var numero   = nfseNode?.SelectSingleNode("nfse:Numero", ns)?.InnerText;
            var xmlNfse  = doc.SelectSingleNode("//nfse:CompNfse", ns)?.OuterXml;

            return Result.Success(new PrefeituraConsultaRetorno(
                Encontrado: nfseNode is not null,
                Autorizado: !string.IsNullOrWhiteSpace(numero),
                NumeroNfse: numero,
                XmlNfse: xmlNfse,
                MotivoErro: null,
                ElapsedMs: 0L));
        }
        catch (XmlException ex)
        {
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.ConsultaXmlInvalido", $"Falha ao parsear consulta ABRASF: {ex.Message}"));
        }
    }
}
