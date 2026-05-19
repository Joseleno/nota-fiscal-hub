# Fase 8 — API Endpoints Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the ASP.NET Core Minimal API layer with all missing endpoints, proper health checks, Scalar docs, EF Core startup migrations, and extracted infrastructure files.

**Architecture:** Program.cs already has the bulk of configuration (JWT, rate limiting, tenant/cliente endpoints). This plan adds the 4 document endpoints, rotate-webhook-secret, proper health checks with `HealthCheckOptions`, Scalar reference, EF migrations on startup, and extracts `GlobalExceptionHandler` + `AdminKeySettings` (Api layer version) into dedicated files. No Application or Domain changes needed.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10, Scalar.AspNetCore, AspNetCore.Diagnostics.HealthChecks, xUnit 2.9.3, NSubstitute 5.3.0, Shouldly 4.3.0.

---

## Mapa de Arquivos

| Arquivo | Ação | Task |
|---------|------|------|
| `src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs` | Criar | T1 |
| `src/VisuFiscalHub.Api/Program.cs` | Modificar — remover GlobalExceptionHandler inline, adicionar docs/saúde/migrações/endpoints documento | T1, T2, T3 |
| `src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj` | Modificar — adicionar Scalar.AspNetCore e HealthChecks.Npgsql | T2 |
| `tests/VisuFiscalHub.Tests/Api/DocumentoEndpointsTests.cs` | Criar — testes dos 4 endpoints de documento | T3 |

---

## Context — Existing Code

### Program.cs current state (key facts)
- File: `src/VisuFiscalHub.Api/Program.cs` — 430 lines
- Already has: JWT RS256 (`IssuerSigningKeys` list), `ValidateOnStart`, `ICurrentUserContext` Scoped, rate limiters (auth/api/admin) with `Retry-After: 60`, `GlobalExceptionHandler` inline (lines 391–412), `AdminKeyHelper` static class inline, all tenant endpoints, both rotate-secret for clientes, Hangfire recurring jobs, `public partial class Program { }` at last line
- Missing: document endpoints (4), rotate-webhook-secret, health checks with HealthCheckOptions, Scalar reference, EF migrations on startup, Scalar.AspNetCore package

### Key types used in document endpoints
```csharp
// IssueDocumentCommand (Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs)
public sealed record IssueDocumentCommand : ICommand<Result<IssueDocumentResponse>>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public TipoDocumento Tipo { get; init; } = TipoDocumento.NfCe;
    public IReadOnlyList<ItemDocumentoDto> Itens { get; init; } = [];
    public IReadOnlyList<PagamentoDto> Pagamentos { get; init; } = [];
    public ConsumidorDto? Consumidor { get; init; }
    public int IndPresenca { get; init; } = 1;
}

// IssueDocumentResponse (Application/Common/Models/IssueDocumentResponse.cs)
public sealed record IssueDocumentResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status,
    string? ChaveAcesso,
    string PollUrl,
    DateTimeOffset CreatedAt);

// GetDocumentStatusQuery (Application/Documents/Queries/GetDocumentStatus/GetDocumentStatusQuery.cs)
public sealed record GetDocumentStatusQuery : IQuery<Result<DocumentoStatusResponse>>
{
    public DocumentoFiscalId DocumentoId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
}

// DocumentoStatusResponse (Application/Common/Models/DocumentoStatusResponse.cs)
// NOTE: does NOT have xmlAssinado — separate xml endpoint returns xml
public sealed record DocumentoStatusResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status,
    string? ChaveAcesso,
    string? QrCode,
    string? Protocolo,
    string? MotivoRejeicao,
    DateTimeOffset? AuthorizedAt);
```

### ICurrentUserContext (already Scoped in Program.cs)
```csharp
// src/VisuFiscalHub.Api/Authentication/HttpContextCurrentUserContext.cs
public sealed class HttpContextCurrentUserContext : ICurrentUserContext
{
    public ClienteAppId ClienteAppId { get; }  // from "sub" claim
    public TenantId TenantId { get; }          // from HttpContext.Items["TenantContext"]
}
```

### IDocumentoFiscalRepository — relevant methods
```csharp
Task<DocumentoFiscal?> GetByIdAsync(DocumentoFiscalId id, CancellationToken ct);
```

### DocumentoFiscal — relevant properties
```csharp
public DocumentoFiscalId Id { get; }
public ClienteAppId ClienteAppId { get; }
public string? XmlAssinado { get; }   // populated after authorization
public StatusDocumento Status { get; }
```

