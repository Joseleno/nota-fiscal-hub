using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class ConfiguracaoFiscalTests
{
    [Fact]
    public void Criar_SemSerieNfe_Sucesso_SerieNfeNula()
    {
        var result = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35, null);
        result.IsSuccess.ShouldBeTrue();
        result.Value.SerieNfe.ShouldBeNull();
    }

    [Fact]
    public void Criar_ComSerieNfeValida_Sucesso()
    {
        var result = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35, null,
            serieNfe: "001");
        result.IsSuccess.ShouldBeTrue();
        result.Value.SerieNfe.ShouldBe("001");
    }

    [Fact]
    public void Criar_ComSerieNfeInvalida_Retorna_Falha()
    {
        var result = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35, null,
            serieNfe: "ABCD");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_ComInscricaoMunicipal_Sucesso()
    {
        var result = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35,
            inscricaoEstadual: null,
            serieNfe: null,
            inscricaoMunicipal: "1234567");

        result.IsSuccess.ShouldBeTrue();
        result.Value.InscricaoMunicipal.ShouldBe("1234567");
    }

    [Fact]
    public void Criar_SemInscricaoMunicipal_InscricaoMunicipalNula()
    {
        var result = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35,
            inscricaoEstadual: null,
            serieNfe: null,
            inscricaoMunicipal: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.InscricaoMunicipal.ShouldBeNull();
    }
}
