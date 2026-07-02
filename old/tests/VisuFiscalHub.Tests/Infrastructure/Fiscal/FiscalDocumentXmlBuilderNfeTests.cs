using System.Xml;
using Microsoft.Extensions.Configuration;
using Shouldly;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Certificates;
using VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class FiscalDocumentXmlBuilderNfeTests
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    private static FiscalDocumentXmlBuilder CreateBuilder()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CERT:EncryptionKey"] = key })
            .Build();
        var enc = new CertificateEncryptionService(cfg);
        return new FiscalDocumentXmlBuilder(new QrCodeGenerator(), enc);
    }

    private static Tenant CreateNfeTenant()
    {
        var cnpj = Cnpj.Criar("11.222.333/0001-81").Value;
        var config = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35).Value;
        var endereco = Endereco.Criar(
            "Rua Teste", "100", null, "Centro", "São Paulo", 3550308, "SP", "01310100").Value;
        return Tenant.Criar(ClienteAppId.New(), cnpj, "Empresa Teste", null, config, endereco, TimeProvider.System).Value;
    }

    [Fact]
    public void Construir_Nfe_XmlContemMod55()
    {
        var builder = CreateBuilder();
        var doc = DocumentoFiscalBuilder.AutorizadoNfe(DateTimeOffset.UtcNow);
        var tenant = CreateNfeTenant();

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var ns = new XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);
        result.Value.SelectSingleNode("//nfe:mod", ns)!.InnerText.ShouldBe("55");
    }

    [Fact]
    public void Construir_Nfe_XmlNaoContemInfNFeSupl()
    {
        var builder = CreateBuilder();
        var doc = DocumentoFiscalBuilder.AutorizadoNfe(DateTimeOffset.UtcNow);
        var tenant = CreateNfeTenant();

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var ns = new XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);
        result.Value.SelectSingleNode("//nfe:infNFeSupl", ns).ShouldBeNull();
    }

    [Fact]
    public void Construir_Nfe_XmlContemDest()
    {
        var builder = CreateBuilder();
        var doc = DocumentoFiscalBuilder.AutorizadoNfe(DateTimeOffset.UtcNow);
        var tenant = CreateNfeTenant();

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var ns = new XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);
        result.Value.SelectSingleNode("//nfe:dest", ns).ShouldNotBeNull();
    }

    [Fact]
    public void Construir_Nfce_XmlContemMod65()
    {
        var builder = CreateBuilder();
        var tenant = CreateNfeTenant();
        // Add CSC/CIdToken for NFC-e
        var key = Convert.ToBase64String(new byte[32]);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CERT:EncryptionKey"] = key })
            .Build();
        var enc = new CertificateEncryptionService(cfg);
        var cscEncryptado = enc.EncryptString("0123456789").Value;
        tenant.AtualizarCsc(cscEncryptado, "000001");

        var docFiscal = DocumentoFiscalBuilder.Processando();

        var result = builder.Construir(docFiscal, tenant);

        result.IsSuccess.ShouldBeTrue();
        var ns = new XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", NfeNs);
        result.Value.SelectSingleNode("//nfe:mod", ns)!.InnerText.ShouldBe("65");
    }
}