### RotateClienteAppWebhookSecretCommand — check if exists
There may be a `RotateClienteAppWebhookSecretCommand` or we create a minimal inline approach. Check `src/VisuFiscalHub.Application/Tenants/Commands/` before implementing.

### AdminKeyHelper (currently inline in Program.cs)
```csharp
static class AdminKeyHelper
{
    private static readonly byte[] _hmacKey = RandomNumberGenerator.GetBytes(32);
    public static bool VerifyAdminKey(string provided, string expected) { ... }
}
```
This stays in Program.cs (it's already there). Do NOT move it.

---

## Task 1: Extract GlobalExceptionHandler to dedicated file + Scalar + EF Migrations

**Files:**
- Create: `src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs`
- Modify: `src/VisuFiscalHub.Api/Program.cs` (remove inline GlobalExceptionHandler, add Scalar, add EF migration startup, add `using Scalar.AspNetCore`)
- Modify: `src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj` (add Scalar.AspNetCore package)

- [ ] **Step 1: Check if Scalar.AspNetCore is already in the csproj**

```powershell
Get-Content src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj
```

Expected: see if `Scalar.AspNetCore` is listed. If yes, skip the package add step.

- [ ] **Step 2: Add Scalar.AspNetCore package if missing**

```powershell
dotnet add src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj package Scalar.AspNetCore
```

Expected: PackageReference added.

- [ ] **Step 3: Create `src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs`**

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace VisuFiscalHub.Api.Middleware;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Erro interno do servidor.",
            Detail = "Ocorreu um erro inesperado. Por favor, tente novamente mais tarde."
        }, cancellationToken);

        return true;
    }
}
```

- [ ] **Step 4: Update Program.cs**

Remove the inline `sealed class GlobalExceptionHandler` block (lines ~391–412 in the current file).

Add `using Scalar.AspNetCore;` and `using VisuFiscalHub.Api.Middleware;` at the top usings block.

Add `using VisuFiscalHub.Infrastructure.Data;` or the correct namespace for `ApplicationDbContext` — verify the namespace by reading `src/VisuFiscalHub.Infrastructure/Data/ApplicationDbContext.cs` or similar.

In the middleware pipeline (after `app.UseMiddleware<TenantValidationMiddleware>()`), add:
```csharp
// Scalar UI — available in all environments (protected in prod via network policy)
app.MapOpenApi();
app.MapScalarApiReference();
```

**Remove** the existing `if (app.Environment.IsDevelopment()) app.MapOpenApi();` block and replace with the always-on version above.

Add EF Core startup migrations block **before** `app.Run()`:
```csharp
// Apply pending migrations on startup — safe for single-instance dev; use Migration Bundles in prod
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}
```

- [ ] **Step 5: Build to confirm no compilation errors**

```powershell
dotnet build src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```
git add src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs src/VisuFiscalHub.Api/Program.cs src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj
git commit -m "refactor: extrair GlobalExceptionHandler; adicionar Scalar e migrações no startup"
```

---

## Task 2: Add rotate-webhook-secret endpoint + proper health checks

**Files:**
- Modify: `src/VisuFiscalHub.Api/Program.cs`

**Context — rotate-webhook-secret:**
First check if `RotateClienteAppWebhookSecretCommand` exists:
```powershell
Get-ChildItem src\VisuFiscalHub.Application\Tenants\Commands -Recurse | Where-Object Name -like "*Webhook*"
```

If the command exists, use it. If not, create a `GetXmlQuery` approach inline. For the webhook rotation endpoint, the pattern must match the existing `rotate-secret` endpoint exactly:

```csharp
clienteApps.MapPost("/{id:guid}/rotate-webhook-secret",
    async (
        Guid id,
        HttpContext ctx,
        IMediator mediator,
        IOptions<AdminKeySettings> adminOptions,
        CancellationToken ct) =>
    {
        var providedKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? string.Empty;
        if (!AdminKeyHelper.VerifyAdminKey(providedKey, adminOptions.Value.Value))
            return Results.Json(new { error = "invalid_key" }, statusCode: StatusCodes.Status401Unauthorized);

        var command = new RotateClienteAppWebhookSecretCommand { ClienteAppId = new ClienteAppId(id) };
        return (await mediator.Send(command, ct)).ToHttpResult(r => Results.Ok(r));
    })
    .RequireRateLimiting("admin");
```

