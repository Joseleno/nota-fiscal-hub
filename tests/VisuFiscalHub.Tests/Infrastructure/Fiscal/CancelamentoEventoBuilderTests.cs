using System.Xml;
using Shouldly;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class CancelamentoEventoBuilderTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static XmlNamespaceManager NfNs(XmlDocument doc)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
        return ns;
    }

    private static DocumentoFiscal CriarDocumentoParaBuilder()
        => DocumentoFiscalBuilder.Autorizado(FixedNow);

    private static Tenant CriarTenant()
    {
        var cnpj = Cnpj.Criar("11.222.333/0001-81").Value;
        var config = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35).Value;
        var endereco = Endereco.Criar(
            "Rua Teste", "100", null, "Centro", "São Paulo", 3550308, "SP", "01310100").Value;

        return Tenant.Criar(
            ClienteAppId.New(), cnpj, "Empresa Teste", null, config, endereco, TimeProvider.System).Value;
    }

    [Fact]
    public void ConstruirEvento_DeveConterTpEvento110111()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, idLote: "202605241000000");

        xml.SelectSingleNode("//nfe:tpEvento", NfNs(xml))!.InnerText.ShouldBe("110111");
    }

    [Fact]
    public void ConstruirEvento_DeveConterNProtDaAutorizacao()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        xml.SelectSingleNode("//nfe:nProt", NfNs(xml))!.InnerText.ShouldBe("PROT001");
    }

    [Fact]
    public void ConstruirEvento_DeveConterXJust()
    {
        const string just = "Justificativa de teste com tamanho suficiente";
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), just, "PROT001", FixedNow, "202605241000000");

        xml.SelectSingleNode("//nfe:xJust", NfNs(xml))!.InnerText.ShouldBe(just);
    }

    [Fact]
    public void ConstruirEvento_IdInfEvento_DeveSerID110111MaisChaveAcesso()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        var id = xml.SelectSingleNode("//nfe:infEvento", NfNs(xml))!.Attributes!["Id"]!.Value;
        id.ShouldBe($"ID110111{doc.ChaveAcesso.Valor}01");
    }

    [Fact]
    public void ConstruirEvento_DeveConterDescEventoCancelamento()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        xml.SelectSingleNode("//nfe:descEvento", NfNs(xml))!.InnerText.ShouldBe("Cancelamento");
    }

    [Fact]
    public void ConstruirEvento_NaoDeveConterElementoSignature()
    {
        // ConstruirEvento retorna XML NÃO assinado — assinatura é responsabilidade do CancelamentoJob
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        xml.SelectSingleNode("//ds:Signature", ns).ShouldBeNull();
    }
}
