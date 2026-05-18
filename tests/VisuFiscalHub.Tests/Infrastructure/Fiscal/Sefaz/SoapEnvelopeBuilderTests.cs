using System.Xml;
using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal.Sefaz;

public class SoapEnvelopeBuilderTests
{
    private const string NfeNs  = "http://www.portalfiscal.inf.br/nfe";
    private const string SoapNs = "http://www.w3.org/2003/05/soap-envelope";
    private const string WsNs   = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4";

    private static readonly string SampleNfeXml =
        $"<nfeProc xmlns=\"{NfeNs}\"><NFe><infNFe/></NFe></nfeProc>";

    // ── idLote válido ────────────────────────────────────────────────────────────

    [Fact]
    public void BuildAutorizacao_IdLoteValido_RetornaXmlComEstruturaSoap()
    {
        var result = SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, idLote: "202605171200000");

        var doc = new XmlDocument();
        doc.LoadXml(result);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("soap12", SoapNs);
        ns.AddNamespace("nfe", NfeNs);
        ns.AddNamespace("ws", WsNs);

        doc.SelectSingleNode("//soap12:Envelope", ns).ShouldNotBeNull();
        doc.SelectSingleNode("//soap12:Header", ns).ShouldNotBeNull();
        doc.SelectSingleNode("//soap12:Body", ns).ShouldNotBeNull();
        // nfeCabMsg e filhos devem estar em WsNs (NFeAutorizacao4), não em NfeNs.
        doc.SelectSingleNode("//ws:nfeCabMsg", ns).ShouldNotBeNull();
        doc.SelectSingleNode("//ws:cUF", ns)!.InnerText.ShouldBe("35");
        doc.SelectSingleNode("//ws:versaoDados", ns)!.InnerText.ShouldBe("4.00");
        doc.SelectSingleNode("//ws:nfeDadosMsg", ns).ShouldNotBeNull();
    }

    [Fact]
    public void BuildAutorizacao_CabecalhoSoap_NaoContemTpAmb()
    {
        // tpAmb pertence ao XML NFC-e (NfceXmlBuilder), não ao cabeçalho SOAP NFeAutorizacao4.
        var result = SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, idLote: "202605171200000");

        var doc = new XmlDocument();
        doc.LoadXml(result);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("soap12", SoapNs);
        ns.AddNamespace("nfe", NfeNs);

        var header = doc.SelectSingleNode("//soap12:Header", ns);
        header.ShouldNotBeNull();
        header!.SelectSingleNode(".//nfe:tpAmb", ns).ShouldBeNull();
    }

    [Fact]
    public void BuildAutorizacao_IdLoteValido_EnviNFeContemIdLoteEIndSinc()
    {
        var result = SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 43, idLote: "123456789012345");

        var doc = new XmlDocument();
        doc.LoadXml(result);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        doc.SelectSingleNode("//nfe:idLote", ns)!.InnerText.ShouldBe("123456789012345");
        doc.SelectSingleNode("//nfe:indSinc", ns)!.InnerText.ShouldBe("1");
    }

    [Fact]
    public void BuildAutorizacao_IdLoteValido_VersoesCorretas()
    {
        var result = SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, idLote: "202605171200000");

        var doc = new XmlDocument();
        doc.LoadXml(result);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var enviNfe = doc.SelectSingleNode("//nfe:enviNFe", ns);
        enviNfe.ShouldNotBeNull();
        enviNfe!.Attributes?["versao"]?.Value.ShouldBe("4.00");
    }

    // ── idLote inválido — guard TDec_015 ────────────────────────────────────────

    [Theory]
    [InlineData("12345678901234")]
    [InlineData("1234567890123456")]
    [InlineData("")]
    [InlineData("2026051712000AB")]
    [InlineData("2026051712 0000")]
    public void BuildAutorizacao_IdLoteInvalido_LancaArgumentException(string idLote)
    {
        var ex = Should.Throw<ArgumentException>(
            () => SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, idLote: idLote));

        ex.ParamName.ShouldBe("idLote");
    }

    [Fact]
    public void BuildAutorizacao_IdLoteExatos15Digitos_NaoLancaExcecao()
    {
        Should.NotThrow(() =>
            SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, idLote: "000000000000000"));
    }
}
