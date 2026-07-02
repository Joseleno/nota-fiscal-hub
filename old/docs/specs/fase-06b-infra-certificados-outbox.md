# Spec Técnica — Fase 6b: Infraestrutura — Certificados com Banco e OutboxRelayJob

**Versão:** 1.0
**Data:** 2026-05-12
**Dependências:** Fase 2 (Banco/EF Core), Fase 6a (CertificateEncryptionService)
**Esforço estimado:** P (1–2 dias)

---

## 1. Visão Geral da Fase

### Objetivo

Implementar os componentes de infraestrutura que dependem tanto do banco de dados quanto dos serviços de criptografia: cache de certificados A1 com isolamento por tenant, provider de certificados que une banco + cache, serviço de entrega de webhooks com proteção anti-SSRF e o relay job do Outbox Pattern.

### Por que separado de 6a

Os componentes desta fase requerem `ITenantRepository` e `ApplicationDbContext` (para o Outbox query raw), que por sua vez dependem das migrations da Fase 2. Separar garante que a Fase 6a possa ser desenvolvida e testada em paralelo com a Fase 2, sem esperar que o banco esteja pronto.

### Dependências obrigatórias resolvidas antes desta fase

| Artefato | De onde vem |
|---|---|
| `CertificateEncryptionService` | Fase 6a |
| `ITenantCertificateProvider` | Fase 1 — Application Interface |
| `ICertificateEncryptionService` | Fase 1 — Application Interface |
| `IWebhookDeliveryService` | Fase 1 — Application Interface |
| `ITenantRepository`, `IClienteAppRepository` | Fase 1 (interface) + Fase 2 (implementação) |
| `IDocumentoFiscalRepository` | Fase 1 + Fase 2 |
| `OutboxMessage` entity + `ApplicationDbContext.OutboxMessages` | Fase 2 |
| `IUnitOfWork` | Fase 1 + Fase 2 |
| `DocumentoFiscalAutorizadoEvent` e demais domain events | Fase 1 |
| `IMediator` (Mediator.SourceGenerator) | Fase 3 |
| `IBackgroundJobClient` (Hangfire) | Fase 2 (registrado na DI) |

### Critério de Conclusão

- `CertificateCache` armazena `byte[]` (PFX decriptografado), não `X509Certificate2`
- `CertificateCache` usa apenas `X509KeyStorageFlags.EphemeralKeySet` — nunca `MachineKeySet`
- `CertificateCache` usa `SemaphoreSlim` por `TenantId` — sem race condition no warm-up
- `WebhookDeliveryService` rejeita URLs RFC 1918 e loopback tanto no cadastro quanto no runtime
- `WebhookDeliveryService` usa `AllowAutoRedirect = false`
- `OutboxRelayJob` processa com `SELECT ... FOR UPDATE SKIP LOCKED`
- `OutboxRelayJob` marca `processed_at = now()` após publicar cada evento

---

## 2. Árvore de Arquivos

```
src/VisuFiscalHub.Infrastructure/
  Fiscal/
    Certificates/
      CertificateCache.cs
      TenantCertificateProvider.cs
  Webhook/
    WebhookDeliveryService.cs
    SsrfGuard.cs
  Scheduling/
    OutboxRelayJob.cs
```

**Total: 5 arquivos .cs**

---

## 3. Especificação por Arquivo

---

### 3.1 `CertificateCache.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/Certificates/CertificateCache.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Certificates`

**Usings:**
```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura:**
```csharp
public sealed class CertificateCache : IDisposable
{
    private const int TtlMinutes = 30;

    // SemaphoreSlim por TenantId: previne cache stampede
    private readonly ConcurrentDictionary<TenantId, SemaphoreSlim> _semaphores = new();
    private readonly IMemoryCache _cache;
    private readonly ILogger<CertificateCache> _logger;

    public CertificateCache(IMemoryCache cache, ILogger<CertificateCache> logger);

