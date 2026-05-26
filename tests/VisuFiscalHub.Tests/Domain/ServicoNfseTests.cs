using Shouldly;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class ServicoNfseTests
{
    [Fact]
    public void Criar_ComDadosValidos_Sucesso()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "Desenvolvimento de software sob encomenda",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2.0m,
            baseCalculoIss: 1000.00m,
            valorIss: 20.00m,
            valorDeducoes: null,
            issRetido: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CodigoServico.ShouldBe("1.01");
        result.Value.AliquotaIss.ShouldBe(2.0m);
        result.Value.ValorIss.ShouldBe(20.00m);
        result.Value.IssRetido.ShouldBeFalse();
    }

    [Fact]
    public void Criar_CodigoServicoVazio_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "",
            discriminacao: "Serviço",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2.0m,
            baseCalculoIss: 100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.ServicoNfseInvalido");
    }

    [Fact]
    public void Criar_DiscriminacaoVazia_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2.0m,
            baseCalculoIss: 100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.ServicoNfseInvalido");
    }

    [Fact]
    public void Criar_AliquotaNegativa_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "Serviço válido",
            codigoTributacaoMunicipio: null,
            aliquotaIss: -1m,
            baseCalculoIss: 100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.ServicoNfseInvalido");
    }

    [Fact]
    public void Criar_BaseCalculoNegativa_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "Serviço válido",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2m,
            baseCalculoIss: -100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.ServicoNfseInvalido");
    }

    [Fact]
    public void Criar_IssRetidoTrue_Sucesso()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "17.06",
            discriminacao: "Assessoria e consultoria",
            codigoTributacaoMunicipio: "170600100",
            aliquotaIss: 5.0m,
            baseCalculoIss: 5000.00m,
            valorIss: 250.00m,
            valorDeducoes: 500.00m,
            issRetido: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IssRetido.ShouldBeTrue();
        result.Value.ValorDeducoes.ShouldBe(500.00m);
        result.Value.CodigoTributacaoMunicipio.ShouldBe("170600100");
    }
}
