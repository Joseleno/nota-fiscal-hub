using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class TributacaoCalculatorTests
{
    private readonly TributacaoCalculator _calculator = new();

    private static Produto ProdutoR100()
        => Produto.Criar("PROD001", "Produto Teste", "12345678", null, "5102", "UN",
            quantidade: 1m, valorUnitario: 100m, valorDesconto: 0m,
            OrigemMercadoria.Nacional).Value;

    [Fact]
    public void CalcularParaCrt1_DeveProduzirCsosn400_ValorIcmsZero()
    {
        var tributo = _calculator.CalcularParaCrt1(ProdutoR100()).Value;

        tributo.CsosnOuCst.ShouldBe(400);
        tributo.ValorIcms.ShouldBe(0m);
        tributo.BaseCalculoIcms.ShouldBe(0m);
        tributo.TipoIcms.ShouldBe(TipoIcms.CSOSN);
    }

    [Fact]
    public void CalcularParaCrt2_DeveProduzirCsosn900Com12Porcento()
    {
        var tributo = _calculator.CalcularParaCrt2(ProdutoR100(), aliquotaIcms: 12m, aliquotaPis: 0.65m, aliquotaCofins: 3m).Value;

        tributo.CsosnOuCst.ShouldBe(900);
        tributo.TipoIcms.ShouldBe(TipoIcms.CSOSN);
        tributo.BaseCalculoIcms.ShouldBe(100m);
        tributo.ValorIcms.ShouldBe(12m);
        tributo.ValorPis.ShouldBe(0.65m);
        tributo.ValorCofins.ShouldBe(3m);
    }

    [Fact]
    public void CalcularParaCrt2_QuandoDesconto_UsaValorLiquido()
    {
        var produto = Produto.Criar("P001", "Produto", "12345678", null, "5102", "UN",
            quantidade: 1m, valorUnitario: 100m, valorDesconto: 10m,
            OrigemMercadoria.Nacional).Value;

        var tributo = _calculator.CalcularParaCrt2(produto, aliquotaIcms: 10m, aliquotaPis: 0m, aliquotaCofins: 0m).Value;

        tributo.BaseCalculoIcms.ShouldBe(90m);
        tributo.ValorIcms.ShouldBe(9m);
    }

    [Fact]
    public void CalcularParaCrt3_DeveProduzirCstComBaseCalculo()
    {
        var tributo = _calculator.CalcularParaCrt3(ProdutoR100(), aliquotaIcms: 12m, aliquotaPis: 0.65m, aliquotaCofins: 3m).Value;

        tributo.TipoIcms.ShouldBe(TipoIcms.CST);
        tributo.BaseCalculoIcms.ShouldBe(100m);
        tributo.ValorIcms.ShouldBe(12m);
        tributo.ValorPis.ShouldBe(0.65m);
        tributo.ValorCofins.ShouldBe(3m);
    }

    [Fact]
    public void CalcularParaCrt3_QuandoAliquotaZero_ValoresZerados()
    {
        var tributo = _calculator.CalcularParaCrt3(ProdutoR100(), aliquotaIcms: 0m, aliquotaPis: 0m, aliquotaCofins: 0m).Value;

        tributo.ValorIcms.ShouldBe(0m);
        tributo.ValorPis.ShouldBe(0m);
        tributo.ValorCofins.ShouldBe(0m);
    }
}
