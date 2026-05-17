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
    // Blocos de endereços privados/loopback — proteção SSRF.
    // Inclui IPv4-mapped-to-IPv6 (::ffff:10.x.x.x) via normalização em IsInBlockedRange.
    private static readonly IReadOnlyList<(IPAddress Network, int PrefixLength)> BlockedRanges =
    [
        (IPAddress.Parse("10.0.0.0"),      8),
        (IPAddress.Parse("172.16.0.0"),   12),
        (IPAddress.Parse("192.168.0.0"),  16),
        (IPAddress.Parse("127.0.0.0"),     8),
        (IPAddress.Parse("169.254.0.0"),  16),  // link-local
        (IPAddress.Parse("::1"),          128),  // IPv6 loopback
        (IPAddress.Parse("fc00::"),         7),  // IPv6 ULA
    ];

    private readonly IClienteAppRepository _clienteAppRepo;
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBackgroundJobClient _jobClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        IClienteAppRepository clienteAppRepo,
        IDocumentoFiscalRepository documentoRepo,
        ICertificateEncryptionService encryptionService,
        IHttpClientFactory httpClientFactory,
        IBackgroundJobClient jobClient,
        ApplicationDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<WebhookDeliveryService> logger)
    {
        _clienteAppRepo = clienteAppRepo;
        _documentoRepo = documentoRepo;
        _encryptionService = encryptionService;
        _httpClientFactory = httpClientFactory;
        _jobClient = jobClient;
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task DeliverAsync(
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
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
            _logger.LogWarning("ClienteApp {ClienteAppId} com webhook URL mas sem secret — entrega cancelada.", clienteAppId.Value);
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
                    "Webhook entregue para ClienteApp {ClienteAppId}, Documento {DocumentoId}. HTTP {Status}",
                    clienteAppId.Value, documentoId.Value, (int)response.StatusCode);
            }
            else
            {
                responseMessage = $"HTTP {(int)response.StatusCode}";
                _logger.LogWarning(
                    "Webhook retornou {Status} para ClienteApp {ClienteAppId}, Documento {DocumentoId}.",
                    (int)response.StatusCode, clienteAppId.Value, documentoId.Value);

                EnqueueRetry(documentoId, clienteAppId);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            responseMessage = ex.Message[..Math.Min(ex.Message.Length, 500)];
            _logger.LogWarning(ex,
                "Falha transiente na entrega do webhook para ClienteApp {ClienteAppId}, Documento {DocumentoId}.",
                clienteAppId.Value, documentoId.Value);

            EnqueueRetry(documentoId, clienteAppId);
        }
        finally
        {
            sw.Stop();
            await RegistrarDeliveryAttemptAsync(documentoId, success, responseCode, responseMessage, sw.ElapsedMilliseconds, ct);
        }
    }

    private void EnqueueRetry(DocumentoFiscalId documentoId, ClienteAppId clienteAppId)
    {
        _jobClient.Schedule<IWebhookDeliveryService>(
            svc => svc.DeliverAsync(documentoId, clienteAppId, CancellationToken.None),
            TimeSpan.FromSeconds(30));
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
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar DeliveryAttempt para Documento {DocumentoId}", documentoId.Value);
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
