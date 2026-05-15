using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITributacaoCalculator
{
    Result<Tributo> CalcularParaCrt1(Produto produto, CSOSN csosn = CSOSN.Csosn400);
    Result<Tributo> CalcularParaCrt2(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins);
    Result<Tributo> CalcularParaCrt3(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins);
}
