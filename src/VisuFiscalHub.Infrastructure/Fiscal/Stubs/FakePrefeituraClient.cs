using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Fiscal.Stubs;

internal sealed class FakePrefeituraClient : IPrefeituraClient
{
    private readonly ILogger<FakePrefeituraClient> _logger;

    public FakePrefeituraClient(ILogger<FakePrefeituraClient> logger) => _logger = logger;

    public Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct)
    {
        _logger.LogWarning("FakePrefeituraClient ativo — resposta hardcoded para {DocumentoId}", documentoId.Value);
        return Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: true,
            NumeroNfse: "1",
            Protocolo: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));
    }

    public Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps, string serieRps, TenantId tenantId, int codigoMunicipio, CancellationToken ct)
    {
        _logger.LogWarning("FakePrefeituraClient ativo — consulta hardcoded para RPS {NumeroRps}", numeroRps);
        return Task.FromResult(Result.Success(new PrefeituraConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            NumeroNfse: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));
    }
}
