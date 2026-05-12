using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Tributo
{
    public TipoIcms TipoIcms { get; }
    public int CsosnOuCst { get; }
    public decimal AliquotaIcms { get; }
    public decimal BaseCalculoIcms { get; }
    public decimal ValorIcms { get; }
    public CstPisCofins CstPis { get; }
    public decimal BaseCalculoPis { get; }
    public decimal AliquotaPis { get; }
    public decimal ValorPis { get; }
    public CstPisCofins CstCofins { get; }
    public decimal BaseCalculoCofins { get; }
    public decimal AliquotaCofins { get; }
    public decimal ValorCofins { get; }

    private Tributo(
        TipoIcms tipoIcms,
        int csosnOuCst,
        decimal aliquotaIcms,
        decimal baseCalculoIcms,
        decimal valorIcms,
        CstPisCofins cstPis,
        decimal baseCalculoPis,
        decimal aliquotaPis,
        decimal valorPis,
        CstPisCofins cstCofins,
        decimal baseCalculoCofins,
        decimal aliquotaCofins,
        decimal valorCofins)
    {
        TipoIcms = tipoIcms;
        CsosnOuCst = csosnOuCst;
        AliquotaIcms = aliquotaIcms;
        BaseCalculoIcms = baseCalculoIcms;
        ValorIcms = valorIcms;
        CstPis = cstPis;
        BaseCalculoPis = baseCalculoPis;
        AliquotaPis = aliquotaPis;
        ValorPis = valorPis;
        CstCofins = cstCofins;
        BaseCalculoCofins = baseCalculoCofins;
        AliquotaCofins = aliquotaCofins;
        ValorCofins = valorCofins;
    }

    public static Result<Tributo> Criar(
        TipoIcms tipoIcms,
        int csosnOuCst,
        decimal aliquotaIcms,
        decimal baseCalculoIcms,
        decimal valorIcms,
        CstPisCofins cstPis,
        decimal baseCalculoPis,
        decimal aliquotaPis,
        decimal valorPis,
        CstPisCofins cstCofins,
        decimal baseCalculoCofins,
        decimal aliquotaCofins,
        decimal valorCofins)
    {
        if (aliquotaIcms < 0 || baseCalculoIcms < 0 || valorIcms < 0)
            return Result.Failure<Tributo>(DocumentoFiscalErrors.ProdutoInvalido);

        if (aliquotaPis < 0 || valorPis < 0 || aliquotaCofins < 0 || valorCofins < 0)
            return Result.Failure<Tributo>(DocumentoFiscalErrors.ProdutoInvalido);

        return Result.Success(new Tributo(
            tipoIcms, csosnOuCst,
            aliquotaIcms, baseCalculoIcms, valorIcms,
            cstPis, baseCalculoPis, aliquotaPis, valorPis,
            cstCofins, baseCalculoCofins, aliquotaCofins, valorCofins));
    }
}
