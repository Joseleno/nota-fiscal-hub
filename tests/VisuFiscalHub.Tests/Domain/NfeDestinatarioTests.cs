using Shouldly;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class NfeDestinatarioTests
{
    private static NfeDestinatario ValidCnpj() => NfeDestinatario.Criar(
        cnpjOuCpf: "11222333000181",
        razaoSocial: "Empresa Teste Ltda",
        indIeDest: 1,
        ie: "123456789",
        logradouro: "Rua Teste",
        numero: "100",
        complemento: null,
        bairro: "Centro",
        municipio: "São Paulo",
        codigoMunicipio: "3550308",
        uf: "SP",
        cep: "01310100",
        email: "nfe@empresa.com").Value;

    [Fact]
    public void Criar_ComCnpjValido_Sucesso()
    {
        var dest = ValidCnpj();
        dest.CnpjOuCpf.ShouldBe("11222333000181");
        dest.RazaoSocial.ShouldBe("Empresa Teste Ltda");
        dest.IndIeDest.ShouldBe(1);
    }

    [Fact]
    public void Criar_ComCpfValido_Sucesso()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "52998224725",
            razaoSocial: "Pessoa Fisica",
            indIeDest: 9,
            ie: null,
            logradouro: "Av Paulista",
            numero: "1",
            complemento: null,
            bairro: "Bela Vista",
            municipio: "São Paulo",
            codigoMunicipio: "3550308",
            uf: "SP",
            cep: "01310100",
            email: null);
        result.IsSuccess.ShouldBeTrue();
        result.Value.CnpjOuCpf.ShouldBe("52998224725");
    }

    [Fact]
    public void Criar_CnpjOuCpfInvalido_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "123",
            razaoSocial: "X",
            indIeDest: 9, ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.NfeDestinatarioInvalido");
    }

    [Fact]
    public void Criar_RazaoSocialVazia_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "",
            indIeDest: 1, ie: "123",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_IndIeDestInvalido_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            indIeDest: 5,
            ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_IndIeDest1_SemIe_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            indIeDest: 1, ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_CepInvalido_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            indIeDest: 9, ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "ABC",
            email: null);
        result.IsFailure.ShouldBeTrue();
    }
}
