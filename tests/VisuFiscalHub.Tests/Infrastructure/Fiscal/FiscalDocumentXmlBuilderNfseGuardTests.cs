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

public class FiscalDocumentXmlBuilderNfseGuardTests
{
    private static FiscalDocumentXmlBuilder CreateBuilder()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["CERT:EncryptionKey"] = key })
            .Build();
        var enc = new CertificateEncryptionService(cfg);
        return new FiscalDocumentXmlBuilder(new QrCodeGenerator(), enc);
    }

    private static Tenant CreateTenant()
    {
        var cnpj = Cnpj.Criar("11.222.333/0001-81").Value;
        var config = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35).Value;
        var endereco = Endereco.Criar(
            "Rua Teste", "100", null, "Centro", "São Paulo", 3550308, "SP", "01310100").Value;
        return Tenant.Criar(ClienteAppId.New(), cnpj, "Empresa Teste", null, config, endereco, TimeProvider.System).Value;
    }

    private static DocumentoFiscal CreateDocumentoNfse()
    {
        var chaveAcesso = ChaveAcesso.Gerar(
            cUF: 35,
            aamm: "2601",
            cnpj: "11222333000181",
            mod: 65,
            serie: "001",
            nNF: "000000001",
            tpEmis: TipoEmissao.Normal,
            cNF: "12345678").Value;

        var tributo = Tributo.Criar(
            tipoIcms: TipoIcms.CSOSN, csosnOuCst: 400,
            aliquotaIcms: 0m, baseCalculoIcms: 0m, valorIcms: 0m,
            cstPis: CstPisCofins.Cst07, baseCalculoPis: 0m, aliquotaPis: 0m, valorPis: 0m,
            cstCofins: CstPisCofins.Cst07, baseCalculoCofins: 0m, aliquotaCofins: 0m, valorCofins: 0m).Value;

        var produto = Produto.Criar(
            "PROD001", "Produto Teste", "12345678", null, "5102",
            "UN", 1m, 10m, 0m, OrigemMercadoria.Nacional).Value;

        var item = new ItemDocumento(1, produto, tributo);
        var pagamento = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;

        // NFSe: usamos tipo NFSe — o domínio aceita (indPresenca=1 está em IndPresencaNfce)
        // ChaveAcesso usa mod=65 apenas para construção válida da chave (o guard no XmlBuilder
        // dispara antes de inspecionar o conteúdo da chave).
        var doc = DocumentoFiscal.Criar(
            id: DocumentoFiscalId.New(),
            tenantId: new TenantId(Guid.NewGuid()),
            clienteAppId: ClienteAppId.New(),
            idempotencyKey: "idem-nfse-guard-test",
            tipo: TipoDocumento.NFSe,
            chaveAcesso: chaveAcesso,
            numero: 1,
            serie: "001",
            indPresenca: 1,
            items: [item],
            pagamentos: [pagamento],
            timeProvider: TimeProvider.System);

        doc.IsSuccess.ShouldBeTrue("Falha ao criar DocumentoFiscal com TipoDocumento.NFSe para o teste de guard.");
        return doc.Value;
    }

    [Fact]
    public void Construir_QuandoTipoNFSe_DeveLancarNotSupportedException()
    {
        var builder = CreateBuilder();
        var documento = CreateDocumentoNfse();
        var tenant = CreateTenant();

        var act = () => builder.Construir(documento, tenant);

        act.ShouldThrow<NotSupportedException>()
           .Message.ShouldContain("NFSe");
    }
}
