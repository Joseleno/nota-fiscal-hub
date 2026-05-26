using Hangfire;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

/// <summary>
/// Job periódico (a cada 5 min) que reconcilia documentos travados em status Processando.
/// Para cada documento travado há mais de 10 min:
///   1. Consulta SEFAZ (ConsultarNfeAsync) para saber o estado real do documento.
///   2. Se SEFAZ confirma autorizado → Autorizar e publicar evento.
///   3. Se SEFAZ não encontrou ou rejeição definitiva → Falhar e publicar evento.
///   4. Se consulta SEFAZ falhou (timeout/rede) → reenfileira FiscalDocumentProcessingJob para retry.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 120)]
[AutomaticRetry(Attempts = 0)]
public sealed class ReconciliacaoJobProcessor
{
    private static readonly TimeSpan ThresholdTravado = TimeSpan.FromMinutes(10);

    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ISefazClient _sefazClient;
    private readonly IDocumentJobQueue _documentJobQueue;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReconciliacaoJobProcessor> _logger;
    private readonly ApplicationDbContext _dbContext;

    public ReconciliacaoJobProcessor(
        IDocumentoFiscalRepository documentoRepo,
        ISefazClient sefazClient,
        IDocumentJobQueue documentJobQueue,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ReconciliacaoJobProcessor> logger,
        ApplicationDbContext dbContext)
    {
        _documentoRepo = documentoRepo;
        _sefazClient = sefazClient;
        _documentJobQueue = documentJobQueue;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var threshold = _timeProvider.GetUtcNow() - ThresholdTravado;
        var travados = await _documentoRepo.GetProcessandoAntigoAsync(threshold, ct);

        if (travados.Count == 0)
            return;

        _logger.LogInformation("Reconciliação: {Count} documento(s) travado(s) em Processando/Enfileirado há mais de {Min} min.",
            travados.Count, ThresholdTravado.TotalMinutes);

        foreach (var documento in travados)
        {
            await ReconciliarAsync(documento, ct);
        }
    }

    private async Task ReconciliarAsync(Domain.Entities.DocumentoFiscal documento, CancellationToken ct)
    {
        using var tenantScope = LogContext.PushProperty("TenantId", documento.TenantId.Value);
        using var docScope    = LogContext.PushProperty("DocumentoId", documento.Id.Value);

        _logger.LogWarning("Reconciliando {DocumentoId} (status={Status}, createdAt={CreatedAt})",
            documento.Id.Value, documento.Status, documento.CreatedAt);

        // Documento nunca enviado à SEFAZ — reenfileirar em vez de consultar.
        if (documento.Status == StatusDocumento.Enfileirado)
        {
            _logger.LogInformation("Documento {DocumentoId} ainda Enfileirado — reenfileirando para processamento.",
                documento.Id.Value);
            await _documentJobQueue.EnqueueProcessingAsync(documento.Id, ct);
            return;
        }

        var consultaResult = await _sefazClient.ConsultarNfeAsync(
            documento.ChaveAcesso!.Valor, documento.TenantId, documento.Tipo, ct);

        if (consultaResult.IsFailure)
        {
            // Consulta também falhou (rede/timeout) — reenfileirar para retry do FiscalDocumentProcessingJob.
            _logger.LogWarning("Consulta SEFAZ falhou para {DocumentoId}: {Error} — reenfileirando.",
                documento.Id.Value, consultaResult.Error.Code);

            var attemptFalha = DeliveryAttempt.Criar(
                documento.Id,
                TipoTentativa.Consulta,
                _timeProvider.GetUtcNow(),
                success: false,
                responseCode: null,
                responseMessage: consultaResult.Error.Code,
                elapsedMs: 0L);
            _dbContext.DeliveryAttempts.Add(attemptFalha);
            await _unitOfWork.SaveChangesAsync(ct);

            await _documentJobQueue.EnqueueProcessingAsync(documento.Id, ct);
            return;
        }

        var consulta = consultaResult.Value;

        if (consulta.Autorizado)
        {
            await AutorizarPorConsultaAsync(documento, consulta, ct);
        }
        else if (!consulta.Encontrado)
        {
            await FalharAsync(documento, "Documento não encontrado na consulta SEFAZ após timeout.", ct);
        }
        else
        {
            // Encontrado mas não autorizado — rejeição definitiva na SEFAZ.
            await FalharAsync(documento, $"Rejeição SEFAZ na consulta: cStat={consulta.CStat}", ct);
        }
    }

    private async Task AutorizarPorConsultaAsync(
        Domain.Entities.DocumentoFiscal documento,
        SefazConsultaRetorno consulta,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(consulta.NProt))
        {
            _logger.LogError("Consulta retornou Autorizado mas NProt está vazio para {DocumentoId}. Falhar.",
                documento.Id.Value);
            await FalharAsync(documento, "NProt ausente na consulta de reconciliação.", ct);
            return;
        }

        if (documento.Tipo == TipoDocumento.NfCe && documento.QrCode is null)
            _logger.LogWarning(
                "QrCode não persistido para {DocumentoId} — usando chave de acesso como fallback (URL incompleta).",
                documento.Id.Value);

        var qrCode = documento.Tipo == TipoDocumento.NfCe
            ? (documento.QrCode ?? QrCode.FromStorage(documento.ChaveAcesso!.Valor))
            : QrCode.NaoAplicavel();
        var authorizedAt = _timeProvider.GetUtcNow();

        var authResult = documento.Autorizar(
            consulta.NProt,
            consulta.XmlProtocolo ?? string.Empty,
            qrCode,
            authorizedAt,
            _timeProvider);

        if (authResult.IsFailure)
        {
            _logger.LogError("Falha ao autorizar {DocumentoId} na reconciliação: {Error}",
                documento.Id.Value, authResult.Error.Code);
            return;
        }

        var attempt = DeliveryAttempt.Criar(
            documento.Id,
            TipoTentativa.Consulta,
            _timeProvider.GetUtcNow(),
            success: true,
            responseCode: consulta.CStat,
            responseMessage: null,
            elapsedMs: consulta.ElapsedMs);

        await _documentoRepo.UpdateAsync(documento, ct);
        _dbContext.DeliveryAttempts.Add(attempt);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("Reconciliação: {DocumentoId} AUTORIZADO via consulta SEFAZ. Protocolo: {NProt}",
            documento.Id.Value, consulta.NProt);
    }

    private async Task FalharAsync(
        Domain.Entities.DocumentoFiscal documento,
        string motivo,
        CancellationToken ct)
    {
        var failResult = documento.Falhar(_timeProvider);

        if (failResult.IsFailure)
        {
            // Documento pode ter sido transitado para status final pelo FiscalDocumentProcessingJob
            // concorrentemente entre GetProcessandoAntigoAsync e esta chamada — não é um erro.
            _logger.LogInformation(
                "Documento {DocumentoId} não pôde ser marcado como Falhou durante reconciliação " +
                "(provavelmente já transitado por job concorrente): {Error}",
                documento.Id.Value, failResult.Error.Code);
            return;
        }

        var attempt = DeliveryAttempt.Criar(
            documento.Id,
            TipoTentativa.Consulta,
            _timeProvider.GetUtcNow(),
            success: false,
            responseCode: null,
            responseMessage: motivo,
            elapsedMs: 0L);

        await _documentoRepo.UpdateAsync(documento, ct);
        _dbContext.DeliveryAttempts.Add(attempt);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogError("Reconciliação: {DocumentoId} marcado como Falhou. Motivo: {Motivo}",
            documento.Id.Value, motivo);
    }
}
