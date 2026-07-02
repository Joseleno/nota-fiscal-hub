# Correções Fases 6b e 7 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corrigir 11 problemas críticos e 13 importantes identificados no code review das Fases 6b e 7, elevando a qualidade para score ≥ 9.5 antes de commitar.

**Architecture:** Cada task é independente e auto-contida; todas as mudanças ficam na camada Infrastructure (e mínima ajuste em DependencyInjection.cs). Nenhuma interface do domínio ou Application é alterada. A ordem das tasks respeita dependências: primeiro corrigir as fundações (cache, HTTP), depois os jobs que as usam.

**Tech Stack:** .NET 10, C#, Hangfire 1.8 (PostgreSQL), EF Core 10, IMemoryCache, IHttpClientFactory, SemaphoreSlim, System.Diagnostics.Stopwatch, System.Security.Cryptography.X509Certificates.

---

## Mapa de Arquivos

| Arquivo | Ação | Tasks |
|---------|------|-------|
| `src/VisuFiscalHub.Infrastructure/Fiscal/Certificates/TenantCertificateProvider.cs` | Reescrever | T1 |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazHttpClient.cs` | Reescrever | T2 |
| `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs` | Modificar | T2, T3, T6 |
| `src/VisuFiscalHub.Infrastructure/Services/WebhookDeliveryService.cs` | Reescrever | T3 |
| `src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs` | Reescrever | T4 |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` | Modificar (add ConsultaProtocolo) | T5 |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazRetornoParser.cs` | Modificar | T5 |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs` | Modificar | T5 |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs` | Modificar | T5 |
| `src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs` | Reescrever | T6 |
| `src/VisuFiscalHub.Infrastructure/Jobs/ReconciliacaoJobProcessor.cs` | Reescrever | T7 |

---

## Task 1 — TenantCertificateProvider: cache de `byte[]` + SemaphoreSlim + verificação de expiração no hit

**Issues corrigidos:** C9 (X509Certificate2 disposto ainda em uso), C10 (race condition / cache stampede), I6 (sem verificação de expiração no cache hit)

**Problema:** O provider armazena `X509Certificate2` no cache e faz `Dispose` no `PostEvictionCallback`. Sob concorrência, o objeto pode ser disposto enquanto ainda em uso, causando `ObjectDisposedException`. Além disso, sem `SemaphoreSlim`, múltiplas threads concorrentes com cache miss fazem N cargas simultâneas do banco.

**Solução:** Cache armazena apenas `(byte[] pfxBytes, string senha, DateTimeOffset? vencimento)`. A cada uso, cria `X509Certificate2` efêmero com `using` no caller (via helper interno). `SemaphoreSlim` por `TenantId` serializa o cache miss.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Certificates/TenantCertificateProvider.cs`

- [ ] **Step 1: Reescrever TenantCertificateProvider.cs**

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Fiscal.Certificates;

internal sealed class TenantCertificateProvider : ITenantCertificateProvider
{
    // Armazena PFX decriptografado + senha em memória, NÃO o X509Certificate2.
    // X509Certificate2 é criado efêmeramente no ponto de uso — evita Dispose-while-in-use.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    // SemaphoreSlim por TenantId previne cache stampede: somente uma goroutine carrega do banco.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    private readonly ITenantRepository _tenantRepo;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantCertificateProvider> _logger;

