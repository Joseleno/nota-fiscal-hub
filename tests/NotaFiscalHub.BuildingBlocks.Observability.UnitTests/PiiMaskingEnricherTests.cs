using NotaFiscalHub.BuildingBlocks.Observability;
using Serilog.Events;
using Serilog.Parsing;

namespace NotaFiscalHub.BuildingBlocks.Observability.UnitTests;

/// <summary>T1 (spec B7) — <see cref="PiiMaskingEnricher"/> mascara propriedades da denylist central (<see cref="PiiFields"/>).</summary>
public class PiiMaskingEnricherTests
{
    [Theory]
    [InlineData("cpf", "12345678900", "***")]
    [InlineData("nome", "Joao Silva", "***")]
    [InlineData("notaId", "abc-123", "abc-123")] // fora da denylist, intacto
    public void PiiMaskingEnricher_MascaraPropriedadeDaDenylist(string propriedade, string valor, string esperado)
    {
        var logEvent = CriarLogEventCom(propriedade, valor);
        var propertyFactory = new LogEventPropertyFactory();

        new PiiMaskingEnricher().Enrich(logEvent, propertyFactory);

        Assert.Equal(esperado, logEvent.Properties[propriedade].ToString().Trim('"'));
    }

    [Fact]
    public void PiiMaskingEnricher_ChaveAcesso_MascaraParcial()
    {
        var logEvent = CriarLogEventCom("chaveAcesso", "35090614200167140065125001000001800100000097");
        var propertyFactory = new LogEventPropertyFactory();

        new PiiMaskingEnricher().Enrich(logEvent, propertyFactory);

        Assert.Matches(@"^350906\.\.\.0097$", logEvent.Properties["chaveAcesso"].ToString().Trim('"'));
    }

    [Theory]
    [InlineData("CPF")]
    [InlineData("Cpf")]
    [InlineData("NOME")]
    public void PiiMaskingEnricher_DenylistECaseInsensitive(string propriedade)
    {
        var logEvent = CriarLogEventCom(propriedade, "valor-sensivel");
        var propertyFactory = new LogEventPropertyFactory();

        new PiiMaskingEnricher().Enrich(logEvent, propertyFactory);

        Assert.Equal("***", logEvent.Properties[propriedade].ToString().Trim('"'));
    }

    [Fact]
    public void PiiMaskingEnricher_PrefixoXml_EMascarado()
    {
        var logEvent = CriarLogEventCom("xmlAssinado", "<infNFe>...</infNFe>");
        var propertyFactory = new LogEventPropertyFactory();

        new PiiMaskingEnricher().Enrich(logEvent, propertyFactory);

        Assert.Equal("***", logEvent.Properties["xmlAssinado"].ToString().Trim('"'));
    }

    private static LogEvent CriarLogEventCom(string propriedade, string valor)
    {
        var template = new MessageTemplateParser().Parse($"Evento com {{{propriedade}}}");
        var properties = new List<LogEventProperty>
        {
            new(propriedade, new ScalarValue(valor)),
        };

        return new LogEvent(
            DateTimeOffset.UtcNow, LogEventLevel.Information, null, template, properties);
    }

    /// <summary>Fábrica mínima de <see cref="LogEventProperty"/> — suficiente para o enricher sob teste.</summary>
    private sealed class LogEventPropertyFactory : Serilog.Core.ILogEventPropertyFactory
    {
        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false) =>
            new(name, new ScalarValue(value));
    }
}
