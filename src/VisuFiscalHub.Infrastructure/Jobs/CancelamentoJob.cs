using System.Diagnostics;
using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300],
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
internal sealed class CancelamentoJob
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantCertificateProvider _certProvider;
    private readonly XmlSigner _signer;
    private readonly SefazHttpClient _httpClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CancelamentoJob> _logger;

    public CancelamentoJob(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        ITenantCertificateProvider certProvider,
        XmlSigner signer,
        SefazHttpClient httpClient,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<CancelamentoJob> logger)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo    = tenantRepo;
        _certProvider  = certProvider;
        _signer        = signer;
        _httpClient    = httpClient;
        _dbContext     = dbContext;
        _unitOfWork    = unitOfWork;
        _timeProvider  = timeProvider;
        _logger        = logger;
    }

    public async Task ExecuteAsync(DocumentoFiscalId documentoId, string justificativa, CancellationToken ct)
    {
        var documento = await _documentoRepo.GetByIdAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("CancelamentoJob: Documento {DocumentoId} não encontrado.", documentoId.Value);
            return;
        }

        if (documento.Status != StatusDocumento.Cancelando)
        {
            _logger.LogInformation(
                "CancelamentoJob: Documento {DocumentoId} em status {Status} — idempotência, ignorado.",
                documentoId.Value, documento.Status);
            return;
        }

        if (documento.Protocolo is null)
        {
            _logger.LogError(
                "CancelamentoJob: Documento {DocumentoId} sem Protocolo — pré-condição violada, abortando sem retry.",
                documentoId.Value);
            return;
        }

        var tenant = await _tenantRepo.GetByIdAsync(documento.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new InvalidOperationException(
                $"Tenant {documento.TenantId.Value} não encontrado ou inativo — Hangfire fará retry.");

        var certResult = await _certProvider.GetCertificateAsync(documento.TenantId, ct);
        if (certResult.IsFailure)
            throw new InvalidOperationException(
                $"Certificado indisponível para Tenant {documento.TenantId.Value}: {certResult.Error.Code}");

        using var certificate = certResult.Value;

        var utcNow   = _timeProvider.GetUtcNow();
        var idLote   = utcNow.ToString("yyyyMMddHHmmss") + (utcNow.Millisecond / 100).ToString();
        var ufCodigo = tenant.ConfiguracaoFiscal.UfCodigo;
        var ambiente = tenant.ConfiguracaoFiscal.Ambiente;

        var xmlEvento = CancelamentoEventoBuilder.ConstruirEvento(
            documento, tenant, justificativa, documento.Protocolo, utcNow, idLote);

        var xmlAssinado = _signer.Assinar(
            xmlEvento, certificate, $"#ID110111{documento.ChaveAcesso.Valor}01");

        var envelope = SoapEnvelopeBuilder.BuildEvento(xmlAssinado.OuterXml, ufCodigo);
        var url      = SefazEndpointResolver.ResolveEvento(ufCodigo, ambiente);

        var sw = Stopwatch.StartNew();
        bool success = false;
        string? responseCode = null;
        string? responseMessage = null;

        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, envelope, certificate, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "CancelamentoJob: Falha HTTP ao cancelar {DocumentoId}. Hangfire fará retry.",
                documentoId.Value);
            await RegistrarAttemptAsync(documentoId, false, null, ex.Message[..Math.Min(ex.Message.Length, 500)],
                sw.ElapsedMilliseconds, ct);
            throw;
        }

        var parseResult = CancelamentoRetornoParser.Parse(soapResponse);
        if (parseResult.IsFailure)
        {
            sw.Stop();
            _logger.LogError(
                "CancelamentoJob: Falha ao parsear resposta SEFAZ para {DocumentoId}: {Error}. Hangfire fará retry.",
                documentoId.Value, parseResult.Error.Code);
            await RegistrarAttemptAsync(documentoId, false, null, parseResult.Error.Code,
                sw.ElapsedMilliseconds, ct);
            throw new InvalidOperationException($"Parse falhou: {parseResult.Error.Code}");
        }

        var retorno = parseResult.Value;
        responseCode = retorno.CStat;

        if (retorno.Aceito)
        {
            var canceladoAt = retorno.DhRegEvento ?? utcNow;
            documento.ConfirmarCancelamento(canceladoAt);
            await _unitOfWork.SaveChangesAsync(ct);
            success = true;
            _logger.LogInformation(
                "CancelamentoJob: Documento {DocumentoId} cancelado (cStat={CStat}).",
                documentoId.Value, retorno.CStat);
        }
        else
        {
            documento.RejeitarCancelamento(retorno.XMotivo);
            await _unitOfWork.SaveChangesAsync(ct);
            responseMessage = retorno.XMotivo;
            _logger.LogWarning(
                "CancelamentoJob: SEFAZ rejeitou cancelamento de {DocumentoId} (cStat={CStat}): {XMotivo}.",
                documentoId.Value, retorno.CStat, retorno.XMotivo);
        }

        sw.Stop();
        await RegistrarAttemptAsync(documentoId, success, responseCode, responseMessage,
            sw.ElapsedMilliseconds, ct);
    }

    private async Task RegistrarAttemptAsync(
        DocumentoFiscalId documentoId,
        bool success,
        string? responseCode,
        string? responseMessage,
        long elapsedMs,
        CancellationToken ct)
    {
        try
        {
            var attempt = DeliveryAttempt.Criar(
                documentoId,
                TipoTentativa.Cancelamento,
                _timeProvider.GetUtcNow(),
                success,
                responseCode,
                responseMessage,
                elapsedMs);

            await _dbContext.DeliveryAttempts.AddAsync(attempt, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CancelamentoJob: Falha ao registrar DeliveryAttempt para {DocumentoId}", documentoId.Value);
        }
    }
}
