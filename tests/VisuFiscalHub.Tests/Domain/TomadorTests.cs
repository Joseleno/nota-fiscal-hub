using Shouldly;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class TomadorTests
{
    [Fact]
    public void Criar_ComCnpjValido_Sucesso()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "Empresa Tomadora Ltda",
            logradouro: "Rua Teste",
            numero: "100",
            complemento: null,
            bairro: "Centro",
            municipio: "São Paulo",
            codigoMunicipio: "3550308",
            uf: "SP",
            cep: "01310100",
            email: null,
            inscricaoMunicipal: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CnpjOuCpf.ShouldBe("11222333000181");
        result.Value.RazaoSocial.ShouldBe("Empresa Tomadora Ltda");
    }

    [Fact]
    public void Criar_ComCpfValido_Sucesso()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "52998224725",
            razaoSocial: "Pessoa Fisica",
            logradouro: "Av Paulista",
            numero: "1",
            complemento: null,
            bairro: "Bela Vista",
            municipio: "São Paulo",
            codigoMunicipio: "3550308",
            uf: "SP",
            cep: "01310100",
            email: null,
            inscricaoMunicipal: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CnpjOuCpf.ShouldBe("52998224725");
    }

    [Fact]
    public void Criar_CnpjOuCpfInvalido_Retorna_Falha()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "123",
            razaoSocial: "X",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null, inscricaoMunicipal: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TomadorInvalido");
    }

    [Fact]
    public void Criar_RazaoSocialVazia_Retorna_Falha()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null, inscricaoMunicipal: null);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_CepInvalido_Retorna_Falha()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "ABC", email: null, inscricaoMunicipal: null);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_ComInscricaoMunicipal_Sucesso()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "Empresa Com IE Municipal",
            logradouro: "Rua X", numero: "1", complemento: null,
            bairro: "B", municipio: "São Paulo", codigoMunicipio: "3550308",
            uf: "SP", cep: "01310100", email: "test@emp.com", inscricaoMunicipal: "1234567");

        result.IsSuccess.ShouldBeTrue();
        result.Value.InscricaoMunicipal.ShouldBe("1234567");
    }
}