    /// <summary>
    /// Retorna byte[] do PFX decriptografado. Cria entrada no cache se ausente.
    /// Usa SemaphoreSlim por TenantId para evitar múltiplas decriptografias simultâneas.
    /// </summary>
    public async Task<byte[]> GetOrLoadAsync(
        TenantId tenantId,
        Func<CancellationToken, Task<byte[]>> loadFunc,
        CancellationToken cancellationToken = default);

    public void Invalidate(TenantId tenantId);

    public void Dispose();
}
```

**Invariantes críticas:**

1. **Cache armazena `byte[]`**, nunca `X509Certificate2`
   - Motivo: `X509Certificate2` em cache de longa duração causa memory leak de OS handles nativos
   - O objeto `X509Certificate2` deve ser criado na hora do uso com `using var cert = X509CertificateLoader.LoadPkcs12(pfxBytes, senha, X509KeyStorageFlags.EphemeralKeySet)`

2. **`X509KeyStorageFlags.EphemeralKeySet` APENAS** — validação de uso correto:
   - `EphemeralKeySet` = não persiste em nenhum key store do SO
   - `EphemeralKeySet` e `MachineKeySet` são mutuamente exclusivas — nunca combinar
   - Em containers Linux (ambiente de deploy), apenas `EphemeralKeySet` é correto

3. **`SemaphoreSlim(1, 1)` por TenantId** — previne race condition:
   - Sem o semaphore: N requisições simultâneas para o mesmo tenant disparariam N decriptografias

4. **TTL: 30 minutos** via `MemoryCacheEntryOptions.AbsoluteExpirationRelativeToNow`

**Implementação de `GetOrLoadAsync` (esboço):**
```csharp
public async Task<byte[]> GetOrLoadAsync(
    TenantId tenantId,
    Func<CancellationToken, Task<byte[]>> loadFunc,
    CancellationToken cancellationToken = default)
{
    var cacheKey = $"cert:{tenantId.Value}";

    if (_cache.TryGetValue<byte[]>(cacheKey, out var cached))
        return cached!;

    var semaphore = _semaphores.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));

    await semaphore.WaitAsync(cancellationToken);
    try
    {
        // Double-check após obter o semaphore
        if (_cache.TryGetValue<byte[]>(cacheKey, out cached))
            return cached!;

        var pfxBytes = await loadFunc(cancellationToken);

        // VALIDAÇÃO: garantir que os bytes formam um PFX válido antes de cachear
        // (dispara exceção se corrompido — melhor falhar aqui do que no momento do uso)
        using var _ = X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            password: null,   // senha validada no TenantCertificateProvider
            X509KeyStorageFlags.EphemeralKeySet);

        _cache.Set(cacheKey, pfxBytes, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(TtlMinutes),
            Size = 1
        });

        _logger.LogDebug("Certificado do Tenant {TenantId} carregado no cache.", tenantId);
        return pfxBytes;
    }
    finally
    {
        semaphore.Release();
    }
}
```

**Notas de implementação:**
- Registrado como `Singleton` — compartilhado entre todos os requests
- `IMemoryCache` injetado via DI (registrado pelo `AddMemoryCache()` na Application DI)
- `Dispose` deve liberar todos os `SemaphoreSlim` em `_semaphores`
- `Invalidate` deve remover a entrada do cache (chamado após upload de novo certificado em `UpdateTenantCertificateCommandHandler`)

---

### 3.2 `TenantCertificateProvider.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/Certificates/TenantCertificateProvider.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Certificates`

**Usings:**
```csharp
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
```

**Assinatura:**
```csharp
public sealed class TenantCertificateProvider : ITenantCertificateProvider
{
    private readonly CertificateCache _cache;
    private readonly ITenantRepository _tenantRepo;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly ILogger<TenantCertificateProvider> _logger;

    public TenantCertificateProvider(
        CertificateCache cache,
        ITenantRepository tenantRepo,
        ICertificateEncryptionService encryptionService,
        ILogger<TenantCertificateProvider> logger);

