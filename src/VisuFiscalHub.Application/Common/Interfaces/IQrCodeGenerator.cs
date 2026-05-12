using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IQrCodeGenerator
{
    Result<QrCode> Gerar(
        ChaveAcesso chaveAcesso,
        AmbienteSefaz ambiente,
        string csc,
        string cIdToken,
        string urlConsultaSefaz);
}
