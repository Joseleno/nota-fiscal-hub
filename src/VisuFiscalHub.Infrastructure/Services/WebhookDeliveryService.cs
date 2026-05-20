using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Services;

internal sealed class WebhookDeliveryService : IWebhookDeliveryService
{
    // Spec fase-06b: 3 tentativas totais (1 inicial + 2 retries) com backoff 30s / 5min.
    // RetryDelays[i] é o intervalo antes da tentativa i+2 (attemptNumber 1 → delay[0] → attempt 2, etc.).
    // O array tem MaxAttempts-1 elementos: um delay por retry agendado, nunca por tentativa final.
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30),   // antes da 2ª tentativa
        TimeSpan.FromMinutes(5),    // antes da 3ª tentativa (última)
    ];

    private const int MaxAttempts = 3;

    // Blocos de endereços privados/loopback — proteção SSRF.
    // Inclui IPv4-mapped-to-IPv6 (::ffff:10.x.x.x) via normalização em IsInBlockedRange.
    private static readonly IReadOnlyList<(IPAddress Network, int PrefixLength)> BlockedRanges =
    [
        (IPAddress.Parse("10.0.0.0"),      8),
        (IPAddress.Parse("172.16.0.0"),   12),
        (IPAddress.Parse("192.168.0.0"),  16),
        (IPAddress.Parse("127.0.0.0"),     8),
        (IPAddress.Parse("169.254.0.0"),  16),  // IPv4 link-local
        (IPAddress.Parse("::1"),          128),  // IPv6 loopback
        (IPAddress.Parse("fc00::"),         7),  // IPv6 ULA (fc00::/7 cobre fc00:: e fd00::)
        (IPAddress.Parse("fe80::"),        10),  // IPv6 link-local (equivalente ao 169.254.0.0/16)
        (IPAddress.Parse("ff00::"),         8),  // IPv6 multicast
        (IPAddress.Parse("2002::"),        16),  // IPv6 6to4 (encapsula todo o espaço IPv4 — bypass SSRF via túnel sit0)
    ];

    private readonly IClienteAppRepository _clienteAppRepo;
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBackgroundJobClient _jobClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        IClienteAppRepository clienteAppRepo,
        IDocumentoFiscalRepository documentoRepo,
        ICertificateEncryptionService encryptionService,
        IHttpClientFactory httpClientFactory,
        IBackgroundJobClient jobClient,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<WebhookDeliveryService> logger)
    {
        _clienteAppRepo = clienteAppRepo;
        _documentoRepo = documentoRepo;
        _encryptionService = encryptionService;
        _httpClientFactory = httpClientFactory;
        _jobClient = jobClient;
        _dbContext = dbContext;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // Ponto de entrada público — primeira tentativa (attemptNumber = 1).
    public Task DeliverAsync(
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
        CancellationToken ct)
        => DeliverInternalAsync(documentoId, clienteAppId, attemptNumber: 1, ct);

    // Ponto de entrada interno para retries agendados pelo Hangfire.
    // Separado da interface pública para encapsular o contador sem vazar para a Application layer.
    public Task RetryDeliverAsync(
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
        int attemptNumber,
        CancellationToken ct)
        => DeliverInternalAsync(documentoId, clienteAppId, attemptNumber, ct);

    private async Task DeliverInternalAsync(
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
        int attemptNumber,
        CancellationToken ct)
    {
        var clienteApp = await _clienteAppRepo.GetByIdAsync(clienteAppId, ct);
        if (clienteApp is null || string.IsNullOrWhiteSpace(clienteApp.WebhookUrl))
        {
            _logger.LogDebug("ClienteApp {ClienteAppId} sem webhook URL — entrega ignorada.", clienteAppId.Value);
            return;
        }

        if (clienteApp.WebhookSecretCriptografado is null)
        {
            _logger.LogWarning("ClienteApp {ClienteAppId} com webhook URL mas sem secret — entrega cancelada.",
                clienteAppId.Value);
            return;
        }

        var documento = await _documentoRepo.GetByIdAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("Documento {DocumentoId} não encontrado para entrega webhook.", documentoId.Value);
            return;
        }

        // Somente HTTPS — HTTP expõe assinatura HMAC em canal não criptografado.
        if (!Uri.TryCreate(clienteApp.WebhookUrl, UriKind.Absolute, out var webhookUri)
            || webhookUri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogError("URL de webhook inválida ou não-HTTPS para ClienteApp {ClienteAppId}: {Url}",
                clienteAppId.Value, clienteApp.WebhookUrl);
            return;
        }

        if (await IsBlockedAddressAsync(webhookUri.Host, ct))
        {
            _logger.LogError("URL de webhook bloqueada (SSRF) para ClienteApp {ClienteAppId}: {Url}",
                clienteAppId.Value, clienteApp.WebhookUrl);
            return;
        }

        var secretResult = _encryptionService.DecryptToString(clienteApp.WebhookSecretCriptografado);
        if (secretResult.IsFailure)
        {
            _logger.LogError("Falha ao decriptografar webhook secret do ClienteApp {ClienteAppId}: {Error}",
                clienteAppId.Value, secretResult.Error.Code);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            event_type = "documento.autorizado",
            documento_id = documentoId.Value,
            chave_acesso = documento.ChaveAcesso?.Valor,
            status = documento.Status.ToString(),
            occurred_at = _timeProvider.GetUtcNow()
        });

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ComputeHmacSha256(payloadBytes, secretResult.Value);

        var httpClient = _httpClientFactory.CreateClient("webhook");

        var sw = Stopwatch.StartNew();
        string? responseCode = null;
        string? responseMessage = null;
        bool success = false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhookUri);
            request.Content = new ByteArrayContent(payloadBytes);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            // Header padrão para assinatura HMAC-SHA256 de webhooks (GitHub/Stripe convention).
            request.Headers.Add("X-Hub-Signature-256", $"sha256={signature}");
            request.Headers.Add("X-Webhook-DocumentoId", documentoId.Value.ToString());

            using var response = await httpClient.SendAsync(request, ct);

            responseCode = ((int)response.StatusCode).ToString();
            success = response.IsSuccessStatusCode;

            if (success)
            {
                _logger.LogInformation(
                    "Webhook entregue para ClienteApp {ClienteAppId}, Documento {DocumentoId}. " +
                    "HTTP {Status} (tentativa {AttemptNumber}/{MaxAttempts})",
                    clienteAppId.Value, documentoId.Value, (int)response.StatusCode,
                    attemptNumber, MaxAttempts);
            }
            else
            {
                responseMessage = $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning(
                    "Webhook retornou {Status} para ClienteApp {ClienteAppId}, Documento {DocumentoId}. " +
                    "(tentativa {AttemptNumber}/{MaxAttempts})",
                    (int)response.StatusCode, clienteAppId.Value, documentoId.Value,
                    attemptNumber, MaxAttempts);

                EnqueueRetryIfApplicable(documentoId, clienteAppId, attemptNumber);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            responseMessage = ex.Message[..Math.Min(ex.Message.Length, 500)];
            _logger.LogWarning(ex,
                "Falha transiente na entrega do webhook para ClienteApp {ClienteAppId}, " +
                "Documento {DocumentoId}. (tentativa {AttemptNumber}/{MaxAttempts})",
                clienteAppId.Value, documentoId.Value, attemptNumber, MaxAttempts);

            EnqueueRetryIfApplicable(documentoId, clienteAppId, attemptNumber);
        }
        finally
        {
            sw.Stop();
            await RegistrarDeliveryAttemptAsync(
                documentoId, success, responseCode, responseMessage, sw.ElapsedMilliseconds, ct);
        }
    }

    private void EnqueueRetryIfApplicable(
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
        int attemptNumber)
    {
        if (attemptNumber >= MaxAttempts)
        {
            _logger.LogError(
                "Webhook para Documento {DocumentoId} esgotou {MaxAttempts} tentativas — entrega abandonada.",
                documentoId.Value, MaxAttempts);
            return;
        }

        // O índice no array é 0-based: attemptNumber=1 → RetryDelays[0]=30s, attemptNumber=2 → [1]=5min.
        var delay = RetryDelays[attemptNumber - 1];
        var nextAttempt = attemptNumber + 1;

        _jobClient.Schedule<WebhookDeliveryService>(
            svc => svc.RetryDeliverAsync(documentoId, clienteAppId, nextAttempt, CancellationToken.None),
            delay);

        _logger.LogInformation(
            "Retry de webhook para Documento {DocumentoId} agendado em {Delay} (tentativa {Next}/{Max}).",
            documentoId.Value, delay, nextAttempt, MaxAttempts);
    }

    private async Task RegistrarDeliveryAttemptAsync(
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
                TipoTentativa.Envio,
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
                "Falha ao registrar DeliveryAttempt para Documento {DocumentoId}", documentoId.Value);
        }
    }

    private static string ComputeHmacSha256(byte[] payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var mac = HMACSHA256.HashData(keyBytes, payload);
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    private static async Task<bool> IsBlockedAddressAsync(string host, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            return addresses.Any(IsInBlockedRange);
        }
        catch
        {
            // Falha de resolução DNS — bloquear por precaução.
            return true;
        }
    }

    private static bool IsInBlockedRange(IPAddress address)
    {
        // Normaliza IPv6-mapped-IPv4 (::ffff:10.x.x.x) para IPv4 antes de verificar blocos.
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var addressBytes = normalized.GetAddressBytes();

        foreach (var (network, prefixLength) in BlockedRanges)
        {
            var networkBytes = network.GetAddressBytes();
            if (addressBytes.Length != networkBytes.Length)
                continue;

            if (IsInSubnet(addressBytes, networkBytes, prefixLength))
                return true;
        }
        return false;
    }

    private static bool IsInSubnet(byte[] address, byte[] network, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (address[i] != network[i])
                return false;
        }

        if (remainingBits > 0 && fullBytes < address.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((address[fullBytes] & mask) != (network[fullBytes] & mask))
                return false;
        }

        return true;
    }
}