    public async Task<CertificadoDigital> GetCertificateAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);
}
```

**Implementação de `GetCertificateAsync`:**

```
1. Chama _cache.GetOrLoadAsync(tenantId, loadFunc, ct)
   onde loadFunc:
     a. Carrega Tenant do repositório
     b. Valida tenant.CertificadoPfxCriptografado != null → lança TenantSemCertificadoException se null
     c. Decriptografa pfxBytes = _encryptionService.Decrypt(tenant.CertificadoPfxCriptografado)
     d. Decriptografa senha = _encryptionService.DecryptToString(tenant.CertificadoSenhaCriptografada)
     e. Retorna pfxBytes

2. Constrói e retorna CertificadoDigital:
   new CertificadoDigital
   {
       VencimentoEm = tenant.CertificadoVencimento ?? DateTime.MaxValue,
       PfxBytes = pfxBytes
   }
```

**Invariantes:**
- O `CertificadoDigital` value object carrega `PfxBytes` — o consumidor (`XmlSigner`, `SefazHttpClient`) cria o `X509Certificate2` com `EphemeralKeySet` usando `using`
- Senha do PFX é decriptografada aqui e passada junto com o `CertificadoDigital`
- Se tenant não tem certificado → `Result.Failure(TenantErrors.SemCertificado)` ou exception específica (a definir na implementação)

**Notas de implementação:**
- Registrado como `Scoped` (usa `ITenantRepository` que é Scoped)
- `CertificateCache` é `Singleton`, mas `TenantCertificateProvider` é `Scoped` — o `Singleton` recebe o `IMemoryCache` que é `Singleton`

---

### 3.3 `WebhookDeliveryService.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Webhook/WebhookDeliveryService.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Webhook`

**Usings:**
```csharp
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura:**
```csharp
public sealed class WebhookDeliveryService : IWebhookDeliveryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClienteAppRepository _clienteAppRepo;
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly IUnitOfWork _uow;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly IBackgroundJobClient _jobClient;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        IHttpClientFactory httpClientFactory,
        IClienteAppRepository clienteAppRepo,
        IDocumentoFiscalRepository documentoRepo,
        IUnitOfWork uow,
        ICertificateEncryptionService encryptionService,
        IBackgroundJobClient jobClient,
        ILogger<WebhookDeliveryService> logger);

    public async Task DeliverAsync(
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
        CancellationToken cancellationToken = default);

    // Método interno chamado pelo job de retry (Hangfire)
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800])]
    public async Task RetryDeliverAsync(
        Guid documentoId,
        Guid clienteAppId,
        CancellationToken cancellationToken = default);
}
```

**Pipeline de `DeliverAsync`:**

```
1. Carrega ClienteApp
   - _clienteAppRepo.GetByIdAsync(clienteAppId)
   - Se null → loga erro e retorna (sem exception — não deve bloquear autorização do documento)
   - Se webhookUrl == null → loga info e retorna (ClienteApp sem webhook configurado)

2. Carrega DocumentoFiscal
   - _documentoRepo.GetByIdAsync(documentoId)
   - Se null → loga erro e retorna

3. Valida SSRF da URL do webhook
   - SsrfGuard.ValidarUrl(clienteApp.WebhookUrl)
   - Se URL inválida → loga Critical e retorna sem enviar

4. Decriptografa WebhookSecret
   - webhookSecret = _encryptionService.DecryptToString(clienteApp.WebhookSecretCriptografado)

5. Monta payload JSON
   - {
       documentoId: string,
       status: "authorized|rejected|error|denied",
       chaveAcesso: string?,
       qrCode: string?,
       protocolo: string?,
       motivoRejeicao: string?,
       authorizedAt: DateTimeOffset?
     }
   - NÃO inclui xmlAssinado

6. Assina payload com HMAC-SHA256
   - usando: HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret))
   - sobre: Encoding.UTF8.GetBytes(jsonPayload)
   - header: "X-Hub-Signature-256: sha256=" + Convert.ToHexString(hash).ToLowerInvariant()

7. Envia HTTP POST
   - via _httpClientFactory.CreateClient("webhook")
   - AllowAutoRedirect = false (configurado no registro do HttpClient)
   - Content-Type: application/json
   - Header: X-Hub-Signature-256
   - Timeout: 10 segundos

8. Registra DeliveryAttempt
   - Success = response.IsSuccessStatusCode
   - ResponseCode = (int)response.StatusCode
   - ElapsedMs medido com Stopwatch
   - TipoTentativa = TipoTentativa.Envio

9. Se falhou → enfileira retry via Hangfire
   - _jobClient.Enqueue(() => RetryDeliverAsync(documentoId.Value, clienteAppId.Value, CancellationToken.None))
   - Retry: 3 tentativas com backoff 30s, 5min, 30min
```

