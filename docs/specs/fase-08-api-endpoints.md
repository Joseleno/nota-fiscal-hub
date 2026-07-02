# Spec Fase 8 — API Endpoints

**Versão:** 1.0
**Data:** 2026-05-12
**Dependências:** Fases 4 (JWT), 5 (Emissão), 7 (ISefazClient registrado)
**Critério de conclusão:** `Program.cs` termina com `public partial class Program { }`; 429 retorna `Retry-After: 60`; todos os endpoints usam `TypedResults`; `POST /api/v1/clientes` usa `CryptographicOperations.FixedTimeEquals`

---

## 1. Visão Geral

### Objetivo

Expor todos os endpoints do VisuFiscalHub via ASP.NET Core Minimal APIs com documentação Scalar, rate limiting, autenticação JWT RS256, health checks e tratamento padronizado de erros via `ProblemDetails`.

### Dependências diretas

| Dependência | Fase |
|---|---|
| JWT RS256 Bearer configurado | Fase 4 |
| `TenantValidationMiddleware` | Fase 4 |
| `ICurrentUserContext` / `HttpContextCurrentUserContext` | Fase 3 |
| Todos os Commands e Queries | Fases 3, 5 |
| `ISefazClient` registrado no DI | Fase 7 |
| `JwtSettings` com `ValidateOnStart` | Fase 4 |

### Critério de conclusão

- [ ] `Program.cs` termina com `public partial class Program { }` (última linha)
- [ ] `IssuerSigningKeys` (lista, não singular) configurado
- [ ] `services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()`
- [ ] `ValidateOnStart()` para `JwtSettings`
- [ ] Todos os endpoints retornam `TypedResults`, erros retornam `ProblemDetails`
- [ ] 429 retorna header `Retry-After: 60`
- [ ] `POST /api/v1/clientes` usa `CryptographicOperations.FixedTimeEquals`
- [ ] `PUT /api/v1/tenants/{id}/certificado` tem `[RequestSizeLimit(50 * 1024)]`
- [ ] `GET /api/v1/documentos/{id}/xml` é endpoint SEPARADO de `GET /api/v1/documentos/{id}/status`
- [ ] `DocumentoStatusResponse` NÃO expõe `xmlAssinado`
- [ ] Endpoints de rotação `rotate-secret` e `rotate-webhook-secret` implementados

---

## 2. Árvore de Arquivos

```
src/VisuFiscalHub.Api/
├── Program.cs                                      ← configuração completa + partial class
├── Authentication/
│   ├── HttpContextCurrentUserContext.cs            ← implementação de ICurrentUserContext
│   └── JwtBearerConfiguration.cs                  ← helpers de configuração JWT (opcional)
├── Endpoints/
│   ├── AuthEndpoints.cs                            ← POST /auth/token
│   ├── ClienteAppEndpoints.cs                      ← POST /api/v1/clientes + rotações
│   ├── TenantEndpoints.cs                          ← CRUD tenants + certificado + CSC
│   ├── DocumentoEndpoints.cs                       ← emissão + status + xml + cancelar
│   └── HealthEndpoints.cs                          ← /health, /health/live, /health/ready
├── Middleware/
│   ├── TenantValidationMiddleware.cs               ← (Fase 4 — referência)
│   └── GlobalExceptionHandler.cs                  ← IExceptionHandler para ProblemDetails
├── Models/
│   └── AdminKeySettings.cs                        ← IOptions binding para X-Admin-Key
└── appsettings.json / appsettings.Development.json
```

---

## 3. Arquivos — Especificações Detalhadas

### 3.1 `Program.cs`

**Namespace:** (top-level — sem namespace explícito para compatibilidade com `WebApplicationFactory<Program>`)

**Estrutura obrigatória:**

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using VisuFiscalHub.Api.Authentication;
using VisuFiscalHub.Api.Endpoints;
using VisuFiscalHub.Api.Middleware;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- Serilog ---
builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "VisuFiscalHub");
    // Enrichers de TenantId e ClienteAppId via UseSerilogRequestLogging abaixo
});

// --- Configuration validation ---
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("JWT"))
    .ValidateDataAnnotations()
    .ValidateOnStart();                          // ← startup falha se ausente/inválido