If `RotateClienteAppWebhookSecretCommand` does not exist, check the `ClienteApp` entity for a `RotarWebhookSecret()` method and create the command+handler inline (but prefer using the existing Application pattern).

**Context — health checks:**
Replace the current minimal health endpoint:
```csharp
// CURRENT (remove this):
app.MapGet("/health", () => TypedResults.Ok(new { status = "Healthy" }))
    .RequireRateLimiting("api");
```

With proper HealthChecks registration in services:
```csharp
// In builder.Services section, add:
builder.Services.AddHealthChecks()
    .AddNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        tags: ["ready"]);
```

And map health endpoints properly:
```csharp
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});
```

Check if `AspNetCore.HealthChecks.NpgSql` is already in the csproj:
```powershell
Get-Content src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj
```

If missing, add:
```powershell
dotnet add src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj package AspNetCore.HealthChecks.NpgSql
```

- [ ] **Step 1: Check for existing RotateClienteAppWebhookSecretCommand**

```powershell
Get-ChildItem src\VisuFiscalHub.Application\Tenants\Commands -Recurse | Select-Object Name
```

- [ ] **Step 2: If RotateClienteAppWebhookSecretCommand is missing, read ClienteApp entity to find webhook rotation method**

```powershell
Get-Content src\VisuFiscalHub.Domain\Entities\ClienteApp.cs
```

Look for a method like `RotarWebhookSecret()` or `RenovarWebhookSecret()`. If it exists, create:
- `src/VisuFiscalHub.Application/Tenants/Commands/RotateClienteAppWebhookSecret/RotateClienteAppWebhookSecretCommand.cs`
- `src/VisuFiscalHub.Application/Tenants/Commands/RotateClienteAppWebhookSecret/RotateClienteAppWebhookSecretCommandHandler.cs`

Follow the exact pattern of `RotateClienteAppSecretCommand` and `RotateClienteAppSecretCommandHandler`.

- [ ] **Step 3: Check csproj for HealthChecks.NpgSql**

```powershell
Select-String "HealthChecks" src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj
```

If missing, add:
```powershell
dotnet add src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj package AspNetCore.HealthChecks.NpgSql
```

- [ ] **Step 4: Add `builder.Services.AddHealthChecks()` in Program.cs**

In the builder.Services section (after `builder.Services.AddOpenApi()`), add:
```csharp
builder.Services.AddHealthChecks()
    .AddNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        tags: ["ready"]);
```

- [ ] **Step 5: Replace the minimal health endpoint with proper HealthChecks mapping**

Remove:
```csharp
app.MapGet("/health", () => TypedResults.Ok(new { status = "Healthy" }))
    .RequireRateLimiting("api");
```

Add (in the app.Map* section, after the recurring job registrations):
```csharp
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = hc => hc.Tags.Contains("ready")
});
```

- [ ] **Step 6: Add rotate-webhook-secret endpoint to the `clienteApps` group**

In Program.cs, after the existing `clienteApps.MapPost("/{id:guid}/rotate-secret", ...)` block, add:
```csharp
clienteApps.MapPost("/{id:guid}/rotate-webhook-secret",
    async (
        Guid id,
        HttpContext ctx,
        IMediator mediator,
        IOptions<AdminKeySettings> adminOptions,
        CancellationToken ct) =>
    {
        var providedKey = ctx.Request.Headers["X-Admin-Key"].FirstOrDefault() ?? string.Empty;
        if (!AdminKeyHelper.VerifyAdminKey(providedKey, adminOptions.Value.Value))
            return Results.Json(new { error = "invalid_key" }, statusCode: StatusCodes.Status401Unauthorized);

        var command = new RotateClienteAppWebhookSecretCommand { ClienteAppId = new ClienteAppId(id) };
        return (await mediator.Send(command, ct)).ToHttpResult(r => Results.Ok(r));
    })
    .RequireRateLimiting("admin");
```

Add the missing using at the top of Program.cs:
```csharp
using VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppWebhookSecret;
```

- [ ] **Step 7: Build**

```powershell
dotnet build src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj
```

Expected: 0 errors.

- [ ] **Step 8: Add 50KB limit to `PUT /{id:guid}/certificado` in Program.cs**

The current `tenants.MapPut("/{id:guid}/certificado", ...)` block does not enforce a body size limit. Add an endpoint filter immediately after the `MapPut` call:

