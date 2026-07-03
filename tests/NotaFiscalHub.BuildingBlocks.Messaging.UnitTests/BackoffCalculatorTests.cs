using NotaFiscalHub.BuildingBlocks.Messaging;

namespace NotaFiscalHub.BuildingBlocks.Messaging.UnitTests;

/// <summary>
/// Cálculo de backoff exponencial com jitter (spec B3 passo 6: base 5s, fator 2, teto 1h). O jitter é
/// estritamente aditivo (nunca reduz abaixo do valor exponencial "puro") — é isso que torna a asserção
/// <c>&gt;=</c> abaixo uma prova real, não uma tolerância frouxa.
/// </summary>
public class BackoffCalculatorTests
{
    [Theory]
    [InlineData(1, 5)]   // tentativa 1: base 5s
    [InlineData(2, 10)]  // tentativa 2: 5s * 2^1
    [InlineData(4, 40)]  // tentativa 4: 5s * 2^3
    public void CalcularBackoff_CresceExponencialmenteComTeto1Hora(int tentativa, int segundosMinimosEsperados)
    {
        var backoff = BackoffCalculator.Calcular(tentativa, seed: 42); // seed fixa para o teste (jitter determinístico)
        Assert.True(backoff.TotalSeconds >= segundosMinimosEsperados);
        Assert.True(backoff <= TimeSpan.FromHours(1));
    }

    [Fact]
    public void CalcularBackoff_TentativaMuitoAlta_RespeitaTeto1Hora()
    {
        var backoff = BackoffCalculator.Calcular(tentativa: 30, seed: 7);

        Assert.True(backoff <= TimeSpan.FromHours(1));
    }

    [Fact]
    public void CalcularBackoff_MesmaSeedMesmaTentativa_EhDeterministico()
    {
        var primeiro = BackoffCalculator.Calcular(3, seed: 99);
        var segundo = BackoffCalculator.Calcular(3, seed: 99);

        Assert.Equal(primeiro, segundo);
    }
}
