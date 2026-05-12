using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITributacaoCalculator
{
    Tributo CalcularParaCrt1(Produto produto, CSOSN csosn = CSOSN.Csosn400);
    Tributo CalcularParaCrt2(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins);
    Tributo CalcularParaCrt3(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins);
}
