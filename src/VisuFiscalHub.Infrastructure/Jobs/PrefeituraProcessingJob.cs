using Hangfire;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

/// <summary>
/// Job Hangfire que processa um documento NFS-e do estado Enfileirado até Autorizado/Rejeitado/Falhou.
/// Segue o mesmo padrão do FiscalDocumentProcessingJob: throw em falha transiente (Hangfire retenta).
/// </summary>
[AutomaticRetry(Attempts = 5, DelaysInSeconds = [30, 120, 600, 1800, 3600],
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class PrefeituraProcessingJob
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly IPrefeituraClient _prefeituraClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrefeituraProcessingJob> _logger;
    private readonly ApplicationDbContext _dbContext;

    public PrefeituraProcessingJob(
        IDocumentoFiscalRepository documentoRepo,
        IPrefeituraClient prefeituraClient,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<PrefeituraProcessingJob> logger,
        ApplicationDbContext dbContext)
    {
        _documentoRepo = documentoRepo;
        _prefeituraClient = prefeituraClient;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(DocumentoFiscalId documentoId, CancellationToken ct)
    {
        _logger.LogInformation("Iniciando processamento NFS-e {DocumentoId}", documentoId.Value);

        var documento = await _documentoRepo.GetByIdForUpdateAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("Documento NFS-e {DocumentoId} não encontrado", documentoId.Value);
            return;
        }

        using var tenantScope = LogContext.PushProperty("TenantId", documento.TenantId.Value);
        using var docScope    = LogContext.PushProperty("DocumentoId", documentoId.Value);

        if (documento.Status is StatusDocumento.Autorizado
            or StatusDocumento.Rejeitado
            or StatusDocumento.Falhou)
        {
            _logger.LogInformation("NFS-e {DocumentoId} já em status final {Status} — ignorado.",
                documentoId.Value, documento.Status);
            return;
        }

        if (documento.Tipo != TipoDocumento.NFSe)
        {
            _logger.LogError(
                "PrefeituraProcessingJob recebeu documento do tipo {Tipo} para {DocumentoId}. " +
                "Apenas NFSe é suportado.",
                documento.Tipo, documentoId.Value);
            documento.Falhar(_timeProvider);
            await _documentoRepo.UpdateAsync(documento, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return;
        }

        if (documento.Status == StatusDocumento.Enfileirado)
        {
            var iniciarResult = documento.IniciarProcessamento();
            if (iniciarResult.IsFailure)
            {
                _logger.LogWarning("NFS-e {DocumentoId} não pôde transicionar para Processando: {Error}",
                    documentoId.Value, iniciarResult.Error.Code);
                return;
            }

            await _documentoRepo.UpdateAsync(documento, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else
        {
            _logger.LogInformation("NFS-e {DocumentoId} retomado em status {Status}.",
                documentoId.Value, documento.Status);
        }

        var retornoResult = await _prefeituraClient.EnviarRpsAsync(documentoId, documento.TenantId, ct);

        if (retornoResult.IsFailure)
        {
            _logger.LogWarning("Falha transiente ao enviar RPS NFS-e {DocumentoId}: {Error}",
                documentoId.Value, retornoResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha na comunicação com a prefeitura para NFS-e {documentoId.Value}: {retornoResult.Error.Message}");
        }

        var retorno = retornoResult.Value;

        if (retorno.Autorizado && !string.IsNullOrWhiteSpace(retorno.NumeroNfse))
            await AutorizarAsync(documento, retorno, ct);
        else
            await RejeitarAsync(documento, retorno, ct);

        _logger.LogInformation("Processamento NFS-e concluído: {DocumentoId} → {Status}",
            documentoId.Value, documento.Status);
    }

    private async Task AutorizarAsync(
        DocumentoFiscal documento,
        PrefeituraRetorno retorno,
        CancellationToken ct)
    {
        var authorizedAt = _timeProvider.GetUtcNow();

        var authResult = documento.Autorizar(
            retorno.NumeroNfse!,
            retorno.XmlNfse ?? string.Empty,
            QrCode.NaoAplicavel(),
            authorizedAt,
            _timeProvider);

        if (authResult.IsFailure)
        {
            _logger.LogError("Falha ao autorizar NFS-e {DocumentoId}: {Error}",
                documento.Id.Value, authResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha ao autorizar NFS-e {documento.Id.Value}: {authResult.Error.Code}");
        }

        var attempt = DeliveryAttempt.Criar(
            documento.Id,
            TipoTentativa.Envio,
            _timeProvider.GetUtcNow(),
            success: true,
            responseCode: "100",
            responseMessage: null,
            elapsedMs: retorno.ElapsedMs);

        await _documentoRepo.UpdateAsync(documento, ct);
        _dbContext.DeliveryAttempts.Add(attempt);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("NFS-e {DocumentoId} AUTORIZADO. Número NFS-e: {NumeroNfse}",
            documento.Id.Value, retorno.NumeroNfse);
    }

    private async Task RejeitarAsync(
        DocumentoFiscal documento,
        PrefeituraRetorno retorno,
        CancellationToken ct)
    {
        var motivo = retorno.MotivoErro ?? "Prefeitura rejeitou a NFS-e sem motivo especificado.";
        var rejectResult = documento.Rejeitar(motivo, _timeProvider);

        if (rejectResult.IsFailure)
        {
            _logger.LogError("Falha ao rejeitar NFS-e {DocumentoId}: {Error}",
                documento.Id.Value, rejectResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha ao rejeitar NFS-e {documento.Id.Value}: {rejectResult.Error.Code}");
        }

        var attempt = DeliveryAttempt.Criar(
            documento.Id,
            TipoTentativa.Envio,
            _timeProvider.GetUtcNow(),
            success: false,
            responseCode: "E99",
            responseMessage: motivo,
            elapsedMs: retorno.ElapsedMs);

        await _documentoRepo.UpdateAsync(documento, ct);
        _dbContext.DeliveryAttempts.Add(attempt);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning("NFS-e {DocumentoId} REJEITADO. Motivo: {Motivo}",
            documento.Id.Value, motivo);
    }
}
