using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Fiscal.Stubs;

internal sealed class FakeSefazClient : ISefazClient
{
    private readonly ILogger<FakeSefazClient> _logger;

    public FakeSefazClient(ILogger<FakeSefazClient> logger) => _logger = logger;

    public Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct)
    {
        _logger.LogWarning("FakeSefazClient ativo — resposta hardcoded para {DocumentoId}", documentoId.Value);
        return Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: true,
            CStat: "100",
            XMotivo: "Autorizado o uso da NF-e [FAKE]",
            NProt: "135260000000001",
            XmlAutorizado: "<protNFe/>",
            QrCodeUrl: "https://www.sefaz.rs.gov.br/NFCE/NFCE-consulta.aspx?p=fake",
            ElapsedMs: 0L)));
    }

    public Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso, TenantId tenantId, TipoDocumento tipo, CancellationToken ct)
    {
        _logger.LogWarning("FakeSefazClient ativo — consulta hardcoded para {ChaveAcesso}", chaveAcesso);
        return Task.FromResult(Result.Success(new SefazConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            CStat: "100",
            NProt: "135260000000001",
            XmlProtocolo: "<protNFe/>",
            ElapsedMs: 0L)));
    }
}