builder.Services.AddOptions<AdminKeySettings>()
    .Bind(builder.Configuration.GetSection("AdminKey"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --- Authentication JWT RS256 ---
var jwtSettings = builder.Configuration.GetSection("JWT").Get<JwtSettings>()!;
var signingKeys = new List<SecurityKey>();
foreach (var pem in jwtSettings.PublicKeyPems)
{
    var rsa = RSA.Create();
    rsa.ImportFromPem(pem);
    signingKeys.Add(new RsaSecurityKey(rsa));
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,     // ← lista para rotação zero-downtime
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// --- ICurrentUserContext (Scoped) ---
builder.Services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>();
builder.Services.AddHttpContextAccessor();

// --- Rate Limiting ---
builder.Services.AddRateLimiter(opts =>
{
    opts.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await ctx.HttpContext.Response.WriteAsync("Too many requests.", ct);
    };

    // "auth": 10 req/min por IP
    opts.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });

    // "api": 100 req/min por client_id
    opts.AddFixedWindowLimiter("api", o =>
    {
        o.PermitLimit = 100;
        o.Window = TimeSpan.FromMinutes(1);
        // Partição por client_id extraído do JWT
    });

    // "admin": 5 req/min por IP
    opts.AddFixedWindowLimiter("admin", o =>
    {
        o.PermitLimit = 5;
        o.Window = TimeSpan.FromMinutes(1);
    });
});

// --- ProblemDetails + Exception Handler ---
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// --- OpenAPI / Scalar ---
builder.Services.AddOpenApi();

// --- Health Checks ---
builder.Services.AddHealthChecks()
    .AddNpgsql(
        builder.Configuration.GetConnectionString("Default")!,
        name: "postgresql",
        tags: ["ready"])
    // Redis e outros podem ser adicionados aqui
    ;

// --- Infrastructure DI ---
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSefazInfrastructure(builder.Configuration);

// --- Application DI ---
builder.Services.AddApplication();

// --- Hangfire ---
builder.Services.AddHangfire(cfg =>
    cfg.UsePostgreSqlStorage(
        builder.Configuration.GetConnectionString("Default"),
        new PostgreSqlStorageOptions { SchemaName = "hangfire" }));
builder.Services.AddHangfireServer();

// --- OpenTelemetry ---
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddNpgsqlInstrumentation()
            .AddHangfireInstrumentation()
            .AddOtlpExporter();
    });

var app = builder.Build();

// --- Middleware pipeline ---
app.UseExceptionHandler();
app.UseSerilogRequestLogging(opts =>
{
    opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        var userContext = httpContext.RequestServices
            .GetService<ICurrentUserContext>();
        if (userContext is not null)
        {
            diagnosticContext.Set("TenantId", userContext.TenantId?.ToString() ?? "none");
            diagnosticContext.Set("ClienteAppId", userContext.ClienteAppId?.ToString() ?? "none");
        }
    };
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<TenantValidationMiddleware>();

// --- Endpoints ---
app.MapOpenApi();
app.MapScalarApiReference();
AuthEndpoints.Map(app);
ClienteAppEndpoints.Map(app);
TenantEndpoints.Map(app);
DocumentoEndpoints.Map(app);
HealthEndpoints.Map(app);

// --- Hangfire Dashboard (protegido em produção) ---
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[]
    {
        new HangfireBasicAuthFilter(
            app.Configuration["HangfireDashboard:User"]!,
            app.Configuration["HangfireDashboard:Password"]!)
    }
});

// --- Aplicar migrations no startup ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

// --- Reconciliação periódica ---
RecurringJob.AddOrUpdate<ReconciliacaoJobProcessor>(
    "reconciliacao-documentos",
    job => job.ProcessarReconciliacaoAsync(CancellationToken.None),
    "*/5 * * * *");

app.Run();