**Registro do HttpClient (na `DependencyInjection.cs`):**
```csharp
services.AddHttpClient("webhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false   // CRÍTICO: previne redirect bypass SSRF
});
```

**Notas de implementação:**
- Registrado como `Scoped`
- NUNCA criar `HttpClientHandler` por chamada — usar `IHttpClientFactory` sempre
- O `webhookSecret` deve ser apagado da memória após uso (não implementar zeroing no MVP mas documentar)
- `DeliveryAttempt` registrado via `_uow.SaveChangesAsync` após cada tentativa

---

### 3.4 `SsrfGuard.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Webhook/SsrfGuard.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Webhook`

**Usings:**
```csharp
using System.Net;
using System.Net.Sockets;
```

**Assinatura:**
```csharp
/// <summary>
/// Proteção contra Server-Side Request Forgery (SSRF).
/// Rejeita URLs que apontam para redes privadas, loopback ou link-local.
/// </summary>
public static class SsrfGuard
{
    /// <summary>
    /// Valida se a URL é segura para envio de webhook.
    /// Retorna (true, null) se segura, (false, mensagem) se bloqueada.
    /// </summary>
    public static (bool IsValid, string? Reason) ValidarUrl(string? url);

    /// <summary>
    /// Valida se um IPAddress é privado/loopback/link-local.
    /// </summary>
    public static bool IsPrivateOrReserved(IPAddress address);
}
```

**Ranges bloqueados obrigatórios (RFC 1918 + especiais):**

```
Loopback IPv4:      127.0.0.0/8
Loopback IPv6:      ::1
Link-local IPv4:    169.254.0.0/16  (metadata cloud instances!)
Link-local IPv6:    fe80::/10
RFC 1918 privados:
  10.0.0.0/8        (10.x.x.x)
  172.16.0.0/12     (172.16.x.x até 172.31.x.x)
  192.168.0.0/16    (192.168.x.x)
Apenas HTTPS:       rejeitar scheme http://
```

**Implementação de `ValidarUrl`:**
```csharp
public static (bool IsValid, string? Reason) ValidarUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
        return (false, "URL do webhook é nula ou vazia.");

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return (false, "URL do webhook é inválida.");

    if (uri.Scheme != Uri.UriSchemeHttps)
        return (false, "URL do webhook deve usar HTTPS.");

    // Resolver IP para validar
    try
    {
        var addresses = Dns.GetHostAddresses(uri.Host);
        foreach (var address in addresses)
        {
            if (IsPrivateOrReserved(address))
                return (false, $"URL do webhook aponta para endereço privado/reservado: {address}.");
        }
    }
    catch (SocketException ex)
    {
        return (false, $"Não foi possível resolver o host do webhook: {ex.Message}");
    }

    return (true, null);
}

public static bool IsPrivateOrReserved(IPAddress address)
{
    if (IPAddress.IsLoopback(address))
        return true;

    var bytes = address.MapToIPv4().GetAddressBytes();

    // 10.0.0.0/8
    if (bytes[0] == 10) return true;
    // 172.16.0.0/12
    if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
    // 192.168.0.0/16
    if (bytes[0] == 192 && bytes[1] == 168) return true;
    // 169.254.0.0/16 (link-local / cloud metadata)
    if (bytes[0] == 169 && bytes[1] == 254) return true;

    // IPv6 link-local fe80::/10
    if (address.AddressFamily == AddressFamily.InterNetworkV6)
    {
        var ipv6Bytes = address.GetAddressBytes();
        if (ipv6Bytes[0] == 0xfe && (ipv6Bytes[1] & 0xc0) == 0x80) return true;
    }

    return false;
}
```

