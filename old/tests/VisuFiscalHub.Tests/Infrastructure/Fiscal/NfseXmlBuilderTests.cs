using System.Xml;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public sealed class NfseXmlBuilderTests
{
    private readonly NfseXmlBuilder _builder = new();

    [Fact]
    public void ConstruirRps_DocumentoNfseValido_RetornaXmlComNamespaceAbrasf()
    {
        var (doc, tenant) = CriarDocumentoNfseValido();
        var result = _builder.ConstruirRps(doc, tenant);
        result.IsSuccess.ShouldBeTrue();
        result.Value.OuterXml.ShouldContain("http://www.abrasf.org.br/nfse.xsd");
    }

    [Fact]
    public void ConstruirRps_DocumentoNfseValido_XmlContemCamposObrigatorios()
    {
        var (doc, tenant) = CriarDocumentoNfseValido();
        var result = _builder.ConstruirRps(doc, tenant);
        result.IsSuccess.ShouldBeTrue();
        var outerXml = result.Value.OuterXml;
        outerXml.ShouldContain("<InfRps");
        outerXml.ShouldContain("<Servicos>");
        outerXml.ShouldContain("<Prestador>");
        outerXml.ShouldContain("<Tomador>");
        outerXml.ShouldContain("<Discriminacao>");
    }

    [Fact]
    public void ConstruirRps_DocumentoNfseValido_LoteRpsCpfCnpjEhElementoComplexo()
    {
        var (doc, tenant) = CriarDocumentoNfseValido();
        var result = _builder.ConstruirRps(doc, tenant);
        result.IsSuccess.ShouldBeTrue();
        var xml = result.Value;
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("nfse", "http://www.abrasf.org.br/nfse.xsd");
        // LoteRps/CpfCnpj deve ser elemento container com filho Cnpj, não texto plano
        var cnpjNode = xml.SelectSingleNode("//nfse:LoteRps/nfse:CpfCnpj/nfse:Cnpj", ns);
        cnpjNode.ShouldNotBeNull("LoteRps/CpfCnpj deve conter elemento filho Cnpj (tipo complexo ABRASF)");
    }

    [Fact]
    public void ConstruirRps_DocumentoNfCe_RetornaFalha()
    {
        var (doc, tenant) = CriarDocumentoNfCeValido();
        var result = _builder.ConstruirRps(doc, tenant);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NfseXml.TipoInvalido");
    }

    /// <summary>
    /// O domínio protege a criação de DocumentoFiscal NFSe sem Tomador (TomadorObrigatorio error).
    /// Este teste verifica que a proteção existe no domínio — o builder nunca receberá um NFSe sem Tomador.
    /// </summary>
    [Fact]
    public void ConstruirRps_SemTomador_DominioRejeita()
    {
        var timeProvider = TimeProvider.System;
        var tenant = NfseTestHelpers.CriarTenantNfse();
        var servico = NfseTestHelpers.ServicoNfseValido();
        var item = NfseTestHelpers.ItemServicoMinimo();

        var result = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: tenant.Id, clienteAppId: tenant.ClienteAppId,
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe, chaveAcesso: null, numero: 1L, serie: "001",
            indPresenca: 0, items: [item], pagamentos: [],
            timeProvider: timeProvider, tomador: null, servicoNfse: servico);

        result.IsFailure.ShouldBeTrue("O domínio deve rejeitar NFSe sem Tomador.");
    }

    /// <summary>
    /// O domínio protege a criação de DocumentoFiscal NFSe sem ServicoNfse (ServicoNfseObrigatorio error).
    /// Este teste verifica que a proteção existe no domínio — o builder nunca receberá um NFSe sem Servico.
    /// </summary>
    [Fact]
    public void ConstruirRps_SemServicoNfse_DominioRejeita()
    {
        var timeProvider = TimeProvider.System;
        var tenant = NfseTestHelpers.CriarTenantNfse();
        var tomador = NfseTestHelpers.TomadorValido();
        var item = NfseTestHelpers.ItemServicoMinimo();

        var result = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: tenant.Id, clienteAppId: tenant.ClienteAppId,
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe, chaveAcesso: null, numero: 1L, serie: "001",
            indPresenca: 0, items: [item], pagamentos: [],
            timeProvider: timeProvider, tomador: tomador, servicoNfse: null);

        result.IsFailure.ShouldBeTrue("O domínio deve rejeitar NFSe sem ServicoNfse.");
    }

    [Fact]
    public void ConstruirRps_InscricaoMunicipalAusente_RetornaFalha()
    {
        var (doc, tenant) = CriarDocumentoNfseValido(semInscricaoMunicipal: true);
        var result = _builder.ConstruirRps(doc, tenant);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NfseXml.InscricaoMunicipalAusente");
    }

    private static (DocumentoFiscal Doc, Tenant Tenant) CriarDocumentoNfseValido(
        bool semInscricaoMunicipal = false)
    {
        var timeProvider = TimeProvider.System;
        var tenant = NfseTestHelpers.CriarTenantNfse(semInscricaoMunicipal: semInscricaoMunicipal);
        var tomador = NfseTestHelpers.TomadorValido();
        var servico = NfseTestHelpers.ServicoNfseValido();
        var item = NfseTestHelpers.ItemServicoMinimo();
        var doc = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: tenant.Id, clienteAppId: tenant.ClienteAppId,
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe, chaveAcesso: null, numero: 1L, serie: "001",
            indPresenca: 0, items: [item], pagamentos: [],
            timeProvider: timeProvider, tomador: tomador, servicoNfse: servico).Value;
        return (doc, tenant);
    }

    private static (DocumentoFiscal Doc, Tenant Tenant) CriarDocumentoNfCeValido()
    {
        var timeProvider = TimeProvider.System;
        var tenant = NfseTestHelpers.CriarTenantNfse();
        var chave = ChaveAcesso.Gerar(35, timeProvider.GetUtcNow().ToString("yyMM"),
            "11222333000181", 65, "001", "000000001", TipoEmissao.Normal, "00000001").Value;
        var produto = Produto.Criar("PROD01", "Produto", "12345678", null, "5102", "UN",
            1m, 10m, 0m, OrigemMercadoria.Nacional).Value;
        var tributo = Tributo.Criar(TipoIcms.CSOSN, 400, 0m, 0m, 0m,
            CstPisCofins.Cst07, 0m, 0m, 0m, CstPisCofins.Cst07, 0m, 0m, 0m).Value;
        var pagamento = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;
        var doc = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: tenant.Id, clienteAppId: tenant.ClienteAppId,
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NfCe, chaveAcesso: chave, numero: 1L, serie: "001",
            indPresenca: 1, items: [new ItemDocumento(1, produto, tributo)],
            pagamentos: [pagamento], timeProvider: timeProvider).Value;
        return (doc, tenant);
    }
}