// OBRIGATÓRIO — última linha do arquivo
// Necessário para WebApplicationFactory<Program> nos testes de integração
public partial class Program { }
```

**Regras críticas:**
- `public partial class Program { }` deve ser a ÚLTIMA instrução do arquivo
- `IssuerSigningKeys` recebe lista de `RsaSecurityKey` — nunca `IssuerSigningKey` singular
- `ValidateOnStart()` aplicado a `JwtSettings` E `AdminKeySettings`
- `services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()` — Scoped obrigatório

---

### 3.2 `HttpContextCurrentUserContext.cs`

**Namespace:** `VisuFiscalHub.Api.Authentication`

**Usings:**
```csharp
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura:**
```csharp
/// <summary>
/// Implementação concreta de ICurrentUserContext para a camada de API.
/// Registrada como Scoped em Program.cs.
/// Handlers da Application layer recebem ICurrentUserContext via construtor —
/// NUNCA acessam IHttpContextAccessor diretamente.
/// </summary>
public sealed class HttpContextCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor);

    /// <summary>
    /// ClienteAppId extraído do claim "sub" do JWT.
    /// </summary>
    public ClienteAppId? ClienteAppId { get; }

    /// <summary>
    /// TenantId extraído de HttpContext.Items["TenantContext"]
    /// populado pelo TenantValidationMiddleware.
    /// </summary>
    public TenantId? TenantId { get; }
}
```

**Implementação:**
- `ClienteAppId`: `Guid.TryParse(context.User.FindFirst("sub")?.Value, out var id) ? new ClienteAppId(id) : null`
- `TenantId`: `context.Items["TenantContext"] is TenantContext tc ? tc.TenantId : null`

---

### 3.3 `GlobalExceptionHandler.cs`

**Namespace:** `VisuFiscalHub.Api.Middleware`

**Usings:**
```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
```

**Assinatura:**
```csharp
/// <summary>
/// Handler global de exceções — converte para ProblemDetails RFC 7807.
/// Registrado via services.AddExceptionHandler<GlobalExceptionHandler>().
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken);
}
```

**Mapeamento de exceções para status codes:**
```
NotFoundException (Domain)       → 404 Not Found
UnauthorizedAccessException      → 403 Forbidden
ValidationException (FluentVal.) → 422 Unprocessable Entity
InvalidOperationException        → 409 Conflict (depende do contexto)
Exception (fallback)             → 500 Internal Server Error (sem stack trace em produção)
```

---

### 3.4 `AuthEndpoints.cs`

**Namespace:** `VisuFiscalHub.Api.Endpoints`

**Endpoints:**

#### `POST /auth/token`
```
Autenticação: nenhuma
Rate limiter: "auth" (10/min por IP)
Content-Type: application/x-www-form-urlencoded OU application/json
Body: { "client_id": string, "client_secret": string, "grant_type": "client_credentials" }
Response 200: { "access_token": "JWT", "token_type": "Bearer", "expires_in": 3600 }
Response 401: { "error": "invalid_client" } (ProblemDetails)
Response 429: Retry-After: 60
```

**Implementação:**
```csharp
app.MapPost("/auth/token", async (
    TokenRequest request,
    IMediator mediator,
    ITokenService tokenService,
    CancellationToken ct) =>
{
    var result = await mediator.Send(new AuthenticateCommand(request.ClientId, request.ClientSecret), ct);
    if (result.IsFailure)
        return TypedResults.Unauthorized();

    var token = tokenService.GenerateToken(result.Value);
    return TypedResults.Ok(new TokenResponse(token, "Bearer", 3600));
})
.RequireRateLimiting("auth")
.WithOpenApi();
```

---

### 3.5 `ClienteAppEndpoints.cs`

**Namespace:** `VisuFiscalHub.Api.Endpoints`

**Usings:**
```csharp
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using VisuFiscalHub.Api.Models;
```

#### `POST /api/v1/clientes`
```
Autenticação: X-Admin-Key (comparação time-safe)
Rate limiter: "admin" (5/min por IP)
Body: CreateClienteAppRequest { name, clientId, clientSecret, webhookUrl? }
Response 201: ClienteAppCreatedResponse (inclui webhookSecret — única vez)
Response 401: X-Admin-Key ausente ou inválida
Response 409: clientId já existe
Response 429: Retry-After: 60
```