**Invariantes críticas:**
- Validação deve ocorrer TANTO no cadastro (handler) quanto no runtime (antes de cada envio)
- `AllowAutoRedirect = false` no HttpClient garante que um redirect para `169.254.169.254` (AWS metadata) não contorne a validação feita no host original
- Se resolver DNS falhar → bloquear por segurança (fail closed)
- `169.254.169.254` é o endpoint de instance metadata da AWS/GCP/Azure — alvo comum de SSRF

**Notas de implementação:**
- Classe estática, sem instância e sem DI
- Validação no cadastro feita no `CreateClienteAppCommandValidator` ou no handler
- Em produção com IPv6, ajustar `MapToIPv4()` para tratar corretamente IPv6 puro

---

### 3.5 `OutboxRelayJob.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Scheduling/OutboxRelayJob.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Scheduling`

**Usings:**
```csharp
using System.Text.Json;
using Hangfire;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Infrastructure.Persistence;
```

**Assinatura:**
```csharp
public sealed class OutboxRelayJob
{
    private const int BatchSize = 50;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMediator _mediator;
    private readonly ILogger<OutboxRelayJob> _logger;

    public OutboxRelayJob(
        ApplicationDbContext dbContext,
        IMediator mediator,
        ILogger<OutboxRelayJob> logger);

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task ProcessarPendentesAsync(CancellationToken cancellationToken = default);
}
```

**Implementação de `ProcessarPendentesAsync`:**

```
1. Query com SELECT ... FOR UPDATE SKIP LOCKED:
   SQL: SELECT id, event_type, payload, occurred_at
        FROM outbox_messages
        WHERE processed_at IS NULL
        ORDER BY occurred_at ASC
        LIMIT 50
        FOR UPDATE SKIP LOCKED

   Via EF Core + raw SQL ou via IQueryable com hint (Npgsql suporta ForUpdateSkipLocked):
   var mensagens = await _dbContext.OutboxMessages
       .Where(m => m.ProcessedAt == null)
       .OrderBy(m => m.OccurredAt)
       .Take(BatchSize)
       .FromSqlRaw(sql)    // ou usar .TagWith() + ExecuteSqlRaw
       .ToListAsync(cancellationToken);

   Alternativa recomendada (raw SQL para garantir SKIP LOCKED):
   var mensagens = await _dbContext.Database
       .SqlQueryRaw<OutboxMessageRow>(
           @"SELECT id, event_type, payload, occurred_at
             FROM outbox_messages
             WHERE processed_at IS NULL
             ORDER BY occurred_at
             LIMIT {0}
             FOR UPDATE SKIP LOCKED",
           BatchSize)
       .ToListAsync(cancellationToken);

2. Para cada mensagem:
   a. Resolve tipo: Type? eventType = Type.GetType(mensagem.EventType);
      Se null → loga Warning e continua (EventType desconhecido)

   b. Deserializa: var domainEvent = (INotification)JsonSerializer.Deserialize(
         mensagem.Payload, eventType, JsonSerializerOptions.Default)!;

   c. Publica: await _mediator.Publish(domainEvent, cancellationToken);

   d. Marca como processado:
      await _dbContext.Database.ExecuteSqlRawAsync(
          "UPDATE outbox_messages SET processed_at = NOW() WHERE id = {0}",
          mensagem.Id, cancellationToken);

3. Loga: _logger.LogInformation("OutboxRelay: {Count} mensagens processadas.", count);
```

