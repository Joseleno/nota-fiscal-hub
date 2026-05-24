using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class CancelamentoRetornoParserTests
{
    private const string SoapAceito135 = """
        <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
          <soap12:Body>
            <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4">
              <retEnvEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
                <retEvento>
                  <infEvento>
                    <cStat>135</cStat>
                    <xMotivo>Evento registrado e vinculado a NF-e</xMotivo>
                    <nProt>135260000000099</nProt>
                    <dhRegEvento>2026-05-24T10:00:00-03:00</dhRegEvento>
                  </infEvento>
                </retEvento>
              </retEnvEvento>
            </nfeResultMsg>
          </soap12:Body>
        </soap12:Envelope>
        """;

    private const string SoapAceito155 = """
        <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
          <soap12:Body>
            <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4">
              <retEnvEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
                <retEvento>
                  <infEvento>
                    <cStat>155</cStat>
                    <xMotivo>Cancelamento homologado fora de prazo</xMotivo>
                    <nProt>155260000000042</nProt>
                    <dhRegEvento>2026-05-24T10:30:00-03:00</dhRegEvento>
                  </infEvento>
                </retEvento>
              </retEnvEvento>
            </nfeResultMsg>
          </soap12:Body>
        </soap12:Envelope>
        """;

    private const string SoapRejeicao218 = """
        <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
          <soap12:Body>
            <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4">
              <retEnvEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
                <retEvento>
                  <infEvento>
                    <cStat>218</cStat>
                    <xMotivo>Rejeição: Prazo de Cancelamento Superior ao Prazo Limite</xMotivo>
                  </infEvento>
                </retEvento>
              </retEnvEvento>
            </nfeResultMsg>
          </soap12:Body>
        </soap12:Envelope>
        """;

    [Fact]
    public void Parse_cStat135_DeveRetornarAceito()
    {
        var result = CancelamentoRetornoParser.Parse(SoapAceito135);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Aceito.ShouldBeTrue();
        result.Value.CStat.ShouldBe("135");
        result.Value.NProtCancelamento.ShouldBe("135260000000099");
        result.Value.DhRegEvento.ShouldNotBeNull();
        result.Value.DhRegEvento!.Value.Offset.ShouldBe(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void Parse_cStat155_DeveRetornarAceito()
    {
        // cStat=155: "Cancelamento homologado fora de prazo" — SEFAZ aceita mesmo
        // após o prazo SEFAZ (distinto do prazo de 30 min da regra de negócio local).
        var result = CancelamentoRetornoParser.Parse(SoapAceito155);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Aceito.ShouldBeTrue();
        result.Value.CStat.ShouldBe("155");
    }

    [Fact]
    public void Parse_cStatRejeicao_DeveRetornarNaoAceito()
    {
        var result = CancelamentoRetornoParser.Parse(SoapRejeicao218);
        result.IsSuccess.ShouldBeTrue(); // Parse ok, mas cancelamento não aceito
        result.Value.Aceito.ShouldBeFalse();
        result.Value.CStat.ShouldBe("218");
        result.Value.XMotivo.ShouldContain("Rejeição");
    }

    [Fact]
    public void Parse_XmlMalformado_DeveRetornarFalhaSemExcecao()
    {
        var result = CancelamentoRetornoParser.Parse("<broken xml");
        result.IsFailure.ShouldBeTrue();
    }
}
