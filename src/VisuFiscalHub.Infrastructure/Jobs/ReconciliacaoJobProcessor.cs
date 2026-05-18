using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Infrastructure.Jobs;

/// <summary>
/// Job periódico (a cada 5 min) que reconcilia documentos travados em status Processando.
/// Para cada documento travado há mais de 10 min:
///   1. Consulta SEFAZ (ConsultarNfeAsync) para saber o estado real do documento.
///   2. Se SEFAZ confirma autorizado → Autorizar e publicar evento.
///   3. Se SEFAZ não encontrou ou rejeição definitiva → Falhar e publicar evento.
///   4. Se consulta SEFAZ falhou (timeout/rede) → reenfileira NfceProcessingJob para retry.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 120)]
[AutomaticRetry(Attempts = 0)]
public sealed class ReconciliacaoJobProcessor
{
    private static readonly TimeSpan ThresholdTravado = TimeSpan.FromMinutes(10);

    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ISefazClient _sefazClient;
    private readonly IDocumentJobQueue _documentJobQueue;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReconciliacaoJobProcessor> _logger;

    public ReconciliacaoJobProcessor(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        ISefazClient sefazClient,
        IDocumentJobQueue documentJobQueue,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<ReconciliacaoJobProcessor> logger)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo = tenantRepo;
        _sefazClient = sefazClient;
        _documentJobQueue = documentJobQueue;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var threshold = _timeProvider.GetUtcNow() - ThresholdTravado;
        var travados = await _documentoRepo.GetProcessandoAntigoAsync(threshold, ct);

        if (travados.Count == 0)
            return;

        _logger.LogInformation("Reconciliação: {Count} documento(s) travado(s) em Processando há mais de {Min} min.",
            travados.Count, ThresholdTravado.TotalMinutes);

        foreach (var documento in travados)
        {
            await ReconciliarAsync(documento, ct);
        }
    }

    private async Task ReconciliarAsync(Domain.Entities.DocumentoFiscal documento, CancellationToken ct)
    {
        _logger.LogWarning("Reconciliando {DocumentoId} (status={Status}, createdAt={CreatedAt})",
            documento.Id.Value, documento.Status, documento.CreatedAt);

        var consultaResult = await _sefazClient.ConsultarNfeAsync(
            documento.ChaveAcesso.Valor, documento.TenantId, ct);

        if (consultaResult.IsFailure)
        {
            // Consulta também falhou (rede/timeout) — reenfileirar para retry do NfceProcessingJob.
            _logger.LogWarning("Consulta SEFAZ falhou para {DocumentoId}: {Error} — reenfileirando.",
                documento.Id.Value, consultaResult.Error.Code);
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

        var qrCode = QrCode.FromStorage(documento.ChaveAcesso.Valor);
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

        await _documentoRepo.UpdateAsync(documento, ct);
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
            _logger.LogError("Falha ao transicionar {DocumentoId} para Falhou: {Error}",
                documento.Id.Value, failResult.Error.Code);
            return;
        }

        await _documentoRepo.UpdateAsync(documento, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogError("Reconciliação: {DocumentoId} marcado como Falhou. Motivo: {Motivo}",
            documento.Id.Value, motivo);
    }
}
