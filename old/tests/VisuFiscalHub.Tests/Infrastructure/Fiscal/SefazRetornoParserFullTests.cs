using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class SefazRetornoParserFullTests
{
    // cStat and xMotivo live on retEnviNFe; nProt anywhere under it via .//nfe:nProt
    private const string SoapCstat100 =
        """
        <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
          <soap12:Body>
            <nfe:retEnviNFe xmlns:nfe="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <nfe:cStat>100</nfe:cStat>
              <nfe:xMotivo>Autorizado o uso da NF-e</nfe:xMotivo>
              <nfe:nProt>135260000000001</nfe:nProt>
              <nfe:protNFe><nfe:infProt><nfe:nProt>135260000000001</nfe:nProt></nfe:infProt></nfe:protNFe>
            </nfe:retEnviNFe>
          </soap12:Body>
        </soap12:Envelope>
        """;

    private const string SoapCstat110 =
        """
        <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
          <soap12:Body>
            <nfe:retEnviNFe xmlns:nfe="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <nfe:cStat>110</nfe:cStat>
              <nfe:xMotivo>Uso Denegado. CNPJ do emitente com irregularidade na SEFAZ</nfe:xMotivo>
            </nfe:retEnviNFe>
          </soap12:Body>
        </soap12:Envelope>
        """;

    private const string SoapCstat204 =
        """
        <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
          <soap12:Body>
            <nfe:retEnviNFe xmlns:nfe="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <nfe:cStat>204</nfe:cStat>
              <nfe:xMotivo>Duplicidade de NF-e</nfe:xMotivo>
            </nfe:retEnviNFe>
          </soap12:Body>
        </soap12:Envelope>
        """;

    [Fact]
    public void Parse_cStat100_DeveRetornarAutorizado()
    {
        var result = SefazRetornoParser.Parse(SoapCstat100);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeTrue();
        result.Value.CStat.ShouldBe("100");
        result.Value.NProt.ShouldBe("135260000000001");
        result.Value.XmlAutorizado.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_cStat100_XMotivoPreservado()
    {
        var result = SefazRetornoParser.Parse(SoapCstat100);

        result.IsSuccess.ShouldBeTrue();
        result.Value.XMotivo.ShouldBe("Autorizado o uso da NF-e");
    }

    [Fact]
    public void Parse_cStat110_DeveRetornarNaoAutorizado_EhDenegado()
    {
        var result = SefazRetornoParser.Parse(SoapCstat110);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeFalse();
        result.Value.CStat.ShouldBe("110");
        result.Value.XmlAutorizado.ShouldBeNull();
        SefazRetornoParser.IsDenegado(result.Value.CStat).ShouldBeTrue();
        result.Value.XMotivo.ShouldContain("Denegad");
    }

    [Fact]
    public void Parse_cStat204_DeveRetornarNaoAutorizado_EhDuplicidade()
    {
        var result = SefazRetornoParser.Parse(SoapCstat204);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeFalse();
        result.Value.CStat.ShouldBe("204");
        result.Value.XmlAutorizado.ShouldBeNull();
        SefazRetornoParser.IsDuplicidade(result.Value.CStat).ShouldBeTrue();
        result.Value.XMotivo.ShouldBe("Duplicidade de NF-e");
    }

    [Fact]
    public void Parse_XmlMalformado_DeveRetornarFailureSemLancarExcecao()
    {
        var result = SefazRetornoParser.Parse("<xml_invalido_completamente");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sefaz.XmlInvalido");
    }

    [Fact]
    public void Parse_StringVazia_DeveRetornarFailure()
    {
        var result = SefazRetornoParser.Parse("");

        result.IsFailure.ShouldBeTrue();
    }
}