**Invariantes críticas:**

1. **`SELECT ... FOR UPDATE SKIP LOCKED`** — garantia de multi-instância:
   - `FOR UPDATE` bloqueia a linha durante o processamento
   - `SKIP LOCKED` pula linhas bloqueadas por outras instâncias (sem espera)
   - Garante que duas instâncias do hub rodando em paralelo não processem o mesmo evento

2. **Marcar `processed_at` APÓS publicar** — não antes:
   - Se `IMediator.Publish` falhar, a mensagem permanece pendente para retry
   - Se `processed_at` fosse gravado antes e `Publish` falhasse, o evento seria perdido

3. **Idempotência**: `SKIP LOCKED` + marca `processed_at` após publicar = sem duplicação

4. **Não lançar exceção para EventType desconhecido**: Log.Warning e continua o batch

**Registro do job periódico (em `DependencyInjection.cs` ou `Program.cs`):**
```csharp
// Registrar job recorrente a cada 30 segundos
RecurringJob.AddOrUpdate<OutboxRelayJob>(
    "outbox-relay",
    job => job.ProcessarPendentesAsync(CancellationToken.None),
    "*/30 * * * * *");   // Cron a cada 30s (suporte do Hangfire via segundos)
```

**Notas de implementação:**
- `[DisableConcurrentExecution(timeoutInSeconds: 60)]` previne sobreposição de execuções do mesmo job
- Registrado como `Scoped` (recebe `ApplicationDbContext` Scoped)
- O `ApplicationDbContext` é injetado diretamente aqui (não via `IUnitOfWork`) porque o query raw SQL para `SKIP LOCKED` precisa de acesso direto ao contexto
- Tratar exceptions por mensagem individualmente para não interromper o batch inteiro
- `DisableConcurrentExecution` é um atributo do Hangfire — verificar versão do pacote

---

## 4. Fluxo de Dados

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
FLUXO: CertificateCache + TenantCertificateProvider
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[HangfireJobProcessor ou XmlSigner precisam de X509Certificate2]
    │
    ▼
[TenantCertificateProvider.GetCertificateAsync(tenantId)]
    │
    ├─► CertificateCache.GetOrLoadAsync(tenantId, loadFunc)
    │       │
    │       ├─► IMemoryCache.TryGetValue("cert:{tenantId}") → HIT → return byte[]
    │       │
    │       └─► MISS:
    │             SemaphoreSlim[tenantId].WaitAsync()   ← evita stampede
    │             Double-check cache
    │             loadFunc():
    │               ITenantRepository.GetByIdAsync(tenantId)
    │               ICertificateEncryptionService.Decrypt(tenant.CertificadoPfxCriptografado)
    │               → pfxBytes (byte[])
    │             IMemoryCache.Set("cert:{tenantId}", pfxBytes, TTL=30min)
    │             SemaphoreSlim.Release()
    │             → return pfxBytes
    │
    └─► return CertificadoDigital { PfxBytes = pfxBytes, VencimentoEm = ... }

[Consumidor — ex: XmlSigner]
    │
    ├─► using var cert = X509CertificateLoader.LoadPkcs12(
    │       pfxBytes,
    │       password,
    │       X509KeyStorageFlags.EphemeralKeySet)   ← APENAS EphemeralKeySet
    │
    └─► cert.GetRSAPrivateKey() → RSA para assinar XML


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
FLUXO: WebhookDeliveryService
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[DocumentoFiscalAutorizadoEventHandler.Handle()]
    │
    ▼
