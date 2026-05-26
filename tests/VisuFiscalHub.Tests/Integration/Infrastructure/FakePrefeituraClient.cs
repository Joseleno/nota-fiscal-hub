using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

/// <summary>
/// Stub configurável para IPrefeituraClient no ambiente de testes de integração.
/// Comportamento padrão: retorna Autorizado com número NFS-e "1".
/// </summary>
public sealed class FakePrefeituraClient : IPrefeituraClient
{
    private Func<DocumentoFiscalId, TenantId, Task<Result<PrefeituraRetorno>>> _enviarHandler =
        (_, _) => Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: true,
            NumeroNfse: "1",
            Protocolo: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));

    private readonly Func<string, string, TenantId, int, Task<Result<PrefeituraConsultaRetorno>>> _consultaHandler =
        (_, _, _, _) => Task.FromResult(Result.Success(new PrefeituraConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            NumeroNfse: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));

    public void SimularAutorizado(string numeroNfse = "1") =>
        _enviarHandler = (_, _) => Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: true,
            NumeroNfse: numeroNfse,
            Protocolo: numeroNfse,
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));

    public void SimularRejeitado(string motivo = "Erro simulado prefeitura") =>
        _enviarHandler = (_, _) => Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: false,
            NumeroNfse: null,
            Protocolo: null,
            XmlNfse: null,
            MotivoErro: motivo,
            ElapsedMs: 0L)));

    public void SimularFalhaHttp() =>
        _enviarHandler = (_, _) => Task.FromResult(Result.Failure<PrefeituraRetorno>(
            new Error("Prefeitura.HttpFalhou", "Falha de comunicação simulada.")));

    public Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct) =>
        _enviarHandler(documentoId, tenantId);

    public Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps, string serieRps, TenantId tenantId, int codigoMunicipio, CancellationToken ct) =>
        _consultaHandler(numeroRps, serieRps, tenantId, codigoMunicipio);
}
