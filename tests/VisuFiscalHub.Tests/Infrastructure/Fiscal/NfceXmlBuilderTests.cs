using Microsoft.Extensions.Configuration;
using Shouldly;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Certificates;
using VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class NfceXmlBuilderTests
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private static CertificateEncryptionService CriarEncryption()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CERT:EncryptionKey"] = key })
            .Build();
        return new CertificateEncryptionService(cfg);
    }

    private static NfceXmlBuilder CriarBuilder()
    {
        var enc = CriarEncryption();
        return new NfceXmlBuilder(new QrCodeGenerator(), enc);
    }

    private static Tenant CriarTenant(CertificateEncryptionService enc)
    {
        var cnpj = Cnpj.Criar("11.222.333/0001-81").Value;
        var config = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35).Value;
        var endereco = Endereco.Criar(
            "Rua Teste", "100", null, "Centro", "São Paulo", 3550308, "SP", "01310100").Value;

        var tenant = Tenant.Criar(
            ClienteAppId.New(), cnpj, "Empresa Teste", null, config, endereco, TimeProvider.System).Value;

        // CSC e cIdToken necessários para gerar o QR Code
        var cscEncryptado = enc.EncryptString("0123456789").Value;
        tenant.AtualizarCsc(cscEncryptado, "000001");

        return tenant;
    }

    private static DocumentoFiscal CriarDocumento(TenantId tenantId)
    {
        var chave = ChaveAcesso.Gerar(35, "2601", "11222333000181", 65, "001",
            "000000001", TipoEmissao.Normal, "12345678").Value;

        var tributo = Tributo.Criar(
            TipoIcms.CSOSN, 400, 0m, 0m, 0m,
            CstPisCofins.Cst07, 0m, 0m, 0m,
            CstPisCofins.Cst07, 0m, 0m, 0m).Value;

        var produto = Produto.Criar(
            "P001", "Produto Teste", "12345678", null, "5102",
            "UN", 1m, 10m, 0m, OrigemMercadoria.Nacional).Value;

        var item = new ItemDocumento(1, produto, tributo);
        var pag = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;

        return DocumentoFiscal.Criar(
            DocumentoFiscalId.New(), tenantId, ClienteAppId.New(),
            $"idem-{Guid.NewGuid()}", TipoDocumento.NfCe,
            chave, 1, "001", 1, [item], [pag], TimeProvider.System).Value;
    }

    // ──────────────────────────────────────────────────────────────
    // Testes
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Construir_InfNFeSupl_DeveSerFilhoDeInfNFe()
    {
        var enc = CriarEncryption();
        var builder = new NfceXmlBuilder(new QrCodeGenerator(), enc);
        var tenant = CriarTenant(enc);
        var doc = CriarDocumento(tenant.Id);

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : "");

        var xml = result.Value;
        var ns = new System.Xml.XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        // infNFeSupl deve ser filho DIRETO de infNFe — não de NFe
        var infNFeSupl = xml.SelectSingleNode("//nfe:NFe/nfe:infNFe/nfe:infNFeSupl", ns);
        infNFeSupl.ShouldNotBeNull(
            "infNFeSupl deve ser filho de infNFe, não de NFe (schema NFC-e 4.0)");

        // Confirmar que NÃO está como filho direto de NFe (seria o bug)
        var infNFeSuplErrado = xml.SelectSingleNode("/nfe:NFe/nfe:infNFeSupl", ns);
        infNFeSuplErrado.ShouldBeNull(
            "infNFeSupl não deve ser filho direto de NFe");
    }

    [Fact]
    public void Construir_InfNFeSupl_DeveConterQrCode()
    {
        var enc = CriarEncryption();
        var builder = new NfceXmlBuilder(new QrCodeGenerator(), enc);
        var tenant = CriarTenant(enc);
        var doc = CriarDocumento(tenant.Id);

        var result = builder.Construir(doc, tenant);
        result.IsSuccess.ShouldBeTrue();

        var ns = new System.Xml.XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var qrCode = result.Value.SelectSingleNode("//nfe:infNFeSupl/nfe:qrCode", ns);
        qrCode.ShouldNotBeNull("infNFeSupl deve conter elemento qrCode");
        qrCode!.InnerText.ShouldNotBeNullOrWhiteSpace("qrCode deve ter URL não-vazia");
    }

    [Fact]
    public void Construir_InfNFeSupl_DeveConterUrlFe()
    {
        var enc = CriarEncryption();
        var builder = new NfceXmlBuilder(new QrCodeGenerator(), enc);
        var tenant = CriarTenant(enc);
        var doc = CriarDocumento(tenant.Id);

        var result = builder.Construir(doc, tenant);
        result.IsSuccess.ShouldBeTrue();

        var ns = new System.Xml.XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var urlFe = result.Value.SelectSingleNode("//nfe:infNFeSupl/nfe:urlFe", ns);
        urlFe.ShouldNotBeNull("infNFeSupl deve conter elemento urlFe");
        urlFe!.InnerText.ShouldNotBeNullOrWhiteSpace("urlFe deve ter URL não-vazia");
    }

    [Fact]
    public void Construir_ProcEmi_DeveSerIgualA3()
    {
        var enc = CriarEncryption();
        var builder = new NfceXmlBuilder(new QrCodeGenerator(), enc);
        var tenant = CriarTenant(enc);
        var doc = CriarDocumento(tenant.Id);

        var result = builder.Construir(doc, tenant);
        result.IsSuccess.ShouldBeTrue();

        var ns = new System.Xml.XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var procEmi = result.Value.SelectSingleNode("//nfe:ide/nfe:procEmi", ns);
        procEmi.ShouldNotBeNull();
        procEmi!.InnerText.ShouldBe("3", "procEmi deve ser 3 (emissão por API do contribuinte)");
    }

    [Fact]
    public void Construir_VerProc_DeveEstarPresente()
    {
        var enc = CriarEncryption();
        var builder = new NfceXmlBuilder(new QrCodeGenerator(), enc);
        var tenant = CriarTenant(enc);
        var doc = CriarDocumento(tenant.Id);

        var result = builder.Construir(doc, tenant);
        result.IsSuccess.ShouldBeTrue();

        var ns = new System.Xml.XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var verProc = result.Value.SelectSingleNode("//nfe:ide/nfe:verProc", ns);
        verProc.ShouldNotBeNull();
        verProc!.InnerText.ShouldNotBeNullOrWhiteSpace("verProc deve estar preenchido");
    }

    [Fact]
    public void Construir_CIdToken_DeveEstarPresente()
    {
        var enc = CriarEncryption();
        var builder = new NfceXmlBuilder(new QrCodeGenerator(), enc);
        var tenant = CriarTenant(enc);
        var doc = CriarDocumento(tenant.Id);

        var result = builder.Construir(doc, tenant);
        result.IsSuccess.ShouldBeTrue();

        var ns = new System.Xml.XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var cIdToken = result.Value.SelectSingleNode("//nfe:ide/nfe:cIdToken", ns);
        cIdToken.ShouldNotBeNull("cIdToken deve estar presente no ide");
        cIdToken!.InnerText.ShouldBe("000001");
    }

    [Fact]
    public void Construir_QrCodeUrl_NaoDeveConterCsc()
    {
        var enc = CriarEncryption();
        var builder = new NfceXmlBuilder(new QrCodeGenerator(), enc);
        var tenant = CriarTenant(enc);
        var doc = CriarDocumento(tenant.Id);

        var result = builder.Construir(doc, tenant);
        result.IsSuccess.ShouldBeTrue();

        var ns = new System.Xml.XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var qrCodeUrl = result.Value.SelectSingleNode("//nfe:infNFeSupl/nfe:qrCode", ns)?.InnerText ?? "";
        qrCodeUrl.ShouldNotContain("0123456789");
        // CSC não deve aparecer na URL do QR Code
    }
}