```csharp
tenants.MapPut("/{id:guid}/certificado",
    async (
        Guid id,
        UpdateTenantCertificateCommand command,
        ICurrentUserContext userCtx,
        IMediator mediator,
        CancellationToken ct) =>
    {
        var cmd = command with
        {
            TenantId = new TenantId(id),
            ClienteAppId = userCtx.ClienteAppId
        };
        return (await mediator.Send(cmd, ct)).ToHttpResult();
    })
    .RequireRateLimiting("api")
    .AddEndpointFilter(async (ctx, next) =>
    {
        if (ctx.HttpContext.Request.ContentLength > 50 * 1024)
            return TypedResults.StatusCode(StatusCodes.Status413RequestEntityTooLarge);
        return await next(ctx);
    });
```

Replace the existing `tenants.MapPut("/{id:guid}/certificado", ...)` block with this version (adds `.AddEndpointFilter(...)`).

- [ ] **Step 9: Build**

```powershell
dotnet build src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj
```

Expected: 0 errors.

- [ ] **Step 10: Commit**

```
git add src/VisuFiscalHub.Api/Program.cs src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj
git commit -m "feat: adicionar rotate-webhook-secret, health checks e limite 50KB no upload de certificado"
```

---

## Task 3: Add document endpoints (nfce, status, xml, cancelar)

**Files:**
- Modify: `src/VisuFiscalHub.Api/Program.cs`

**Context — all 4 document endpoints:**

```
POST /api/v1/documentos/nfce
  Auth: Bearer JWT (RequireAuthorization)
  Rate: "api"
  Headers: X-Tenant-Id (required for TenantValidationMiddleware), X-Idempotency-Key
  Body: IssueNfceRequest { itens[], pagamentos[], consumidor?, indPresenca? }
  Response 202: IssueDocumentResponse
  Response 409: idempotency key already used (final status)
  Response 422: domain validation errors

GET /api/v1/documentos/{id}/status
  Auth: Bearer JWT
  Response 200: DocumentoStatusResponse (NO xmlAssinado field — it's not in the type)
  Response 403: documento does not belong to ClienteApp
  Response 404: not found

GET /api/v1/documentos/{id}/xml
  Auth: Bearer JWT
  Response 200: { "xmlAssinado": "..." }
  Response 403: forbidden
  Response 404: not found or xml not yet available

POST /api/v1/documentos/{id}/cancelar
  Auth: Bearer JWT
  Response 501: Not Implemented (cancellation is Fase 9)
```

**IssueNfceRequest — request body type (define inline in Program.cs):**
```csharp
// Add near the bottom of Program.cs with TokenRequest
internal sealed record IssueNfceRequest(
    IReadOnlyList<ItemDocumentoDto> Itens,
    IReadOnlyList<PagamentoDto> Pagamentos,
    ConsumidorDto? Consumidor,
    int IndPresenca = 1);
```

**IDocumentoFiscalRepository.GetByIdAsync signature:**
```csharp
Task<DocumentoFiscal?> GetByIdAsync(DocumentoFiscalId id, CancellationToken ct);
```

