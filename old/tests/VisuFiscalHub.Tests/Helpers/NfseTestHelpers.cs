using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Helpers;

public static class NfseTestHelpers
{
    public static Tenant CriarTenantNfse(bool semInscricaoMunicipal = false)
    {
        var timeProvider = TimeProvider.System;
        var clienteAppId = new ClienteAppId(Guid.NewGuid());
        var configuracao = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35,
            inscricaoEstadual: null,
            inscricaoMunicipal: semInscricaoMunicipal ? null : "123456789").Value;
        var endereco = Endereco.Criar(
            logradouro: "Rua Teste", numero: "100", complemento: null, bairro: "Centro",
            municipio: "São Paulo", codigoMunicipio: 3550308, uf: "SP", cep: "01310100").Value;
        return Tenant.Criar(
            clienteAppId: clienteAppId, cnpj: Cnpj.Criar("11222333000181").Value,
            razaoSocial: "Empresa Teste LTDA", nomeFantasia: null,
            configuracaoFiscal: configuracao, endereco: endereco, timeProvider: timeProvider).Value;
    }

    public static Tomador TomadorValido() => Tomador.FromStorage(
        cnpjOuCpf: "44555666000177", razaoSocial: "Tomador Teste LTDA",
        logradouro: "Av Paulista", numero: "1000", complemento: null, bairro: "Bela Vista",
        municipio: "São Paulo", codigoMunicipio: "3550308", uf: "SP", cep: "01310100",
        email: null, inscricaoMunicipal: null);

    public static ServicoNfse ServicoNfseValido() => ServicoNfse.Criar(
        codigoServico: "1.01", discriminacao: "Serviço de consultoria de TI",
        codigoTributacaoMunicipio: null, aliquotaIss: 2.00m, baseCalculoIss: 1000.00m,
        valorIss: 20.00m, valorDeducoes: null, issRetido: false).Value;

    public static ItemDocumento ItemServicoMinimo()
    {
        var produto = Produto.Criar("SVC001", "Serviço", "00000000", null, "5301", "UN",
            1m, 1000m, 0m, OrigemMercadoria.Nacional).Value;
        var tributo = Tributo.Criar(TipoIcms.CSOSN, 400, 0m, 0m, 0m,
            CstPisCofins.Cst07, 0m, 0m, 0m, CstPisCofins.Cst07, 0m, 0m, 0m).Value;
        return new ItemDocumento(1, produto, tributo);
    }
}