**Validação da Admin Key — CRÍTICO:**
```csharp
// CryptographicOperations.FixedTimeEquals — comparação constante no tempo
// Previne timing attacks
var adminKey = httpContext.Request.Headers["X-Admin-Key"].ToString();
var expectedKey = adminKeySettings.Value.Value;
var adminKeyBytes = System.Text.Encoding.UTF8.GetBytes(adminKey);
var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedKey);
if (!CryptographicOperations.FixedTimeEquals(adminKeyBytes, expectedBytes))
    return TypedResults.Unauthorized();
```

**Nota:** `FixedTimeEquals` compara byte arrays de mesmo tamanho. Se tamanhos diferentes, a comparação de tamanho é inevitavelmente não-constante. Implementar verificação de tamanho separada (pre-verificar tamanho com lógica que não vaze via timing, ou sempre comparar contra buffer de tamanho fixo).

#### `POST /api/v1/clientes/{id}/rotate-secret`
```
Autenticação: X-Admin-Key
Body: vazio
Response 200: { "clientId": string, "newClientSecret": string }
Response 404: ClienteApp não encontrado
```

#### `POST /api/v1/clientes/{id}/rotate-webhook-secret`
```
Autenticação: X-Admin-Key
Body: vazio
Response 200: { "clientId": string, "newWebhookSecret": string }
Response 404: ClienteApp não encontrado
```

**Estrutura do mapeamento:**
```csharp
public static class ClienteAppEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/clientes")
            .WithOpenApi()
            .WithTags("ClienteApps");

        group.MapPost("/", CreateClienteApp)
            .RequireRateLimiting("admin");

        group.MapPost("/{id}/rotate-secret", RotateSecret);
        group.MapPost("/{id}/rotate-webhook-secret", RotateWebhookSecret);
    }

    private static async Task<IResult> CreateClienteApp(...) { ... }
    private static async Task<IResult> RotateSecret(...) { ... }
    private static async Task<IResult> RotateWebhookSecret(...) { ... }
}
```

---

### 3.6 `TenantEndpoints.cs`

**Namespace:** `VisuFiscalHub.Api.Endpoints`

#### `POST /api/v1/tenants`
```
Autenticação: Bearer JWT
Rate limiter: "api"
Headers: Authorization, X-Tenant-Id (opcional aqui — é criação)
Body: CreateTenantRequest { cnpj, razaoSocial, regimeTributario, ufCodigo, serie, endereco }
Response 201: TenantResponse
Response 422: CNPJ inválido (ProblemDetails com detalhes de validação)
Response 409: CNPJ já cadastrado para este ClienteApp
```

#### `PUT /api/v1/tenants/{id}/certificado`
```
Autenticação: Bearer JWT
[RequestSizeLimit(50 * 1024)]  ← 50KB máximo
Content-Type: multipart/form-data
Form fields: pfx (IFormFile), senha (string), vencimento (DateTime ISO 8601)
Response 204: sem corpo
Response 413: PFX excede 50KB
Response 403: Tenant não pertence ao ClienteApp do JWT
```

**Implementação do limite:**
```csharp
group.MapPut("/{id}/certificado", UploadCertificado)
    .RequireAuthorization()
    .AddEndpointFilter(async (ctx, next) =>
    {
        // RequestSizeLimit via atributo não funciona diretamente em Minimal APIs
        // Usar DisableRequestSizeLimit + validação manual OU configurar via IOptions
        if (ctx.HttpContext.Request.ContentLength > 50 * 1024)
            return TypedResults.StatusCode(StatusCodes.Status413RequestEntityTooLarge);
        return await next(ctx);
    });
```

**Alternativa com Minimal APIs (.NET 10):**
```csharp
.WithRequestTimeout(TimeSpan.FromSeconds(30))
// Configurar MaxRequestBodySize no Kestrel para este endpoint específico
```

#### `PUT /api/v1/tenants/{id}/csc`
```
Autenticação: Bearer JWT
Body: { csc: string, cIdToken: string }
Response 204
```

#### `GET /api/v1/tenants/{id}`
```
Autenticação: Bearer JWT
Response 200: TenantResponse
Response 404: Tenant não encontrado
Response 403: Tenant não pertence ao ClienteApp
```

#### `GET /api/v1/tenants/{id}/certificado/status`
```
Autenticação: Bearer JWT
Response 200: { "vencimentoEm": "2026-12-31T00:00:00Z", "diasRestantes": 234 }
Response 404 / 403
```

