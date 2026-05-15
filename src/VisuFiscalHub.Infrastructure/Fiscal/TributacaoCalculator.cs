using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Fiscal;

internal sealed class TributacaoCalculator : ITributacaoCalculator
{
    // CRT 1 — Simples Nacional. CSOSN 400: ICMS isento/não tributado.
    // PIS/COFINS: CST 07 (isento de contribuição) — sem valores numéricos.
    public Result<Tributo> CalcularParaCrt1(Produto produto, CSOSN csosn = CSOSN.Csosn400)
        => Tributo.Criar(
            TipoIcms.CSOSN,
            (int)csosn,
            aliquotaIcms: 0m,
            baseCalculoIcms: 0m,
            valorIcms: 0m,
            cstPis: CstPisCofins.Cst07,
            baseCalculoPis: 0m,
            aliquotaPis: 0m,
            valorPis: 0m,
            cstCofins: CstPisCofins.Cst07,
            baseCalculoCofins: 0m,
            aliquotaCofins: 0m,
            valorCofins: 0m);

    // CRT 2 — Simples Nacional com excesso de sublimite. CSOSN 900 com base e alíquota calculadas.
    // PIS/COFINS: CST 01 (tributado pela alíquota básica).
    public Result<Tributo> CalcularParaCrt2(
        Produto produto,
        decimal aliquotaIcms,
        decimal aliquotaPis,
        decimal aliquotaCofins)
    {
        var baseCalculo = produto.ValorLiquido;
        var valorIcms = Math.Round(baseCalculo * aliquotaIcms / 100m, 2);
        var valorPis = Math.Round(baseCalculo * aliquotaPis / 100m, 2);
        var valorCofins = Math.Round(baseCalculo * aliquotaCofins / 100m, 2);

        return Tributo.Criar(
            TipoIcms.CSOSN,
            (int)CSOSN.Csosn900,
            aliquotaIcms,
            baseCalculo,
            valorIcms,
            CstPisCofins.Cst01,
            baseCalculo,
            aliquotaPis,
            valorPis,
            CstPisCofins.Cst01,
            baseCalculo,
            aliquotaCofins,
            valorCofins);
    }

    // CRT 3 — Regime Normal. CST ICMS com base de cálculo e alíquota reais.
    // PIS/COFINS: CST 01 (tributado pela alíquota básica).
    public Result<Tributo> CalcularParaCrt3(
        Produto produto,
        decimal aliquotaIcms,
        decimal aliquotaPis,
        decimal aliquotaCofins)
    {
        var baseCalculo = produto.ValorLiquido;
        var valorIcms = Math.Round(baseCalculo * aliquotaIcms / 100m, 2);
        var valorPis = Math.Round(baseCalculo * aliquotaPis / 100m, 2);
        var valorCofins = Math.Round(baseCalculo * aliquotaCofins / 100m, 2);

        // CST 00: tributação plena. Handler de negócio escolhe o CST correto.
        return Tributo.Criar(
            TipoIcms.CST,
            (int)CstIcms.Cst00,
            aliquotaIcms,
            baseCalculo,
            valorIcms,
            CstPisCofins.Cst01,
            baseCalculo,
            aliquotaPis,
            valorPis,
            CstPisCofins.Cst01,
            baseCalculo,
            aliquotaCofins,
            valorCofins);
    }
}
