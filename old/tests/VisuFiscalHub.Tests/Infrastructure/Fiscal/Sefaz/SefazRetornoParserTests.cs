using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal.Sefaz;

public class SefazRetornoParserTests
{
    // ── IsDuplicidade ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public void IsDuplicidade_CodigoDuplicidade_RetornaTrue(string cStat)
        => SefazRetornoParser.IsDuplicidade(cStat).ShouldBeTrue();

    [Theory]
    [InlineData("100")]
    [InlineData("110")]
    [InlineData("999")]
    [InlineData("")]
    public void IsDuplicidade_CodigoNaoDuplicidade_RetornaFalse(string cStat)
        => SefazRetornoParser.IsDuplicidade(cStat).ShouldBeFalse();

    // ── IsDenegado ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("110")]
    [InlineData("301")]
    [InlineData("302")]
    public void IsDenegado_CodigoDenegado_RetornaTrue(string cStat)
        => SefazRetornoParser.IsDenegado(cStat).ShouldBeTrue();

    [Theory]
    [InlineData("100")]
    [InlineData("204")]
    [InlineData("572")]
    [InlineData("999")]
    [InlineData("")]
    public void IsDenegado_CodigoNaoDenegado_RetornaFalse(string cStat)
        => SefazRetornoParser.IsDenegado(cStat).ShouldBeFalse();

    // ── IsNaoEncontrado ─────────────────────────────────────────────────────────

    [Fact]
    public void IsNaoEncontrado_CStat217_RetornaTrue()
        => SefazRetornoParser.IsNaoEncontrado("217").ShouldBeTrue();

    [Theory]
    [InlineData("100")]
    [InlineData("204")]
    [InlineData("110")]
    [InlineData("999")]
    [InlineData("")]
    public void IsNaoEncontrado_OutrosCodigos_RetornaFalse(string cStat)
        => SefazRetornoParser.IsNaoEncontrado(cStat).ShouldBeFalse();

    // ── IsRecuperavel ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("108")]
    [InlineData("150")]
    [InlineData("199")]
    public void IsRecuperavel_CodigoMenor200ExcetoDenego_RetornaTrue(string cStat)
        => SefazRetornoParser.IsRecuperavel(cStat).ShouldBeTrue();

    [Fact]
    public void IsRecuperavel_CStat110_RetornaFalse()
        => SefazRetornoParser.IsRecuperavel("110").ShouldBeFalse();

    [Theory]
    [InlineData("200")]
    [InlineData("204")]
    [InlineData("301")]
    [InlineData("302")]
    [InlineData("400")]
    [InlineData("500")]
    [InlineData("572")]
    [InlineData("999")]
    public void IsRecuperavel_Codigo200OuMaior_RetornaFalse(string cStat)
        => SefazRetornoParser.IsRecuperavel(cStat).ShouldBeFalse();

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsRecuperavel_CStatMalFormado_RetornaTrue(string cStat)
        => SefazRetornoParser.IsRecuperavel(cStat).ShouldBeTrue();

    // ── Parse — retorno de autorização ─────────────────────────────────────────

    [Fact]
    public void Parse_CStat100_RetornaAutorizadoComProtocolo()
    {
        var soap = BuildSoapAutorizacao(cStat: "100", nProt: "315230012345678", xmlProtBody: "<protNFe><infProt><nProt>315230012345678</nProt></infProt></protNFe>");

        var result = SefazRetornoParser.Parse(soap);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeTrue();
        result.Value.CStat.ShouldBe("100");
        result.Value.NProt.ShouldBe("315230012345678");
        result.Value.XmlAutorizado.ShouldNotBeNull();
        result.Value.XmlAutorizado.ShouldContain("315230012345678");
    }

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public void Parse_CStatDuplicidade_NaoAutorizado(string cStat)
    {
        var soap = BuildSoapAutorizacao(cStat: cStat, nProt: null, xmlProtBody: null);

        var result = SefazRetornoParser.Parse(soap);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeFalse();
        result.Value.CStat.ShouldBe(cStat);
        result.Value.XmlAutorizado.ShouldBeNull();
    }

    [Theory]
    [InlineData("110")]
    [InlineData("301")]
    [InlineData("302")]
    public void Parse_CStatDenegado_NaoAutorizado(string cStat)
    {
        var soap = BuildSoapAutorizacao(cStat: cStat, nProt: null, xmlProtBody: null);

        var result = SefazRetornoParser.Parse(soap);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeFalse();
        result.Value.CStat.ShouldBe(cStat);
    }

    [Fact]
    public void Parse_XmlInvalido_RetornaFalha()
    {
        var result = SefazRetornoParser.Parse("não é xml");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sefaz.XmlInvalido");
    }

    [Fact]
    public void Parse_SemRetEnviNFe_RetornaFalha()
    {
        var soap = "<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"><soap:Body><outro/></soap:Body></soap:Envelope>";

        var result = SefazRetornoParser.Parse(soap);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sefaz.RetornoInvalido");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static string BuildSoapAutorizacao(string cStat, string? nProt, string? xmlProtBody)
    {
        var nProtEl   = nProt is null ? "" : $"<nfe:nProt xmlns:nfe=\"http://www.portalfiscal.inf.br/nfe\">{nProt}</nfe:nProt>";
        var protNfeEl = xmlProtBody is null ? "" : $"<nfe:protNFe xmlns:nfe=\"http://www.portalfiscal.inf.br/nfe\">{xmlProtBody}</nfe:protNFe>";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body>
                <nfe:retEnviNFe xmlns:nfe="http://www.portalfiscal.inf.br/nfe">
                  <nfe:cStat>{cStat}</nfe:cStat>
                  <nfe:xMotivo>Motivo teste</nfe:xMotivo>
                  {nProtEl}
                  {protNfeEl}
                </nfe:retEnviNFe>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }
}
