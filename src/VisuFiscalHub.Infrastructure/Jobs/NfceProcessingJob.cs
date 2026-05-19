using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Infrastructure.Jobs;

/// <summary>
/// Job Hangfire que processa uma NFC-e do estado Enfileirado (ou Processando pós-crash) até
/// Autorizado / Rejeitado / Denegado. Falhas transitórias mantêm status Processando para que
/// o retry do Hangfire possa reiniciar — Falhar() só é chamado pelo ReconciliacaoJobProcessor
/// que detecta o documento travado após todos os retries esgotados.
/// </summary>
[AutomaticRetry(Attempts = 5, DelaysInSeconds = [30, 120, 600, 1800, 3600],
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class NfceProcessingJob
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ISefazClient _sefazClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NfceProcessingJob> _logger;

    public NfceProcessingJob(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        ISefazClient sefazClient,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<NfceProcessingJob> logger)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo = tenantRepo;
        _sefazClient = sefazClient;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(DocumentoFiscalId documentoId, CancellationToken ct)
    {
        _logger.LogInformation("Iniciando processamento NFC-e {DocumentoId}", documentoId.Value);

        var documento = await _documentoRepo.GetByIdForUpdateAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("Documento {DocumentoId} não encontrado no processamento", documentoId.Value);
            return;
        }

        // Idempotência: status finais são terminais — não processar novamente.
        if (documento.Status is StatusDocumento.Autorizado
            or StatusDocumento.Rejeitado
            or StatusDocumento.Denegado
            or StatusDocumento.Cancelado
            or StatusDocumento.Falhou)
        {
            _logger.LogInformation("Documento {DocumentoId} já em status final {Status} — ignorado.",
                documentoId.Value, documento.Status);
            return;
        }

        // Documento pode estar em Processando se o worker crashou após IniciarProcessamento.
        // Neste caso, pular a transição e seguir direto para o envio SEFAZ (idempotente).
        if (documento.Status == StatusDocumento.Enfileirado)
        {
            var iniciarResult = documento.IniciarProcessamento();
            if (iniciarResult.IsFailure)
            {
                _logger.LogWarning("Documento {DocumentoId} não pôde transicionar para Processando: {Error}",
                    documentoId.Value, iniciarResult.Error.Code);
                return;
            }

            await _documentoRepo.UpdateAsync(documento, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else
        {
            _logger.LogInformation("Documento {DocumentoId} retomado em status {Status} — pulando IniciarProcessamento.",
                documentoId.Value, documento.Status);
        }

        var retornoResult = await _sefazClient.SubmeterAutorizacaoAsync(documentoId, documento.TenantId, ct);

        if (retornoResult.IsFailure)
        {
            // Erro de infraestrutura (HTTP, timeout, cert) — NÃO transicionar para Falhou.
            // O documento permanece em Processando; Hangfire retenta conforme AutomaticRetry.
            // ReconciliacaoJobProcessor detecta o documento travado após todos os retries.
            _logger.LogWarning("Falha transiente ao submeter {DocumentoId}: {Error}",
                documentoId.Value, retornoResult.Error.Code);

            throw new InvalidOperationException(
                $"Falha na comunicação com SEFAZ para {documentoId.Value}: {retornoResult.Error.Message}");
        }

        var retorno = retornoResult.Value;

        if (retorno.Autorizado)
        {
            await AutorizarAsync(documento, retorno, ct);
        }
        else if (SefazRetornoParser.IsDuplicidade(retorno.CStat))
        {
            // cStat=204/572: documento já existe e está autorizado na SEFAZ.
            // Buscar protocolo via consulta para autorizar localmente.
            await AutorizarPorDuplicidadeAsync(documento, ct);
        }
        else if (SefazRetornoParser.IsDenegado(retorno.CStat))
        {
            await DenegarAsync(documento, retorno, ct);
        }
        else if (!SefazRetornoParser.IsRecuperavel(retorno.CStat))
        {
            // Rejeição fiscal definitiva (4xx/5xx) — não reenviar.
            await RejeitarAsync(documento, retorno, motivo: null, ct);
        }
        else
        {
            // Rejeição recuperável (1xx exceto 110) — manter Processando e retentar via Hangfire.
            _logger.LogWarning("Rejeição recuperável cStat={CStat} para {DocumentoId}: {XMotivo}",
                retorno.CStat, documentoId.Value, retorno.XMotivo);

            throw new InvalidOperationException(
                $"Rejeição recuperável SEFAZ cStat={retorno.CStat} para {documentoId.Value}: {retorno.XMotivo}");
        }

        _logger.LogInformation("Processamento concluído: documento {DocumentoId} → {Status}",
            documentoId.Value, documento.Status);
    }

    private async Task AutorizarAsync(
        Domain.Entities.DocumentoFiscal documento,
        SefazRetorno retorno,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(retorno.NProt))
        {
            _logger.LogError("SEFAZ retornou Autorizado mas NProt está vazio para {DocumentoId}", documento.Id.Value);
            throw new InvalidOperationException($"NProt ausente na autorização de {documento.Id.Value}.");
        }

        var qrCode = QrCode.FromStorage(documento.ChaveAcesso.Valor);
        var authorizedAt = _timeProvider.GetUtcNow();

        var authResult = documento.Autorizar(
            retorno.NProt,
            retorno.XmlAutorizado ?? string.Empty,
            qrCode,
            authorizedAt,
            _timeProvider);

        if (authResult.IsFailure)
        {
            _logger.LogError("Falha ao autorizar documento {DocumentoId}: {Error}",
                documento.Id.Value, authResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha ao autorizar {documento.Id.Value}: {authResult.Error.Code}");
        }

        await _documentoRepo.UpdateAsync(documento, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("NFC-e {DocumentoId} AUTORIZADA. Protocolo: {Protocolo}",
            documento.Id.Value, retorno.NProt);
    }

    private async Task AutorizarPorDuplicidadeAsync(
        Domain.Entities.DocumentoFiscal documento,
        CancellationToken ct)
    {
        var consultaResult = await _sefazClient.ConsultarNfeAsync(
            documento.ChaveAcesso.Valor, documento.TenantId, ct);

        if (consultaResult.IsFailure)
        {
            _logger.LogWarning(
                "Duplicidade detectada mas consulta SEFAZ falhou para {DocumentoId}: {Error} — retentando.",
                documento.Id.Value, consultaResult.Error.Code);
            // Falha transiente — lançar para que o Hangfire retente.
            throw new InvalidOperationException(
                $"Duplicidade: consulta SEFAZ falhou para {documento.Id.Value}: {consultaResult.Error.Message}");
        }

        var consulta = consultaResult.Value;

        if (consulta.Autorizado && !string.IsNullOrWhiteSpace(consulta.NProt))
        {
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
                _logger.LogError("Falha ao autorizar duplicidade {DocumentoId}: {Error}",
                    documento.Id.Value, authResult.Error.Code);
                throw new InvalidOperationException(
                    $"Falha ao autorizar duplicidade {documento.Id.Value}: {authResult.Error.Code}");
            }

            await _documentoRepo.UpdateAsync(documento, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation("NFC-e {DocumentoId} AUTORIZADA via duplicidade. Protocolo: {NProt}",
                documento.Id.Value, consulta.NProt);
        }
        else
        {
            // SEFAZ reportou duplicidade mas consulta não confirmou autorização — situação anômala.
            _logger.LogError(
                "Duplicidade sem autorização confirmada na consulta para {DocumentoId}: autorizado={Autorizado} NProt={NProt}",
                documento.Id.Value, consulta.Autorizado, consulta.NProt);
            // Manter em Processando — ReconciliacaoJobProcessor resolverá após threshold.
            throw new InvalidOperationException(
                $"Duplicidade sem autorização confirmada na consulta para {documento.Id.Value}.");
        }
    }

    private async Task RejeitarAsync(
        Domain.Entities.DocumentoFiscal documento,
        SefazRetorno retorno,
        string? motivo,
        CancellationToken ct)
    {
        var motivoFinal = motivo ?? $"[{retorno.CStat}] {retorno.XMotivo}";
        var rejectResult = documento.Rejeitar(motivoFinal, _timeProvider);

        if (rejectResult.IsFailure)
        {
            _logger.LogError("Falha ao rejeitar documento {DocumentoId}: {Error}",
                documento.Id.Value, rejectResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha ao rejeitar {documento.Id.Value}: {rejectResult.Error.Code}");
        }

        await _documentoRepo.UpdateAsync(documento, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning("NFC-e {DocumentoId} REJEITADA. cStat={CStat} xMotivo={XMotivo}",
            documento.Id.Value, retorno.CStat, retorno.XMotivo);
    }

    private async Task DenegarAsync(
        Domain.Entities.DocumentoFiscal documento,
        SefazRetorno retorno,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(documento.TenantId, ct);
        var cnpjEmitente = tenant?.Cnpj.Valor ?? "CNPJ desconhecido";
        var motivo = $"[{retorno.CStat}] {retorno.XMotivo}";

        var denyResult = documento.Denegar(motivo, cnpjEmitente, _timeProvider);

        if (denyResult.IsFailure)
        {
            _logger.LogError("Falha ao denegar documento {DocumentoId}: {Error}",
                documento.Id.Value, denyResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha ao denegar {documento.Id.Value}: {denyResult.Error.Code}");
        }

        await _documentoRepo.UpdateAsync(documento, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogCritical("NFC-e {DocumentoId} DENEGADA para CNPJ {Cnpj}. cStat={CStat} xMotivo={XMotivo}",
            documento.Id.Value, cnpjEmitente, retorno.CStat, retorno.XMotivo);
    }
}