---

### 3.7 `DocumentoEndpoints.cs`

**Namespace:** `VisuFiscalHub.Api.Endpoints`

#### `POST /api/v1/documentos/nfce`
```
Autenticação: Bearer JWT
Rate limiter: "api" (100/min por client_id)
Headers:
  Authorization: Bearer <JWT>
  X-Tenant-Id: <tenant-uuid>          ← UUID interno, não CNPJ
  X-Idempotency-Key: <uuid>           ← obrigatório
Body: IssueNfceRequest { itens[], pagamentos[], consumidor?, indPresenca? }
Response 202: IssueDocumentResponse { documentoId, status, chaveAcesso, pollUrl, createdAt }
Response 409: idempotency key com status final
Response 422: validação de negócio (ProblemDetails com detalhes dos erros)
Response 429: Retry-After: 60
```

**Particionamento do rate limiter "api" por client_id:**
```csharp
opts.AddSlidingWindowLimiter("api", o =>
{
    o.PermitLimit = 100;
    o.Window = TimeSpan.FromMinutes(1);
    o.SegmentsPerWindow = 4;
    // Partição por client_id
}).AddPartitionedRateLimiter(..., ctx =>
    ctx.User.FindFirst("client_id")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
```

**Nota:** Em .NET 10, usar `RateLimiterExtensions` com `CreatePartitioned` para rate limiting por claim.

#### `GET /api/v1/documentos/{id}/status`
```
Autenticação: Bearer JWT
Response 200: DocumentoStatusResponse
  {
    documentoId, status, chaveAcesso?, qrCode?, protocolo?,
    motivoRejeicao?, authorizedAt?
    // SEM xmlAssinado — campo proibido neste endpoint
  }
Response 404 / 403
```

#### `GET /api/v1/documentos/{id}/xml`
```
Autenticação: Bearer JWT
Response 200: { "xmlAssinado": "<?xml ..." }
Response 404: Documento não encontrado ou sem XML (ainda Processando)
Response 403: Documento não pertence ao ClienteApp do JWT
```

**CRÍTICO:** Este endpoint é SEPARADO de `/status`. O `DocumentoStatusResponse` NUNCA expõe `xmlAssinado`.

#### `POST /api/v1/documentos/{id}/cancelar`
```
Autenticação: Bearer JWT
Body: { justificativa: string (15–255 chars) }
Response 202: { documentoId, status: "Cancelando" }
Response 422: Prazo de 30 minutos expirado (ProblemDetails)
Response 404 / 403
NOTA: Implementado na Fase 6B — endpoint definido aqui, retorna 501 até Fase 6B
```

---

### 3.8 `HealthEndpoints.cs`

**Namespace:** `VisuFiscalHub.Api.Endpoints`

```csharp
public static class HealthEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // Health completo (liveness + readiness)
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        });

        // Liveness — apenas verifica se app está rodando
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false // Nenhuma dependência externa
        });

        // Readiness — verifica dependências
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = hc => hc.Tags.Contains("ready")
        });
    }
}
```

---

### 3.9 `AdminKeySettings.cs`

**Namespace:** `VisuFiscalHub.Api.Models`

```csharp
using System.ComponentModel.DataAnnotations;

namespace VisuFiscalHub.Api.Models;

/// <summary>
/// Configuração da chave administrativa.
/// Bind de AdminKey__Value na variável de ambiente.
/// </summary>
public sealed class AdminKeySettings
{
    [Required(ErrorMessage = "AdminKey__Value é obrigatório")]
    [MinLength(32, ErrorMessage = "AdminKey deve ter pelo menos 32 caracteres")]
    public string Value { get; init; } = string.Empty;
}
```

---

## 4. Fluxos e Diagramas

### 4.1 Pipeline de Autenticação e Autorização

