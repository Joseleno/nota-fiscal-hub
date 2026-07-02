using Shouldly;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class CnpjTests
{
    [Fact]
    public void Criar_QuandoCnpjValido_DeveRetornarSucesso()
    {
        var result = Cnpj.Criar("11222333000181");
        result.IsSuccess.ShouldBeTrue();
        result.Value.Valor.ShouldBe("11222333000181");
    }

    [Theory]
    [InlineData("11.222.333/0001-81")]
    [InlineData("11222333000181")]
    public void Criar_QuandoCnpjComOuSemMascara_DeveProduirMesmoValor(string cnpjInput)
    {
        var result = Cnpj.Criar(cnpjInput);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Valor.ShouldBe("11222333000181");
    }

    [Fact]
    public void Criar_QuandoDigitosVerificadoresErrados_DeveRetornarErro()
    {
        var result = Cnpj.Criar("11222333000182");
        result.IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00000000000000")]
    [InlineData("11111111111111")]
    [InlineData("99999999999999")]
    public void Criar_QuandoTodosDigitosIguais_DeveRetornarErro(string cnpjRepetido)
    {
        var result = Cnpj.Criar(cnpjRepetido);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_QuandoCnpjVazio_DeveRetornarErro()
    {
        var result = Cnpj.Criar("");
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_QuandoCnpjComMenosDe14Digitos_DeveRetornarErro()
    {
        var result = Cnpj.Criar("1122233300018");
        result.IsFailure.ShouldBeTrue();
    }
}