[WebhookDeliveryService.DeliverAsync(documentoId, clienteAppId)]
    │
    ├─1─► Carrega ClienteApp (webhookUrl, webhookSecretCriptografado)
    │
    ├─2─► Carrega DocumentoFiscal (status, chaveAcesso, qrCode, protocolo)
    │
    ├─3─► SsrfGuard.ValidarUrl(webhookUrl)
    │       ├─► Rejeita: 127.x.x.x, ::1, 10.x.x.x, 172.16-31.x.x, 192.168.x.x, 169.254.x.x
    │       └─► Se SSRF detectado → loga Critical + return (sem envio)
    │
    ├─4─► ICertificateEncryptionService.DecryptToString(webhookSecretCriptografado)
    │       └─► webhookSecret (string)
    │
    ├─5─► Monta payload JSON
    │       └─► { documentoId, status, chaveAcesso, qrCode, protocolo, motivoRejeicao, authorizedAt }
    │           NÃO inclui xmlAssinado
    │
    ├─6─► HMAC-SHA256(key=webhookSecret, data=jsonPayload)
    │       └─► header: "X-Hub-Signature-256: sha256={hex}"
    │
    ├─7─► HttpClient (IHttpClientFactory, AllowAutoRedirect=false)
    │       └─► POST webhookUrl
    │             Content-Type: application/json
    │             X-Hub-Signature-256: sha256=...
    │
    ├─8─► Registra DeliveryAttempt
    │
    └─9─► Se falhou → Hangfire.RetryDeliverAsync (30s, 5min, 30min)


━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
FLUXO: OutboxRelayJob (executado a cada 30s via Hangfire)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[Hangfire: OutboxRelayJob.ProcessarPendentesAsync()] a cada 30s
    │
    ├─► SELECT id, event_type, payload FROM outbox_messages
    │     WHERE processed_at IS NULL
    │     ORDER BY occurred_at
    │     LIMIT 50
    │     FOR UPDATE SKIP LOCKED
    │       └─► Instâncias paralelas do hub pegam mensagens diferentes
    │
    ├─► Para cada OutboxMessage:
    │     Type eventType = Type.GetType(mensagem.EventType)
    │     INotification domainEvent = JsonSerializer.Deserialize(mensagem.Payload, eventType)
    │     await IMediator.Publish(domainEvent)
    │         │
    │         ├─► DocumentoFiscalAutorizadoEvent
    │         │     └─► DocumentoFiscalAutorizadoEventHandler → WebhookDeliveryService
    │         │
    │         ├─► DocumentoFiscalRejeitadoEvent
    │         │     └─► (handler futuro — webhook de rejeição)
    │         │
    │         └─► DocumentoFiscalDenegadoEvent
    │               └─► DocumentoFiscalDenegadoEventHandler → Log.Critical + Webhook
    │
    └─► UPDATE outbox_messages SET processed_at = NOW() WHERE id = {id}
            └─► Após publicar com sucesso

GARANTIA MULTI-INSTÂNCIA:
  Instância A → pega mensagens 1..50 (FOR UPDATE)
  Instância B → pega mensagens 51..100 (SKIP LOCKED ignora 1..50)
  Zero duplicação, zero perda.