```
Request chega
  │
  ├── /auth/token:
  │       └── Rate limiter "auth" (10/min/IP)
  │               └── Valida client_id + client_secret (PBKDF2)
  │                       └── Retorna JWT RS256
  │
  ├── /api/v1/clientes (POST):
  │       └── Rate limiter "admin" (5/min/IP)
  │               └── X-Admin-Key: FixedTimeEquals
  │                       └── CreateClienteAppCommand
  │
  └── /api/v1/documentos/nfce (POST):
          └── JWT Bearer RS256 (IssuerSigningKeys lista)
                  └── TenantValidationMiddleware
                  │       ├── Extrai client_id do JWT
                  │       ├── Lê X-Tenant-Id (UUID do Tenant)
                  │       └── Valida vínculo Tenant → ClienteApp
                  └── Rate limiter "api" (100/min/client_id)
                          └── IssueDocumentCommand
```

### 4.2 Mapeamento Result → HTTP Status

```
Result.Success              → 200/201/202/204
ClienteAppErrors.NaoEncontrado     → 404
TenantErrors.NaoEncontrado         → 404
TenantErrors.NaoPertenceAoClienteApp → 403
TenantErrors.Inativo               → 403
DocumentoFiscalErrors.NaoEncontrado → 404
DocumentoFiscalErrors.IdempotencyKeyJaUsada → 409
DocumentoFiscalErrors.TransicaoInvalida → 422
DocumentoFiscalErrors.PrazoCancelamentoExpirado → 422
Erros de validação FluentValidation → 422 com lista de erros
Erro não mapeado (fallback)        → 500
```

### 4.3 Estrutura dos Endpoints Agrupados

```
/auth/token                          (sem autenticação, rate "auth")
/api/v1/clientes
  POST /                             (X-Admin-Key, rate "admin")
  POST /{id}/rotate-secret           (X-Admin-Key)
  POST /{id}/rotate-webhook-secret   (X-Admin-Key)
/api/v1/tenants
  POST /                             (Bearer JWT, rate "api")
  GET  /{id}                         (Bearer JWT)
  PUT  /{id}/certificado             (Bearer JWT, RequestSizeLimit 50KB)
  PUT  /{id}/csc                     (Bearer JWT)
  GET  /{id}/certificado/status      (Bearer JWT)
/api/v1/documentos
  POST /nfce                         (Bearer JWT, rate "api", X-Tenant-Id, X-Idempotency-Key)
  GET  /{id}/status                  (Bearer JWT)        ← SEM xmlAssinado
  GET  /{id}/xml                     (Bearer JWT)        ← XML completo separado
  POST /{id}/cancelar                (Bearer JWT)        ← Fase 6B
/health                              (sem autenticação)
/health/live                         (sem autenticação)
/health/ready                        (sem autenticação)
/hangfire                            (autenticação básica via variável de ambiente)
```

---

## 5. Checklist de Conclusão

- [ ] `Program.cs` — última linha é `public partial class Program { }`
- [ ] `IssuerSigningKeys` recebe lista (não `IssuerSigningKey` singular)
- [ ] `ValidateOnStart()` em `JwtSettings` E `AdminKeySettings`
- [ ] `services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()`
- [ ] `POST /api/v1/clientes`: `CryptographicOperations.FixedTimeEquals` para Admin Key
- [ ] `POST /api/v1/clientes`: rate limiter "admin" (5/min por IP)
- [ ] `PUT /api/v1/tenants/{id}/certificado`: limite de 50KB implementado
- [ ] `POST /auth/token`: rate limiter "auth" (10/min por IP)
- [ ] `POST /api/v1/documentos/nfce`: rate limiter "api" (100/min por client_id)
- [ ] Todos os 429 retornam header `Retry-After: 60`
- [ ] Todos os endpoints usam `TypedResults` (não `Results`)
- [ ] Erros retornam `ProblemDetails` via `GlobalExceptionHandler`
- [ ] `GET /api/v1/documentos/{id}/status`: sem `xmlAssinado` na response
- [ ] `GET /api/v1/documentos/{id}/xml`: endpoint separado, retorna XML completo
- [ ] `POST /api/v1/clientes/{id}/rotate-secret`: implementado
- [ ] `POST /api/v1/clientes/{id}/rotate-webhook-secret`: implementado
- [ ] Hangfire Dashboard com autenticação básica
- [ ] Health checks em `/health`, `/health/live`, `/health/ready`
- [ ] Migrations aplicadas no startup via `db.Database.MigrateAsync()`
- [ ] `ReconciliacaoJobProcessor` registrado com cron `"*/5 * * * *"` no startup