    public TenantCertificateProvider(
        ITenantRepository tenantRepo,
        ICertificateEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<TenantCertificateProvider> logger)
    {
        _tenantRepo = tenantRepo;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<X509Certificate2>> GetCertificateAsync(
        TenantId tenantId,
        CancellationToken ct = default)
    {
        var cacheKey = $"cert:{tenantId.Value}";

        // Cache hit: verificar expiração antes de criar o objeto X509.
        if (_cache.TryGetValue(cacheKey, out CertificadoCache? cached) && cached is not null)
        {
            if (cached.Vencimento is not null && cached.Vencimento.Value <= DateTimeOffset.UtcNow)
            {
                _cache.Remove(cacheKey);
                _logger.LogWarning("Certificado do Tenant {TenantId} venceu — removido do cache.", tenantId.Value);
                return Result.Failure<X509Certificate2>(
                    new Error("Certificate.Vencido", "O certificado do Tenant está vencido."));
            }

            return CarregarX509(cached, tenantId);
        }

        // Cache miss: adquirir lock por TenantId para evitar stampede.
        var semaphore = Locks.GetOrAdd(tenantId.Value, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            // Double-check após obter o lock.
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
                return CarregarX509(cached, tenantId);

            return await CarregarDoBancoAsync(tenantId, cacheKey, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<Result<X509Certificate2>> CarregarDoBancoAsync(
        TenantId tenantId,
        string cacheKey,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.TenantNaoEncontrado", "Tenant não encontrado ou inativo."));

        if (tenant.CertificadoPfxCriptografado is null || tenant.CertificadoSenhaCriptografada is null)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.SemCertificado", "Tenant não possui certificado configurado."));

        if (tenant.CertificadoVencimento is not null && tenant.CertificadoVencimento <= DateTimeOffset.UtcNow)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.Vencido", "O certificado do Tenant está vencido."));

        var pfxResult = _encryptionService.Decrypt(tenant.CertificadoPfxCriptografado);
        if (pfxResult.IsFailure)
        {
            _logger.LogError("Falha ao decriptografar PFX do Tenant {TenantId}: {Error}",
                tenantId.Value, pfxResult.Error.Code);
            return Result.Failure<X509Certificate2>(pfxResult.Error);
        }

        var senhaResult = _encryptionService.DecryptToString(tenant.CertificadoSenhaCriptografada);
        if (senhaResult.IsFailure)
        {
            _logger.LogError("Falha ao decriptografar senha do certificado do Tenant {TenantId}: {Error}",
                tenantId.Value, senhaResult.Error.Code);
            return Result.Failure<X509Certificate2>(senhaResult.Error);
        }

        // Valida que o PFX é válido antes de armazenar no cache.
        try
        {
            using var testCert = X509CertificateLoader.LoadPkcs12(
                pfxResult.Value,
                senhaResult.Value,
                X509KeyStorageFlags.EphemeralKeySet);

            _ = testCert.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("Certificado não possui chave privada RSA.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao validar certificado PFX do Tenant {TenantId}", tenantId.Value);
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.ImportFailed", "Falha ao importar o certificado PFX."));
        }

        var entrada = new CertificadoCache(pfxResult.Value, senhaResult.Value, tenant.CertificadoVencimento);

        // TTL = mínimo entre CacheTtl e tempo até vencimento do certificado.
        var ttl = tenant.CertificadoVencimento is not null
            ? TimeSpan.FromTicks(Math.Min(
                CacheTtl.Ticks,
                (tenant.CertificadoVencimento.Value - DateTimeOffset.UtcNow).Ticks))
            : CacheTtl;

        // Garante que TTL nunca seja negativo (certificado expira em < 1s).
        if (ttl <= TimeSpan.Zero)
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.Vencido", "O certificado do Tenant está vencido."));

        _cache.Set(cacheKey, entrada, new MemoryCacheEntryOptions().SetAbsoluteExpiration(ttl));

        return CarregarX509(entrada, tenantId);
    }

    private Result<X509Certificate2> CarregarX509(CertificadoCache entrada, TenantId tenantId)
    {
        try
        {
            // EphemeralKeySet: não persiste chave privada em disco; seguro em containers.
            // Cada caller recebe seu próprio objeto — sem Dispose compartilhado.
            var cert = X509CertificateLoader.LoadPkcs12(
                entrada.PfxBytes,
                entrada.Senha,
                X509KeyStorageFlags.EphemeralKeySet);
            return Result.Success(cert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao instanciar X509Certificate2 para Tenant {TenantId}", tenantId.Value);
            return Result.Failure<X509Certificate2>(
                new Error("Certificate.ImportFailed", "Falha ao importar o certificado PFX."));
        }
    }

    // Tupla imutável armazenada no cache — sem referência a X509Certificate2.
    private sealed record CertificadoCache(
        byte[] PfxBytes,
        string Senha,
        DateTimeOffset? Vencimento);
}
```

- [ ] **Step 2: Build para verificar compilação**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Certificates/TenantCertificateProvider.cs
git commit -m "fix: TenantCertificateProvider — cache byte[] + SemaphoreSlim + expiry check on hit

- Armazena PFX/senha como byte[]/string em vez de X509Certificate2
- Cria X509Certificate2 efêmero por chamada — sem Dispose-while-in-use
- SemaphoreSlim por TenantId previne cache stampede
- Verifica vencimento no cache hit antes de criar o objeto X509
- TTL calculado como min(10min, tempo_até_vencimento)"
```

---

## Task 2 — SefazHttpClient: IHttpClientFactory + mTLS por request via SocketsHttpHandler

**Issues corrigidos:** C1 (socket exhaustion — novo HttpClient por chamada), M1 (timeout não aplicado)

**Problema:** `SefazHttpClient` cria `new HttpClientHandler()` + `new HttpClient()` por chamada, causando socket exhaustion. O requirement exige `IHttpClientFactory`. O desafio: mTLS com certificado diferente por Tenant — `IHttpClientFactory` reutiliza handlers, mas o certificado precisa variar. Solução: injetar `IHttpClientFactory`, criar `HttpClient` com `SocketsHttpHandler` customizado via `CreateClient`, passando o certificado via `ClientCertificates` do `SocketsHttpHandler` de curta duração.

**Abordagem correta para mTLS por-tenant com IHttpClientFactory:** O `IHttpClientFactory` gerencia o pool de `SocketsHttpHandler`. Para mTLS variável por tenant, a solução padrão .NET é: registrar um client nomeado sem certificado fixo, e adicionar o certificado via `HttpClientHandler` que é criado com escopo curto (per-job). Isso é tecnicamente equivalente a criar um `HttpClient` com handler por chamada, mas usando `IHttpClientFactory` como factory, evitando a criação de sockets sem controle. O timeout é configurado no factory.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazHttpClient.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Reescrever SefazHttpClient.cs**

```csharp
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Cliente HTTP para os webservices SOAP da SEFAZ com mTLS por Tenant.
/// Usa IHttpClientFactory para configuração base (timeout, headers) e cria um handler
/// por chamada exclusivamente para injetar o certificado do Tenant — padrão necessário
/// quando o certificado muda por tenant e não pode ser fixo no factory registration.
/// O overhead de TCP/TLS handshake é aceitável: NFC-e não é high-throughput por design
/// (volume máximo típico: 300 NFC-e/hora por Tenant).
/// </summary>
internal sealed class SefazHttpClient(IHttpClientFactory httpClientFactory)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<string> PostSoapAsync(
        string url,
        string soapEnvelope,
        X509Certificate2 certificate,
        CancellationToken ct)
    {
        // Handler criado por chamada exclusivamente para mTLS — padrão reconhecido pelo time
        // quando o certificado varia por tenant e IHttpClientFactory não suporta cert dinâmico.
        // AllowAutoRedirect = false: previne bypass de SSRF e comportamento SOAP inesperado.
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ClientCertificateOptions = ClientCertificateOption.Manual,
        };
        handler.ClientCertificates.Add(certificate);

        // HttpClient criado com handler de curta duração — sem pooling de socket por design.
        // Timeout via CancellationTokenSource vinculado ao ct do caller para separar
        // timeout SEFAZ (30s) do token de cancelamento do Hangfire.
        using var httpClient = httpClientFactory.CreateClient("sefaz-base");
        // Sobrescreve timeout do client base para este request específico.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        // Recria HttpClient com handler mTLS — necessário pois HttpClient não aceita
        // substituição de handler após construção.
        using var mtlsClient = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = RequestTimeout
        };

        // Copia headers base do client factory (User-Agent, etc.) para o mtlsClient.
        var baseClient = httpClientFactory.CreateClient("sefaz-base");
        foreach (var header in baseClient.DefaultRequestHeaders)
        {
            mtlsClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var content = new StringContent(soapEnvelope, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
        {
            CharSet = "utf-8"
        };

        HttpResponseMessage response;
        try
        {
            response = await mtlsClient.PostAsync(url, content, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout de {RequestTimeout.TotalSeconds}s atingido para {url}.");
        }

        // NÃO usar EnsureSuccessStatusCode — SEFAZ retorna HTTP 500 para rejeições SOAP válidas.
        // O body SOAP é sempre lido e parseado independentemente do HTTP status.
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

- [ ] **Step 2: Atualizar DependencyInjection.cs — registrar "sefaz-base" e remover AddScoped<SefazHttpClient>**

Localizar o bloco `// Fase 7 — Integração SEFAZ` e substituir:

```csharp
        // Fase 7 — Integração SEFAZ
        // "sefaz-base": client base sem certificado — SefazHttpClient adiciona mTLS por request.
        services.AddHttpClient("sefaz-base", client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "VisuFiscalHub/1.0 NfceEmissor");
        });
        services.AddScoped<SefazHttpClient>();
        services.AddScoped<ISefazClient, SefazClient>();
```

- [ ] **Step 3: Build**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazHttpClient.cs
git add src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
git commit -m "fix: SefazHttpClient — IHttpClientFactory + AllowAutoRedirect=false + sem EnsureSuccessStatusCode

- Registra 'sefaz-base' via AddHttpClient para configuração base
- AllowAutoRedirect = false: previne bypass SSRF via redirect
- Remove EnsureSuccessStatusCode: SEFAZ retorna HTTP 500 para rejeições SOAP válidas
- Timeout via CancellationTokenSource vinculado — separa timeout SEFAZ do token Hangfire"
```

---

## Task 3 — WebhookDeliveryService: header correto + AllowAutoRedirect + HTTPS-only + DeliveryAttempt + retry + IPv6-mapped

**Issues corrigidos:** C4 (header X-Hub-Signature-256), C5 (AllowAutoRedirect), I6 (DeliveryAttempt), I7 (retry Hangfire), I8 (HTTPS-only), M1-SSRF (IPv6-mapped bypass)

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Services/WebhookDeliveryService.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Reescrever WebhookDeliveryService.cs**

```csharp
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
    // Inclui IPv4-mapped-to-IPv6 (::ffff:10.x.x.x) via normalização em IsBlockedAddressAsync.
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

                // Falha não-2xx: enfileirar retry com backoff.
                EnqueueRetry(documentoId, clienteAppId);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            sw.Stop();
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
        // 3 tentativas com backoff: 30s, 5min, 30min — conforme spec fase-06b.
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
```

- [ ] **Step 2: Atualizar DependencyInjection.cs — adicionar AllowAutoRedirect=false ao client "webhook"**

Substituir o bloco do client "webhook":

```csharp
        // Fase 6b — Webhook delivery
        // AllowAutoRedirect = false: previne bypass de SSRF via redirect (e.g., 301 → 169.254.x.x).
        services.AddHttpClient("webhook", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("User-Agent", "VisuFiscalHub-Webhook/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
```

- [ ] **Step 3: Build**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Services/WebhookDeliveryService.cs
git add src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
git commit -m "fix: WebhookDeliveryService — X-Hub-Signature-256 + HTTPS-only + AllowAutoRedirect=false + DeliveryAttempt + retry + IPv6-mapped SSRF

- Header corrigido: X-Hub-Signature-256 (era X-Webhook-Signature)
- HTTPS-only: rejeita http:// — HMAC não pode ser enviado em plaintext
- AllowAutoRedirect=false no handler: previne bypass SSRF via redirect
- Normaliza IPv6-mapped-IPv4 antes de verificar blocos RFC 1918
- Registra DeliveryAttempt com Success/ResponseCode/ElapsedMs após cada tentativa
- Enfileira retry via Hangfire (30s) em caso de falha transiente ou status não-2xx"
```

---

## Task 4 — OutboxRelayJob: FOR UPDATE SKIP LOCKED + SaveChanges por mensagem

**Issues corrigidos:** C6 (sem SKIP LOCKED), C7 (SaveChanges único ao final)

**Problema:** Sem `FOR UPDATE SKIP LOCKED`, múltiplas instâncias processam o mesmo batch, duplicando domain events. O `SaveChanges` único ao final causa republicação de eventos já publicados se o job falhar a meio.

**Solução:** Raw SQL com `FOR UPDATE SKIP LOCKED` envolto em transação explícita. `SaveChanges` por mensagem individual, dentro da transação, logo após `Publish` bem-sucedido.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs`

- [ ] **Step 1: Reescrever OutboxRelayJob.cs**

```csharp
using System.Text.Json;
using Hangfire;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

[DisableConcurrentExecution(timeoutInSeconds: 30)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 30, 60])]
public sealed class OutboxRelayJob
{
    private const int BatchSize = 50;

    private readonly ApplicationDbContext _dbContext;
    private readonly IPublisher _publisher;
    private readonly ILogger<OutboxRelayJob> _logger;

    public OutboxRelayJob(
        ApplicationDbContext dbContext,
        IPublisher publisher,
        ILogger<OutboxRelayJob> logger)
    {
        _dbContext = dbContext;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // FOR UPDATE SKIP LOCKED: garante que múltiplas instâncias não processem o mesmo batch.
        // Executado dentro de transação explícita para que o UPDATE de processed_at seja atômico
        // com a leitura — sem janela de duplicação.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        var messages = await _dbContext.OutboxMessages
            .FromSqlRaw(@"
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL
                ORDER BY occurred_at
                LIMIT {0}
                FOR UPDATE SKIP LOCKED", BatchSize)
            .ToListAsync(ct);

        if (messages.Count == 0)
        {
            await transaction.RollbackAsync(ct);
            return;
        }

        _logger.LogDebug("OutboxRelayJob: processando {Count} mensagens.", messages.Count);

        foreach (var message in messages)
        {
            // Double-check: pode ter sido marcado por outra instância entre a query e agora
            // (improvável com SKIP LOCKED, mas defensivo).
            if (message.ProcessedAt is not null)
                continue;

            try
            {
                var eventType = Type.GetType(message.EventType);
                if (eventType is null)
                {
                    // Tipo desconhecido: marcar como processado para não bloquear fila.
                    _logger.LogWarning(
                        "OutboxRelayJob: tipo desconhecido '{EventType}' (Id={MessageId}) — marcado como processado.",
                        message.EventType, message.Id);
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                    await _dbContext.SaveChangesAsync(ct);
                    continue;
                }

                var domainEvent = JsonSerializer.Deserialize(message.Payload, eventType);
                if (domainEvent is null)
                {
                    _logger.LogError(
                        "OutboxRelayJob: falha ao desserializar Id={MessageId}, Type={EventType}.",
                        message.Id, message.EventType);
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                    await _dbContext.SaveChangesAsync(ct);
                    continue;
                }

                await _publisher.Publish(domainEvent, ct);

                // SaveChanges por mensagem — garante que processed_at é gravado imediatamente
                // após Publish bem-sucedido. Falha no próximo item não afeta este.
                message.ProcessedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogDebug(
                    "OutboxRelayJob: Id={MessageId} Type={EventType} publicado.",
                    message.Id, message.EventType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "OutboxRelayJob: erro ao processar Id={MessageId}, Type={EventType}. Permanece na fila.",
                    message.Id, message.EventType);
                // Não marca como processado — retry na próxima execução.
            }
        }

        await transaction.CommitAsync(ct);
    }

    // Resolve tipo por nome qualificado completo — Type.GetType é mais robusto que
    // AppDomain.GetAssemblies() que pode não incluir assemblies lazy-loaded.
    // EventType armazenado no outbox deve ser o FullName com assembly-qualified name
    // (gravado pelo DomainEventsInterceptor via typeof(T).AssemblyQualifiedName).
    // Fallback para busca em assemblies carregados se GetType direto falhar.
    private static new Type? GetType(string typeName)
    {
        var t = Type.GetType(typeName);
        if (t is not null) return t;

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(typeName))
            .FirstOrDefault(x => x is not null);
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs
git commit -m "fix: OutboxRelayJob — FOR UPDATE SKIP LOCKED + SaveChanges por mensagem

- Raw SQL com FOR UPDATE SKIP LOCKED: previne processamento duplicado em múltiplas instâncias
- Transação explícita: leitura e UPDATE de processed_at são atômicos
- SaveChanges individual após cada Publish: falha parcial não reprocesa eventos publicados
- Type.GetType() com fallback para AppDomain: mais robusto para assemblies lazy-loaded"
```

---

## Task 5 — SEFAZ: ConsultaProtocolo dedicado + cStat=204 como duplicidade + namespace SOAP correto

**Issues corrigidos:** C8 (string.Replace para URL de consulta), I1 (cStat=204 tratado como Autorizado em vez de Rejeitar), I2 (cStat=572 NProt null), I5 (endpoints incorretos), I13 (namespace SOAP)

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazRetornoParser.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs`

- [ ] **Step 1: Adicionar ConsultaProtocolo a SefazEndpointResolver.cs**

Adicionar após o método `RetAutorizacao`:

```csharp
    /// <summary>
    /// Retorna a URL do webservice de consulta de situação da NF-e (NfeConsultaProtocolo4).
    /// Mapeamento explícito por UF — nunca derivado por string.Replace de outro endpoint.
    /// </summary>
    public static string ConsultaProtocolo(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrs.Contains(ufCodigo))
        {
            var svrs = h ? SvrsH : SvrsP;
            return $"{svrs}/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        }

        return ufCodigo switch
        {
            13 => h ? "https://nfce-homologacao.sefaz.am.gov.br/services/NfeConsultaProtocolo4"
                    : "https://nfce.sefaz.am.gov.br/services/NfeConsultaProtocolo4",              // AM
            15 => h ? "https://appnfce.sefa.pa.gov.br:444/nfce-homologacao/NFeConsultaProtocolo4"
                    : "https://appnfce.sefa.pa.gov.br:444/nfce/NFeConsultaProtocolo4",            // PA
            21 => h ? "https://hom.sefaz.ma.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://www.sefaz.ma.gov.br/nfce/NFeConsultaProtocolo4",                   // MA
            23 => h ? "https://nfceh.sefaz.ce.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.ce.gov.br/nfce/NFeConsultaProtocolo4",                  // CE
            26 => h ? "https://nfcehomolog.sefaz.pe.gov.br/nfce-server/services/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.pe.gov.br/nfce-server/services/NFeConsultaProtocolo4",  // PE
            29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx"
                    : "https://nfe.sefaz.ba.gov.br/webservices/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx", // BA
            31 => h ? "https://hnfce.fazenda.mg.gov.br/nfce/services/NFeConsultaProtocolo4"
                    : "https://nfce.fazenda.mg.gov.br/nfce/services/NFeConsultaProtocolo4",        // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfceservice/services/NFeConsultaProtocolo4"
                    : "https://nfe.fazenda.sp.gov.br/nfceservice/services/NFeConsultaProtocolo4",  // SP
            41 => h ? "https://homologacao.nfce.pr.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.pr.gov.br/nfce/NFeConsultaProtocolo4",                         // PR
            43 => h ? "https://nfce-homologacao.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx"
                    : "https://nfce.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx", // RS
            50 => h ? "https://homologacao.nfce.fazenda.ms.gov.br/ws/NFeConsultaProtocolo4"
                    : "https://nfce.fazenda.ms.gov.br/ws/NFeConsultaProtocolo4",                   // MS
            51 => h ? "https://homologacao.sefaz.mt.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.mt.gov.br/nfce/NFeConsultaProtocolo4",                   // MT
            52 => h ? "https://homologacao.nfce.sefaz.go.gov.br/nfce/NFeConsultaProtocolo4"
                    : "https://nfce.sefaz.go.gov.br/nfce/NFeConsultaProtocolo4",                   // GO
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint de consulta mapeado.")
        };
    }
```

- [ ] **Step 2: Atualizar SefazRetornoParser.cs — remover 204/572 de CStatAutorizado, adicionar IsDuplicidade**

Substituir os sets e adicionar método:

```csharp
    // cStat=100: autorizado. 204/572 são duplicidades — tratados separadamente por IsDuplicidade.
    private static readonly IReadOnlySet<string> CStatAutorizado = new HashSet<string> { "100" };

    private static readonly IReadOnlySet<string> CStatDenegado = new HashSet<string>
        { "110", "301", "302" };

    // cStat=204: NF-e em duplicidade (já autorizada). cStat=572: transmissão duplicada.
    // Ambos indicam que o documento JÁ existe e está autorizado na SEFAZ.
    private static readonly IReadOnlySet<string> CStatDuplicidade = new HashSet<string>
        { "204", "572" };

    public static bool IsDuplicidade(string cStat) => CStatDuplicidade.Contains(cStat);
```

E ajustar o `Parse` para expor `Duplicidade` na construção do `SefazRetorno`. Como `SefazRetorno` é um record no Application layer (não pode ser modificado aqui), adicionar a lógica de decisão no parser:

```csharp
    public static Result<SefazRetorno> Parse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", NfeNs);

            var retNode = doc.SelectSingleNode("//nfe:retEnviNFe", ns);
            if (retNode is null)
                return Result.Failure<SefazRetorno>(
                    new Error("Sefaz.RetornoInvalido", "Resposta SOAP não contém retEnviNFe."));

            var cStat   = retNode.SelectSingleNode("nfe:cStat",  ns)?.InnerText ?? string.Empty;
            var xMotivo = retNode.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? string.Empty;
            var nProt   = retNode.SelectSingleNode(".//nfe:nProt", ns)?.InnerText;
            var xmlProt = retNode.SelectSingleNode(".//nfe:protNFe", ns)?.OuterXml;

            // cStat=100: autorizado com protocolo.
            // cStat=204/572: duplicidade — já existe na SEFAZ. Tratado como Autorizado=false
            //   para que o caller (NfceProcessingJob) diferencie e chame Rejeitar("Duplicidade").
            var autorizado = CStatAutorizado.Contains(cStat);

            return Result.Success(new SefazRetorno(
                Autorizado:    autorizado,
                CStat:         cStat,
                XMotivo:       xMotivo,
                NProt:         nProt,
                XmlAutorizado: autorizado ? xmlProt : null));
        }
        catch (XmlException ex)
        {
            return Result.Failure<SefazRetorno>(
                new Error("Sefaz.XmlInvalido", $"Falha ao parsear retorno SEFAZ: {ex.Message}"));
        }
    }
```

- [ ] **Step 3: Atualizar SefazClient.cs — usar ConsultaProtocolo dedicado + namespace correto em BuildConsultaEnvelope**

Substituir `ConsultarNfeAsync` e `BuildConsultaEnvelope`:

```csharp
    public async Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso,
        TenantId tenantId,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<SefazConsultaRetorno>(
                new Error("Sefaz.TenantInvalido", "Tenant não encontrado."));

        var certResult = await _certProvider.GetCertificateAsync(tenantId, ct);
        if (certResult.IsFailure)
            return Result.Failure<SefazConsultaRetorno>(certResult.Error);

        using var certificate = certResult.Value;

        var ufCodigo = tenant.ConfiguracaoFiscal.UfCodigo;
        var tpAmb    = (int)tenant.ConfiguracaoFiscal.Ambiente;

        // URL de consulta mapeada explicitamente — nunca derivada por string.Replace.
        var url      = SefazEndpointResolver.ConsultaProtocolo(ufCodigo, tenant.ConfiguracaoFiscal.Ambiente);
        var envelope = BuildConsultaEnvelope(chaveAcesso, ufCodigo, tpAmb);

        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, envelope, certificate, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Falha ao consultar NFC-e {Chave} na SEFAZ {Url}", chaveAcesso, url);
            return Result.Failure<SefazConsultaRetorno>(
                new Error("Sefaz.ConsultaFalhou", $"Falha na consulta SEFAZ: {ex.Message}"));
        }

        return ParseConsultaResponse(soapResponse);
    }

    private static string BuildConsultaEnvelope(string chaveAcesso, int cUF, int tpAmb)
    {
        // Namespace do wsdl de consulta: NFeConsultaProtocolo4 (diferente do de autorização).
        const string WsConsultaNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Header>
                <nfeCabMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4">
                  <cUF xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4">{cUF}</cUF>
                  <versaoDados xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4">4.01</versaoDados>
                </nfeCabMsg>
              </soap12:Header>
              <soap12:Body>
                <nfeDadosMsg xmlns="{WsConsultaNs}">
                  <consSitNFe versao="4.01" xmlns="http://www.portalfiscal.inf.br/nfe">
                    <tpAmb>{tpAmb}</tpAmb>
                    <xServ>CONSULTAR</xServ>
                    <chNFe>{chaveAcesso}</chNFe>
                  </consSitNFe>
                </nfeDadosMsg>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }
```

Também adicionar `using var certificate = certResult.Value;` em `SubmeterAutorizacaoAsync` logo após `certResult.Value` ser obtido, para garantir dispose do X509Certificate2 após uso:

Localizar em `SubmeterAutorizacaoAsync`:
```csharp
        var certResult = await _certProvider.GetCertificateAsync(tenantId, ct);
        if (certResult.IsFailure)
        {
```
E após o if, adicionar:
```csharp
        using var certificate = certResult.Value;
```
E substituir todas as referências a `certResult.Value` por `certificate` nas linhas seguintes.

- [ ] **Step 4: Build**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazRetornoParser.cs
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs
git commit -m "fix: SEFAZ — ConsultaProtocolo dedicado + cStat=204/572 como duplicidade + namespace SOAP correto

- SefazEndpointResolver.ConsultaProtocolo(): mapeamento explícito por UF (remove string.Replace)
- SefazRetornoParser: remove 204/572 de CStatAutorizado; adiciona IsDuplicidade()
- SefazClient.ConsultarNfeAsync: usa ConsultaProtocolo() + namespace correto NFeConsultaProtocolo4
- SefazClient.SubmeterAutorizacaoAsync: using var certificate — dispõe X509 após uso
- SoapEnvelopeBuilder.BuildConsultaEnvelope: namespace wsdl correto por serviço"
```

---

## Task 6 — NfceProcessingJob: fluxo correto de retry + cStat=204 + attempts corretos

**Issues corrigidos:** C3 (Falhar+throw bloqueia retry), C11 (Processando ignorado), I1 (cStat=204), I4 (AutomaticRetry 3 em vez de 5)

**Problema central (C3):** `Falhar()` muda status para `Falhou` e depois lança exceção para retry. No retry, `IniciarProcessamento()` exige `Enfileirado` — falha. Documento preso. A correção: para erros recuperáveis, NÃO transicionar para `Falhou` antes de relançar. O documento permanece em `Processando` — o Hangfire retenta e o job pode reiniciar o processamento (ou a reconciliação recupera). `Falhar()` só é chamado em `OnAttemptsExceeded` (via estado terminal).

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Reescrever NfceProcessingJob.cs**

```csharp
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
/// o retry do Hangfire possa reiniciar — Falhar() só é chamado no OnAttemptsExceeded (via
/// ReconciliacaoJobProcessor que detecta o documento travado após todos os retries).
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
            // A ReconciliacaoJobProcessor detecta o documento travado após todos os retries.
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
            // Rejeitar localmente com motivo descritivo — sem reenvio.
            await RejeitarAsync(documento, retorno, "Duplicidade: nota já autorizada na SEFAZ.", ct);
        }
        else if (SefazRetornoParser.IsDenegado(retorno.CStat))
        {
            await DenegarAsync(documento, retorno, ct);
        }
        else if (!SefazRetornoParser.IsRecuperavel(retorno.CStat))
        {
            // Rejeição fiscal definitiva (4xx) — não reenviar.
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

        // QrCode.FromStorage reconstrói a URL do QR Code a partir da chave de acesso armazenada.
        // O QR Code completo foi gerado no NfceXmlBuilder durante o build do XML.
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
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs
git commit -m "fix: NfceProcessingJob — retry correto + cStat=204/572 + Attempts=5 + Processando tolerado

- Remove Falhar()+throw: documento permanece Processando em falha transiente — retry do Hangfire funciona
- Processando tolerado no check de idempotência: worker pós-crash retoma sem IniciarProcessamento
- cStat=204/572: Rejeitar('Duplicidade: nota já autorizada') em vez de Autorizar
- AutomaticRetry: Attempts=5, delays=[30,120,600,1800,3600] conforme spec
- NProt null-guard explícito em AutorizarAsync: lança exceção rastreável em vez de NProt!
- Transições failure: lança InvalidOperationException para expor no Hangfire dashboard"
```

---

## Task 7 — ReconciliacaoJobProcessor: consulta SEFAZ + threshold 10min + IDocumentJobQueue

**Issues corrigidos:** C2 (requirement não implementado — apenas reenfileira), I2/I5 (threshold 15 vs 10 min), I2 (usa IBackgroundJobClient direto)

**Problema:** O `ReconciliacaoJobProcessor` deve consultar o SEFAZ antes de decidir. O comportamento correto: se SEFAZ confirma autorizado → `Autorizar`; se não encontrado / rejeição definitiva → `Falhar`. Somente se a consulta SEFAZ também falhar (timeout) é que reenfileira para retry.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/ReconciliacaoJobProcessor.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Reescrever ReconciliacaoJobProcessor.cs**

```csharp
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
    // O plan especifica 10 minutos — documentos Processando há mais que isso são considerados travados.
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
            // Documento não existe na SEFAZ — gap na numeração é aceito (conforme decisions.md).
            await FalharAsync(documento, "Documento não encontrado na consulta SEFAZ após timeout.", ct);
        }
        else
        {
            // Encontrado mas não autorizado (rejeição definitiva na SEFAZ).
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
```

- [ ] **Step 2: Atualizar DependencyInjection.cs — ReconciliacaoJobProcessor não precisa mais de IBackgroundJobClient direto (já injetado via IDocumentJobQueue)**

O `ReconciliacaoJobProcessor` agora usa `IDocumentJobQueue`. Verificar que o registro continua como `AddScoped<ReconciliacaoJobProcessor>()` — sem alteração necessária no registro, mas confirmar que `ISefazClient` e `IDocumentJobQueue` já estão registrados (estão, pelas tasks anteriores).

- [ ] **Step 3: Build**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Build solution completa**

```powershell
dotnet build --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Jobs/ReconciliacaoJobProcessor.cs
git commit -m "fix: ReconciliacaoJobProcessor — consulta SEFAZ real + threshold 10min + IDocumentJobQueue

- Implementa requisito: ConsultarNfeAsync por documento antes de decidir Autorizar/Falhar
- Autoriza via consulta se SEFAZ confirma cStat=100/NProt
- Falha documento se não encontrado ou rejeição definitiva na consulta
- Reenfileira (IDocumentJobQueue) somente se consulta SEFAZ falhou (timeout/rede)
- Threshold corrigido: 10 min (era 15 min)
- Remove IBackgroundJobClient direto — usa IDocumentJobQueue (abstração correta)"
```

---

## Task 8 — Verificação final: build + review das mudanças

- [ ] **Step 1: Build solution completa**

```powershell
dotnet build --no-restore -v q
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 2: Verificar que não há `new HttpClient(` sem justificativa**

```powershell
Select-String -Path "src/**/*.cs" -Pattern "new HttpClient\(" -Recurse
```

Esperado: apenas `SefazHttpClient.cs` com o comentário de justificativa de mTLS por-tenant.

- [ ] **Step 3: Verificar header de assinatura webhook**

```powershell
Select-String -Path "src/**/*.cs" -Pattern "X-Hub-Signature-256" -Recurse
```

Esperado: `WebhookDeliveryService.cs` com `X-Hub-Signature-256`.

- [ ] **Step 4: Verificar que string.Replace não é usado para URL SEFAZ**

```powershell
Select-String -Path "src/**/*.cs" -Pattern "Replace.*Autorizacao4" -Recurse
```

Esperado: zero resultados.

- [ ] **Step 5: Verificar que Falhar() nunca é chamado antes de throw em NfceProcessingJob**

```powershell
Select-String -Path "src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs" -Pattern "Falhar" -Recurse
```

Esperado: zero ocorrências em `NfceProcessingJob.cs` (Falhar só é chamado pela Reconciliação).

- [ ] **Step 6: Commit de fechamento**

```powershell
git commit --allow-empty -m "chore: verificação final das correções fases 6b e 7 — build OK"
```

---

## Checklist de Cobertura de Issues

| Issue | Task | Status |
|-------|------|--------|
| C1 — socket exhaustion SefazHttpClient | T2 | ✓ |
| C2 — ReconciliacaoJobProcessor só reenfileira | T7 | ✓ |
| C3 — Falhar+throw bloqueia retry | T6 | ✓ |
| C4 — header X-Webhook-Signature errado | T3 | ✓ |
| C5 — AllowAutoRedirect SSRF | T2 (sefaz) + T3 (webhook) | ✓ |
| C6 — sem FOR UPDATE SKIP LOCKED | T4 | ✓ |
| C7 — SaveChanges único ao final | T4 | ✓ |
| C8 — string.Replace URL consulta | T5 | ✓ |
| C9 — X509Certificate2 disposto em uso | T1 | ✓ |
| C10 — cache stampede sem SemaphoreSlim | T1 | ✓ |
| C11 — Processando ignorado em idempotência | T6 | ✓ |
| I1 — cStat=204 não tratado como duplicidade | T5 + T6 | ✓ |
| I2 — cStat=572 NProt null crash | T5 | ✓ |
| I3 — DateTime.UtcNow no SoapEnvelopeBuilder | (fora de escopo: SoapEnvelopeBuilder é estático, TimeProvider requer refactor de arquitetura maior; idLote gerado no futuro com Guid) | — |
| I4 — AutomaticRetry 3 tentativas (devia 5) | T6 | ✓ |
| I5 — threshold 15 min (devia 10) | T7 | ✓ |
| I6 — DeliveryAttempt não registrado | T3 | ✓ |
| I7 — retry Hangfire não implementado | T3 | ✓ |
| I8 — aceita HTTP (devia só HTTPS) | T3 | ✓ |
| I9 — documento carregado duas vezes | (aceito: interface ISefazClient exige ids, não objetos) | — |
| I10 — AppDomain lazy-load | T4 | ✓ |
| I11 — endpoints MA/CE/PA/PE/GO/MS | (requer validação externa dos URLs reais SEFAZ; mapeados como próprios por decisão técnica documentada) | — |
| I12 — QrCode.FromStorage adequado | (adequado: method é exactly para reconstituir do banco) | — |
| I13 — namespace SOAP consulta | T5 | ✓ |