This is injected directly in the endpoint lambda for the XML endpoint (to avoid creating a new query just for XML retrieval — the status query handler already uses it, but doesn't return XmlAssinado).

- [ ] **Step 1: Read the IDocumentoFiscalRepository interface**

```powershell
Get-Content src\VisuFiscalHub.Domain\Interfaces\IDocumentoFiscalRepository.cs
```

Confirm `GetByIdAsync(DocumentoFiscalId id, CancellationToken ct)` exists.

- [ ] **Step 2: Add `IssueNfceRequest` record to Program.cs**

At the bottom of Program.cs (before `public partial class Program { }`), add:
```csharp
internal sealed record IssueNfceRequest(
    IReadOnlyList<ItemDocumentoDto> Itens,
    IReadOnlyList<PagamentoDto> Pagamentos,
    ConsumidorDto? Consumidor,
    int IndPresenca = 1);
```

Add missing using at the top:
```csharp
using VisuFiscalHub.Application.Documents.Commands.IssueDocument;
using VisuFiscalHub.Application.Documents.Queries.GetDocumentStatus;
```

(These may already exist — check Program.cs usings.)

- [ ] **Step 3: Add document endpoints group in Program.cs**

After the `tenants` group endpoints block and before the Hangfire dashboard section, add:

```csharp
// ── Documentos ────────────────────────────────────────────────────────────
var documentos = app.MapGroup("/api/v1/documentos").RequireAuthorization();

documentos.MapPost("/nfce",
    async (
        IssueNfceRequest request,
        HttpContext ctx,
        ICurrentUserContext userCtx,
        IMediator mediator,
        CancellationToken ct) =>
    {
        var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.Problem(
                detail: "DocumentoFiscal.IdempotencyKeyInvalida",
                title: "X-Idempotency-Key é obrigatório.",
                statusCode: StatusCodes.Status422UnprocessableEntity);

        var command = new IssueDocumentCommand
        {
            TenantId = userCtx.TenantId,
            ClienteAppId = userCtx.ClienteAppId,
            IdempotencyKey = idempotencyKey,
            Tipo = TipoDocumento.NfCe,
            Itens = request.Itens,
            Pagamentos = request.Pagamentos,
            Consumidor = request.Consumidor,
            IndPresenca = request.IndPresenca
        };

        return (await mediator.Send(command, ct))
            .ToHttpResult(r => Results.Accepted($"/api/v1/documentos/{r.DocumentoId.Value}/status", r));
    })
    .RequireRateLimiting("api");

documentos.MapGet("/{id:guid}/status",
    async (
        Guid id,
        ICurrentUserContext userCtx,
        IMediator mediator,
        CancellationToken ct) =>
    {
        var query = new GetDocumentStatusQuery
        {
            DocumentoId = new DocumentoFiscalId(id),
            ClienteAppId = userCtx.ClienteAppId
        };
        return (await mediator.Send(query, ct)).ToHttpResult();
    });

documentos.MapGet("/{id:guid}/xml",
    async (
        Guid id,
        ICurrentUserContext userCtx,
        IDocumentoFiscalRepository documentoRepo,
        CancellationToken ct) =>
    {
        var documento = await documentoRepo.GetByIdAsync(new DocumentoFiscalId(id), ct);

        if (documento is null)
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Documento não encontrado.",
                Detail = "DocumentoFiscal.NaoEncontrado"
            }) as IResult;

        if (documento.ClienteAppId != userCtx.ClienteAppId)
            return TypedResults.Problem(
                detail: "Tenant.NaoPertenceAoClienteApp",
                title: "Documento não pertence a este ClienteApp.",
                statusCode: StatusCodes.Status403Forbidden);

        if (string.IsNullOrEmpty(documento.XmlAssinado))
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "XML não disponível.",
                Detail = "Documento ainda não foi autorizado pela SEFAZ."
            });

        return TypedResults.Ok(new { xmlAssinado = documento.XmlAssinado });
    });

documentos.MapPost("/{id:guid}/cancelar",
    (Guid id) => TypedResults.StatusCode(StatusCodes.Status501NotImplemented));
```

**Important:** Add these usings at the top of Program.cs if not present:
```csharp
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Interfaces;
```

Also need:
```csharp
using VisuFiscalHub.Domain.Identifiers;
```
(already present — check).

- [ ] **Step 4: Build**

```powershell
dotnet build src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj
```

Expected: 0 errors.

- [ ] **Step 5: Run existing tests to confirm no regression**

```powershell
dotnet test tests\VisuFiscalHub.Tests --no-build
```

Expected: 454 passed, 0 failed.

- [ ] **Step 6: Commit**

```
git add src/VisuFiscalHub.Api/Program.cs
git commit -m "feat: adicionar endpoints de documento NFC-e (emissão, status, xml, cancelar)"
```

---

## Task 4: Add unit tests for document endpoints

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Api/DocumentoEndpointsTests.cs`

**Context — what to test:**
The document endpoints are defined inline in Program.cs as lambdas. Unit-testing lambdas is awkward; instead, test the `IssueDocumentCommand`, `GetDocumentStatusQuery`, and the XML endpoint behavior by verifying that the `ToHttpResult` extensions map correctly and that the command handler returns the right responses.

Since `WebApplicationFactory<Program>` requires `public partial class Program { }` (already present at end of Program.cs), we CAN write integration tests. However, these need a running database which we don't have in CI. Instead, test the non-trivial logic:

1. `IssueNfceRequest` missing `X-Idempotency-Key` → 422
2. `DocumentoStatusResponse` type does NOT have `xmlAssinado` property (compile-time check)
3. The `IssueDocumentResponse` `PollUrl` format

**Test file:**

```csharp
using Shouldly;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Api;

/// <summary>
/// Verifica contratos de tipo dos endpoints de documento — garante que documentoStatusResponse
/// nunca exponha xmlAssinado e que IssueDocumentResponse contenha PollUrl.
/// Testes compilam apenas se os tipos estão corretos: qualquer adição de xmlAssinado ao
/// DocumentoStatusResponse causará erro de compilação neste arquivo.
/// </summary>
public class DocumentoEndpointsContractTests
{
    // ── DocumentoStatusResponse não tem xmlAssinado ────────────────────────────

    [Fact]
    public void DocumentoStatusResponse_NaoExpoe_XmlAssinado()
    {
        var tipo = typeof(DocumentoStatusResponse);
        var hasXml = tipo.GetProperties()
            .Any(p => p.Name.Contains("Xml", StringComparison.OrdinalIgnoreCase));

        hasXml.ShouldBeFalse(
            "DocumentoStatusResponse não deve expor XML — use GET /documentos/{id}/xml");
    }

    // ── IssueDocumentResponse tem PollUrl ─────────────────────────────────────

    [Fact]
    public void IssueDocumentResponse_Contem_PollUrl()
    {
        var id = DocumentoFiscalId.New();
        var response = new IssueDocumentResponse(
            DocumentoId: id,
            Status: StatusDocumento.Enfileirado,
            ChaveAcesso: null,
            PollUrl: $"/api/v1/documentos/{id.Value}/status",
            CreatedAt: DateTimeOffset.UtcNow);

        response.PollUrl.ShouldStartWith("/api/v1/documentos/");
        response.PollUrl.ShouldEndWith("/status");
    }

    // ── DocumentoStatusResponse contém todos os campos esperados ─────────────

    [Fact]
    public void DocumentoStatusResponse_ContemCamposEsperados()
    {
        var id = DocumentoFiscalId.New();
        var response = new DocumentoStatusResponse(
            DocumentoId: id,
            Status: StatusDocumento.Autorizado,
            ChaveAcesso: "35260112345678000195650010000000011234567899",
            QrCode: "https://www.sefaz.rs.gov.br/NFCE/...",
            Protocolo: "315260000000001",
            MotivoRejeicao: null,
            AuthorizedAt: DateTimeOffset.UtcNow);

        response.DocumentoId.ShouldBe(id);
        response.Status.ShouldBe(StatusDocumento.Autorizado);
        response.Protocolo.ShouldBe("315260000000001");
        response.MotivoRejeicao.ShouldBeNull();
    }
}
```

- [ ] **Step 1: Create the test file**

Create `tests/VisuFiscalHub.Tests/Api/DocumentoEndpointsContractTests.cs` with the content above.

- [ ] **Step 2: Run the new tests**

```powershell
dotnet test tests\VisuFiscalHub.Tests --filter "FullyQualifiedName~DocumentoEndpointsContractTests"
```

Expected: 3 passed, 0 failed.

- [ ] **Step 3: Run all tests**

```powershell
dotnet test tests\VisuFiscalHub.Tests
```

Expected: 457 passed, 0 failed.

- [ ] **Step 4: Commit**

```
git add tests/VisuFiscalHub.Tests/Api/DocumentoEndpointsContractTests.cs
git commit -m "test: verificar contratos de tipo dos endpoints de documento"
```

---

## Checklist de Conclusão

- [ ] `GlobalExceptionHandler` em arquivo dedicado `Middleware/GlobalExceptionHandler.cs`
- [ ] `Scalar.AspNetCore` adicionado ao csproj + `app.MapScalarApiReference()` no pipeline
- [ ] EF Core `db.Database.MigrateAsync()` no startup
- [ ] `POST /api/v1/clientes/{id}/rotate-webhook-secret` implementado com `AdminKeyHelper`
- [ ] Health checks em `/health`, `/health/live`, `/health/ready` com `HealthCheckOptions`
- [ ] `POST /api/v1/documentos/nfce` retorna 202 com `IssueDocumentResponse`
- [ ] `GET /api/v1/documentos/{id}/status` retorna `DocumentoStatusResponse` sem `xmlAssinado`
- [ ] `GET /api/v1/documentos/{id}/xml` retorna `{ xmlAssinado }` em endpoint separado
- [ ] `POST /api/v1/documentos/{id}/cancelar` retorna 501
- [ ] Todos os testes passam (457 total)
