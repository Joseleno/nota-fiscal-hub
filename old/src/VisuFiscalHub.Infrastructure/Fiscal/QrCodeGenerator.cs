using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Fiscal;

internal sealed class QrCodeGenerator : IQrCodeGenerator
{
    public Result<QrCode> Gerar(
        ChaveAcesso chaveAcesso,
        AmbienteSefaz ambiente,
        string csc,
        string urlConsultaSefaz)
        => QrCode.Gerar(chaveAcesso, ambiente, csc, urlConsultaSefaz);
}
