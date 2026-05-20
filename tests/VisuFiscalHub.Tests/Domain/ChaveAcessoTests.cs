using Shouldly;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class ChaveAcessoTests
{
    private static Result<ChaveAcesso> GerarPadrao(string cNF = "00000001", string nNF = "000000001")
        => ChaveAcesso.Gerar(
            cUF: 35, aamm: "2601", cnpj: "14200167140065",
            mod: 65, serie: "001", nNF: nNF,
            tpEmis: TipoEmissao.Normal, cNF: cNF);

    [Fact]
    public void Gerar_QuandoCamposValidos_DeveRetornarChaveDe44Digitos()
    {
        var result = GerarPadrao();
        result.IsSuccess.ShouldBeTrue();
        result.Value.Valor.Length.ShouldBe(44);
        result.Value.Valor.ShouldAllBe(c => char.IsDigit(c));
    }

    // cDV Módulo 11 — vetores de referência
    // Vetor MOC 7.0: "3509061420016714006512500100000180010000009" → soma=492, resto=8, cDV=11-8=3
    // Vetores sintéticos: cUF=35, aamm=2601, cnpj=14200167140065, mod=65, serie=001, nNF=000000001, tpEmis=1
    [Theory]
    [InlineData("3509061420016714006512500100000180010000009", 3)]  // MOC 7.0
    [InlineData("3526011420016714006565001000000001100000000", 0)]  // resto=1 < 2 → cDV=0
    [InlineData("3526011420016714006565001000000001100000001", 9)]  // resto=2 → cDV=9
    [InlineData("3526011420016714006565001000000001100000013", 2)]  // resto=9 → cDV=2
    public void Gerar_CDV_ModuloOnzeCorreto(string quarentaTresDig, int cDVEsperado)
    {
        var cUF    = int.Parse(quarentaTresDig[..2]);
        var aamm   = quarentaTresDig[2..6];
        var cnpj   = quarentaTresDig[6..20];
        var mod    = int.Parse(quarentaTresDig[20..22]);
        var serie  = quarentaTresDig[22..25];
        var nNF    = quarentaTresDig[25..34];
        var tpEmis = (TipoEmissao)int.Parse(quarentaTresDig[34..35]);
        var cNF    = quarentaTresDig[35..43];

        var result = ChaveAcesso.Gerar(cUF, aamm, cnpj, mod, serie, nNF, tpEmis, cNF);

        result.IsSuccess.ShouldBeTrue($"Gerar falhou para {quarentaTresDig}");
        var cDVReal = result.Value.Valor[43] - '0';
        cDVReal.ShouldBe(cDVEsperado, $"cDV errado para {quarentaTresDig}");
    }

    [Fact]
    public void Gerar_QuandoCnpjComMenosDe14Digitos_DeveRetornarErro()
    {
        var result = ChaveAcesso.Gerar(35, "2601", "1234567890123", 65, "001", "000000001", TipoEmissao.Normal, "00000001");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Gerar_QuandoAammComMesInvalido_DeveRetornarErro()
    {
        var result = ChaveAcesso.Gerar(35, "2613", "14200167140065", 65, "001", "000000001", TipoEmissao.Normal, "00000001");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void From_QuandoChave44DigitosValida_DeveRetornarSucesso()
    {
        var chave = "35090614200167140065125001000001800100000093";
        var result = ChaveAcesso.From(chave);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Valor.ShouldBe(chave);
    }

    [Fact]
    public void From_QuandoChaveCom43Digitos_DeveRetornarErro()
    {
        var result = ChaveAcesso.From("3509061420016714006512500100000180010000009");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void From_QuandoChaveComLetras_DeveRetornarErro()
    {
        var result = ChaveAcesso.From("3509061420016714006512500100000180010000X093");
        result.IsFailure.ShouldBeTrue();
    }
}