```

---

## 5. Checklist de Conclusão

### CertificateCache
- [ ] Armazena `byte[]` (PFX), nunca `X509Certificate2`
- [ ] TTL de 30 minutos via `AbsoluteExpirationRelativeToNow`
- [ ] `SemaphoreSlim(1, 1)` por `TenantId` — double-check após obter semaphore
- [ ] `X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.EphemeralKeySet)` apenas para validação do PFX antes de cachear
- [ ] `Invalidate(tenantId)` disponível para ser chamado após upload de certificado
- [ ] `Dispose()` libera todos os semaphores

### TenantCertificateProvider
- [ ] Implementa `ITenantCertificateProvider`
- [ ] Usa `CertificateCache.GetOrLoadAsync` — não acessa banco diretamente sem cache
- [ ] Decriptografa PFX e senha antes de retornar `CertificadoDigital`
- [ ] Lança erro descritivo se tenant sem certificado

### WebhookDeliveryService
- [ ] Implementa `IWebhookDeliveryService`
- [ ] Registrado via `IHttpClientFactory` — nunca cria `HttpClientHandler` por chamada
- [ ] `AllowAutoRedirect = false` configurado no `HttpClient` registrado
- [ ] Valida SSRF via `SsrfGuard.ValidarUrl` antes de cada envio
- [ ] Decriptografa `WebhookSecretCriptografado` via `ICertificateEncryptionService`
- [ ] Header `X-Hub-Signature-256: sha256={hex}` em minúsculas
- [ ] Payload não inclui `XmlAssinado`
- [ ] `DeliveryAttempt` registrado para cada tentativa
- [ ] Retry via Hangfire: 3 tentativas com delays 30s, 5min, 30min

### SsrfGuard
- [ ] Rejeita `http://` (somente `https://` permitido)
- [ ] Rejeita `10.0.0.0/8`
- [ ] Rejeita `172.16.0.0/12` (172.16.x.x até 172.31.x.x)
- [ ] Rejeita `192.168.0.0/16`
- [ ] Rejeita `127.0.0.0/8` (loopback)
- [ ] Rejeita `169.254.0.0/16` (link-local / cloud metadata)
- [ ] Rejeita `::1` e `fe80::/10` (IPv6 loopback/link-local)
- [ ] Bloqueia se DNS não resolver (fail closed)
- [ ] Testes unitários com valores concretos: `10.0.0.1`, `192.168.1.1`, `172.16.0.1`, URL pública deve passar

### OutboxRelayJob
- [ ] Query usa `FOR UPDATE SKIP LOCKED` (SQL raw via `ExecuteSqlRaw` ou hint EF Core)
- [ ] Marca `processed_at = NOW()` APÓS `IMediator.Publish` com sucesso
- [ ] EventType desconhecido gera `LogWarning` e continua o batch (sem exception que aborte)
- [ ] `[DisableConcurrentExecution]` evita sobreposição de execuções
- [ ] Registrado como job recorrente a cada 30s via `RecurringJob.AddOrUpdate`
- [ ] Processa máximo 50 mensagens por execução (`BatchSize = 50`)

### Flags de Certificado
- [ ] `X509KeyStorageFlags.EphemeralKeySet` usado em TODOS os locais onde `LoadPkcs12` é chamado
- [ ] Nenhum uso de `MachineKeySet`, `UserKeySet` ou `PersistKeySet`
- [ ] Todo uso de `X509Certificate2` encapsulado em `using` (descartado após uso)

### Clean Architecture
- [ ] `WebhookDeliveryService` não acessa `IHttpContextAccessor`
- [ ] `OutboxRelayJob` usa `ApplicationDbContext` diretamente (justificado: raw SQL `SKIP LOCKED`)
- [ ] `CertificateCache` e `TenantCertificateProvider` registrados em `DependencyInjection.cs`
- [ ] `WebhookDeliveryService` registrado em `DependencyInjection.cs`
- [ ] `OutboxRelayJob` registrado em `DependencyInjection.cs` (job Hangfire via `RecurringJob.AddOrUpdate`)
- [ ] `SsrfGuard` é classe estática — sem registro de DI necessário

### Testes Obrigatórios (Fase 10)
- [ ] `Deliver_DeveAssinarPayloadComHmacSha256()` — verifica header `X-Hub-Signature-256`
- [ ] `Deliver_DeveBloquearUrlRfc1918()` — testa `10.0.0.1`, `192.168.1.1`, `172.16.0.1`
- [ ] `Deliver_NaoDeveBloquearUrlPublica()` — URL pública válida HTTPS
- [ ] `Deliver_DeveCalcularAssinaturaCorretamente()` — valor pré-computado em `[InlineData]`
- [ ] `Processar_QuandoMensagemPendente_DevePublicarEventoEMarcarProcessado()`
- [ ] `Processar_QuandoNenhumaMensagem_NaoDevePublicarNada()`
- [ ] `Encrypt_Decrypt_RoundTrip_DeveRetornarBytesOriginais()` (de 6a — relevante aqui para validar integração)
