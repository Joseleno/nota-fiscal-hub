# Fase 13 — Observabilidade e Logging: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tornar o VisuFiscalHub observável em produção via CorrelationId ponta-a-ponta, Serilog estruturado com Seq, e DeliveryAttempt com ElapsedMs em todos os jobs.

**Architecture:** CorrelationIdMiddleware propaga X-Correlation-Id via HttpContext.Items + LogContext; CorrelationIdJobFilter usa BackgroundJob.Id nos jobs Hangfire; ElapsedMs é adicionado diretamente em SefazRetorno/SefazConsultaRetorno sem novo tipo genérico; DeliveryAttempt é adicionado do zero em FiscalDocumentProcessingJob e ReconciliacaoJobProcessor.

**Tech Stack:** ASP.NET Core 10, Serilog 10, Hangfire 1.8, EF Core 10 (PostgreSQL), xUnit + NSubstitute + Shouldly.

---

## File Map

**Criar:**
- `src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs` — middleware X-Correlation-Id
- `src/VisuFiscalHub.Application/Common/Interfaces/ICorrelationContext.cs` — interface Scoped
- `src/VisuFiscalHub.Infrastructure/Services/HttpCorrelationContext.cs` — implementação via IHttpContextAccessor
- `src/VisuFiscalHub.Infrastructure/Jobs/CorrelationIdJobFilter.cs` — IServerFilter Hangfire
- `src/VisuFiscalHub.Api/appsettings.Docker.json` — config Serilog para ambiente Docker
- `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/<timestamp>_AddCorrelationIdToOutboxMessages.cs` — gerada via dotnet ef

**Modificar:**
- `src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs` — adicionar `ElapsedMs` em `SefazRetorno` e `SefazConsultaRetorno`
- `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs` — adicionar Stopwatch; popular ElapsedMs
- `src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs` — remover `Retry = 3`
- `src/VisuFiscalHub.Infrastructure/Persistence/OutboxMessage.cs` — adicionar `CorrelationId string?`
- `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs` — mapear coluna
- `src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs` — LogContext + DeliveryAttempt
- `src/VisuFiscalHub.Infrastructure/Jobs/ReconciliacaoJobProcessor.cs` — LogContext + DeliveryAttempt
- `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs` — LogContext TenantId/DocumentoId
- `src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs` — LogContext CorrelationId por mensagem
- `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs` — registrar ICorrelationContext + CorrelationIdJobFilter
- `src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs` — CorrelationId em ProblemDetails.Extensions
- `src/VisuFiscalHub.Api/Program.cs` — registrar middleware; atualizar UseSerilog; remover WriteTo.Console hardcoded
- `src/VisuFiscalHub.Api/appsettings.json` — substituir seção Logging por Serilog
- `src/VisuFiscalHub.Api/appsettings.Development.json` — adicionar seção Serilog com Seq localhost
- `infra/docker-compose.override.yml` — adicionar serviço seq; montar appsettings.Docker.json
- `src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj` — adicionar 3 packages Serilog

**Testes a atualizar** (quebrarão com ElapsedMs):
- `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs`
- `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/ReconciliacaoJobProcessorTests.cs`
- `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs`

---

## Task 1: ElapsedMs em SefazRetorno/SefazConsultaRetorno + Stopwatch no SefazClient

**Files:**
- Modify: `src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs`
- Modify: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs`
- Modify: `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs`
- Modify: `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/ReconciliacaoJobProcessorTests.cs`

- [ ] **Step 1: Adicionar `ElapsedMs` em `SefazRetorno` e `SefazConsultaRetorno`**

Em `src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs`, alterar os dois records:

```csharp
public sealed record SefazRetorno(
    bool Autorizado,
    string CStat,
    string XMotivo,
    string? NProt,
    string? XmlAutorizado,
    string? QrCodeUrl,
    long ElapsedMs);

public sealed record SefazConsultaRetorno(
    bool Encontrado,
    bool Autorizado,
    string CStat,
    string? NProt,
    string? XmlProtocolo,
    long ElapsedMs);
```

- [ ] **Step 2: Verificar que o projeto não compila (confirmando quebra esperada)**

```
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj
```

Expected: vários erros de compilação em `FakeSefazClient.cs`, `NfceProcessingJobTests.cs`, `ReconciliacaoJobProcessorTests.cs`, e `SefazClient.cs` — todos instanciam os records sem `ElapsedMs`.

- [ ] **Step 3: Adicionar Stopwatch em `SefazClient.SubmeterAutorizacaoAsync`**

Em `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs`, adicionar medição em `SubmeterAutorizacaoAsync`. Inserir o `Stopwatch` antes da chamada HTTP e usar `sw.ElapsedMilliseconds` ao construir o retorno.

Localizar a linha `string soapResponse;` (por volta da linha 110) e adicionar antes dela:

```csharp
var sw = System.Diagnostics.Stopwatch.StartNew();
```

Na linha `var retorno = SefazRetornoParser.Parse(soapResponse);` (por volta da linha 123), após o parse, ajustar:

```csharp
sw.Stop();
var retorno = SefazRetornoParser.Parse(soapResponse);
if (retorno.IsFailure) return retorno;
return Result.Success(retorno.Value with { QrCodeUrl = qrCodeUrl, ElapsedMs = sw.ElapsedMilliseconds });
```

- [ ] **Step 4: Adicionar Stopwatch em `SefazClient.ConsultarNfeAsync`**

No mesmo arquivo, em `ConsultarNfeAsync`, adicionar `Stopwatch` antes da chamada HTTP. Localizar `string soapResponse;` na segunda ocorrência (por volta da linha 159) e inserir antes:

```csharp
var swConsulta = System.Diagnostics.Stopwatch.StartNew();
```

E logo antes de `return ParseConsultaResponse(soapResponse);`:

```csharp
swConsulta.Stop();
var parseResult = ParseConsultaResponse(soapResponse);
if (parseResult.IsFailure) return parseResult;
return Result.Success(parseResult.Value with { ElapsedMs = swConsulta.ElapsedMilliseconds });
```

Remover a linha `return ParseConsultaResponse(soapResponse);` original.

- [ ] **Step 5: Atualizar `ParseConsultaResponse` para retornar `ElapsedMs: 0` temporariamente**

Em `ParseConsultaResponse` (método privado estático), o record `SefazConsultaRetorno` agora exige `ElapsedMs`. Adicionar `ElapsedMs: 0L` na construção do record (o valor real será sobrescrito pelo `with` no passo 4):

```csharp
return Result.Success(new SefazConsultaRetorno(
    Encontrado: encontrado,
    Autorizado: autorizado,
    CStat:      cStat,
    NProt:      nProt,
    XmlProtocolo: xmlProt,
    ElapsedMs:  0L));
```

- [ ] **Step 6: Corrigir `FakeSefazClient` para compilar**

Em `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs`, adicionar `ElapsedMs: 0L` em todas as instanciações de `SefazRetorno` e `SefazConsultaRetorno`. São 6 ocorrências de `SefazRetorno` e 3 de `SefazConsultaRetorno`:

```csharp
// Exemplo — padrão a aplicar em todas as ocorrências:
new SefazRetorno(
    Autorizado: true,
    CStat: "100",
    XMotivo: "Autorizado o uso da NF-e",
    NProt: "135260000000001",
    XmlAutorizado: "<protNFe/>",
    QrCodeUrl: "https://...",
    ElapsedMs: 0L)

new SefazConsultaRetorno(
    Encontrado: true,
    Autorizado: true,
    CStat: "100",
    NProt: "135260000000001",
    XmlProtocolo: null,
    ElapsedMs: 0L)
```

- [ ] **Step 7: Corrigir `NfceProcessingJobTests` — adicionar `ElapsedMs: 0L` nos stubs**

Em `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs`, todos os `.Returns(Result.Success(new SefazRetorno(...)))` e `.Returns(Result.Success(new SefazConsultaRetorno(...)))` precisam de `ElapsedMs: 0L`. Buscar todas as ocorrências de `new SefazRetorno(` e `new SefazConsultaRetorno(` no arquivo e adicionar o campo.

- [ ] **Step 8: Corrigir `ReconciliacaoJobProcessorTests` — adicionar `ElapsedMs: 0L` nos stubs**

Mesmo processo em `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/ReconciliacaoJobProcessorTests.cs`.

- [ ] **Step 9: Verificar que o projeto compila sem erros**

```
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Executar os testes afetados**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~NfceProcessingJobTests|FullyQualifiedName~ReconciliacaoJobProcessorTests" --no-build
```

Expected: todos os testes passam.

- [ ] **Step 11: Commit**

```
git add src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs
git add tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs
git add tests/VisuFiscalHub.Tests/Infrastructure/Jobs/ReconciliacaoJobProcessorTests.cs
git commit -m "feat: adicionar ElapsedMs em SefazRetorno/SefazConsultaRetorno e Stopwatch no SefazClient"
```

---

## Task 2: TipoTentativa — remover Retry sem renumerar

**Files:**
- Modify: `src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs`

- [ ] **Step 1: Escrever teste que confirma que `Retry` não existe no enum**

Em `tests/VisuFiscalHub.Tests/Domain/TipoTentativaTests.cs` (criar arquivo):

```csharp
using Shouldly;
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Tests.Domain;

public class TipoTentativaTests
{
    [Fact]
    public void TipoTentativa_NaoDeveTerRetry()
    {
        var names = Enum.GetNames<TipoTentativa>();
        names.ShouldNotContain("Retry");
    }

    [Fact]
    public void TipoTentativa_Cancelamento_DeveSerValor4()
    {
        ((int)TipoTentativa.Cancelamento).ShouldBe(4);
    }

    [Fact]
    public void TipoTentativa_Envio_DeveSerValor1()
    {
        ((int)TipoTentativa.Envio).ShouldBe(1);
    }

    [Fact]
    public void TipoTentativa_Consulta_DeveSerValor2()
    {
        ((int)TipoTentativa.Consulta).ShouldBe(2);
    }
}
```

- [ ] **Step 2: Executar testes para confirmar que o primeiro falha**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~TipoTentativaTests" --no-build
```

Expected: `TipoTentativa_NaoDeveTerRetry` FAIL — `Retry` ainda existe.

- [ ] **Step 3: Remover `Retry = 3` do enum**

Em `src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs`:

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum TipoTentativa
{
    Envio = 1,
    Consulta = 2,
    // 3 removido (era Retry — nunca persistido no banco)
    Cancelamento = 4
}
```

- [ ] **Step 4: Executar os testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~TipoTentativaTests"
```

Expected: 4 testes passam.

- [ ] **Step 5: Commit**

```
git add src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs
git add tests/VisuFiscalHub.Tests/Domain/TipoTentativaTests.cs
git commit -m "feat: remover TipoTentativa.Retry (nunca usado/persistido); manter Cancelamento=4"
```

---

## Task 3: CorrelationIdMiddleware + ICorrelationContext

**Files:**
- Create: `src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs`
- Create: `src/VisuFiscalHub.Application/Common/Interfaces/ICorrelationContext.cs`
- Create: `src/VisuFiscalHub.Infrastructure/Services/HttpCorrelationContext.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`
- Modify: `src/VisuFiscalHub.Api/Program.cs`

- [ ] **Step 1: Escrever testes para `CorrelationIdMiddleware`**

Criar `tests/VisuFiscalHub.Tests/Api/CorrelationIdMiddlewareTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Shouldly;
using VisuFiscalHub.Api.Middleware;

namespace VisuFiscalHub.Tests.Api;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Invoke_SemHeaderDeEntrada_GeraCorrelationIdNaResposta()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.Headers["X-Correlation-Id"].ToString().ShouldNotBeEmpty();
        context.Items["CorrelationId"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Invoke_ComHeaderDeEntrada_ReutilizaCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "meu-id-12345";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.Headers["X-Correlation-Id"].ToString().ShouldBe("meu-id-12345");
        context.Items["CorrelationId"].ShouldBe("meu-id-12345");
    }

    [Fact]
    public async Task Invoke_HeaderMaiorQue128Chars_Trunca()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = new string('x', 200);
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        var id = context.Response.Headers["X-Correlation-Id"].ToString();
        id.Length.ShouldBe(128);
    }
}
```

- [ ] **Step 2: Executar testes para confirmar que falham**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CorrelationIdMiddlewareTests"
```

Expected: FAIL — `CorrelationIdMiddleware` não existe.

- [ ] **Step 3: Criar `CorrelationIdMiddleware`**

Criar `src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs`:

```csharp
using Serilog.Context;

namespace VisuFiscalHub.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate _next)
    {
        var raw = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        var correlationId = raw is { Length: > 0 }
            ? raw[..Math.Min(raw.Length, 128)]
            : Guid.NewGuid().ToString("N");

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        context.Items["CorrelationId"] = correlationId;

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        await next(context);
    }
}
```

Nota: o construtor recebe `RequestDelegate next` (padrão de middleware convencional), mas `InvokeAsync` recebe `RequestDelegate _next` porque o pipeline injetará o next correto em tempo de execução. O construtor `next` não é usado — simplificar para:

```csharp
using Serilog.Context;

namespace VisuFiscalHub.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var raw = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        var correlationId = raw is { Length: > 0 }
            ? raw[..Math.Min(raw.Length, 128)]
            : Guid.NewGuid().ToString("N");

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        context.Items["CorrelationId"] = correlationId;

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        await next(context);
    }
}
```

- [ ] **Step 4: Criar `ICorrelationContext`**

Criar `src/VisuFiscalHub.Application/Common/Interfaces/ICorrelationContext.cs`:

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
```

- [ ] **Step 5: Criar `HttpCorrelationContext`**

Criar `src/VisuFiscalHub.Infrastructure/Services/HttpCorrelationContext.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using VisuFiscalHub.Application.Common.Interfaces;

namespace VisuFiscalHub.Infrastructure.Services;

internal sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string? CorrelationId =>
        accessor.HttpContext?.Items["CorrelationId"] as string;
}
```

- [ ] **Step 6: Registrar `ICorrelationContext` e `IHttpContextAccessor` em `DependencyInjection.cs`**

Em `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`, adicionar após `services.AddSingleton(TimeProvider.System);`:

```csharp
services.AddHttpContextAccessor();
services.AddScoped<ICorrelationContext, HttpCorrelationContext>();
```

- [ ] **Step 7: Registrar middleware e reordenar pipeline em `Program.cs`**

Em `src/VisuFiscalHub.Api/Program.cs`, localizar o bloco de middleware (por volta da linha 230) e inserir `UseMiddleware<CorrelationIdMiddleware>()` após `UseExceptionHandler` e antes de `UseSerilogRequestLogging`:

```csharp
app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();   // ← adicionar aqui
app.Use(async (ctx, next) => { /* security headers */ await next(); });
app.UseForwardedHeaders();
app.UseHsts();
app.UseHttpsRedirection();
app.UseSerilogRequestLogging();
```

Verificar a posição exata no arquivo — o middleware deve vir antes de `UseSerilogRequestLogging()` de forma que o log de request capture `CorrelationId`.

- [ ] **Step 8: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CorrelationIdMiddlewareTests"
```

Expected: 3 testes passam.

- [ ] **Step 9: Executar suite completa para verificar regressões**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```

Expected: todos os testes passam.

- [ ] **Step 10: Commit**

```
git add src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs
git add src/VisuFiscalHub.Application/Common/Interfaces/ICorrelationContext.cs
git add src/VisuFiscalHub.Infrastructure/Services/HttpCorrelationContext.cs
git add src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
git add src/VisuFiscalHub.Api/Program.cs
git add tests/VisuFiscalHub.Tests/Api/CorrelationIdMiddlewareTests.cs
git commit -m "feat: CorrelationIdMiddleware + ICorrelationContext para propagação ponta-a-ponta"
```

---

## Task 4: GlobalExceptionHandler com CorrelationId

**Files:**
- Modify: `src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs`

- [ ] **Step 1: Escrever teste para `GlobalExceptionHandler`**

Criar `tests/VisuFiscalHub.Tests/Api/GlobalExceptionHandlerTests.cs`:

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Api.Middleware;

namespace VisuFiscalHub.Tests.Api;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ComCorrelationIdEmItems_IncluiNoExtensions()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        problemDetailsService
            .TryWriteAsync(Arg.Any<ProblemDetailsContext>())
            .Returns(true);

        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Items["CorrelationId"] = "abc123";

        ProblemDetailsContext? capturedContext = null;
        await problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(c => capturedContext = c))
            .Returns(true);

        await handler.TryHandleAsync(context, new Exception("test"), CancellationToken.None);

        capturedContext.ShouldNotBeNull();
        capturedContext!.ProblemDetails.Extensions["correlationId"].ShouldBe("abc123");
    }

    [Fact]
    public async Task TryHandleAsync_SemCorrelationId_ExtensionEhNull()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);

        var context = new DefaultHttpContext();

        ProblemDetailsContext? capturedContext = null;
        await problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(c => capturedContext = c))
            .Returns(true);

        await handler.TryHandleAsync(context, new Exception("test"), CancellationToken.None);

        capturedContext.ShouldNotBeNull();
        capturedContext!.ProblemDetails.Extensions.ContainsKey("correlationId").ShouldBeTrue();
        capturedContext!.ProblemDetails.Extensions["correlationId"].ShouldBeNull();
    }
}
```

- [ ] **Step 2: Executar teste para confirmar que falha**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~GlobalExceptionHandlerTests"
```

Expected: FAIL — `Extensions` não tem `correlationId`.

- [ ] **Step 3: Atualizar `GlobalExceptionHandler`**

Substituir o conteúdo de `src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs`:

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace VisuFiscalHub.Api.Middleware;

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var correlationId = httpContext.Items["CorrelationId"] as string;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erro interno do servidor.",
                Detail = "Ocorreu um erro inesperado. Por favor, tente novamente mais tarde.",
                Extensions = { ["correlationId"] = correlationId }
            }
        });
    }
}
```

- [ ] **Step 4: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~GlobalExceptionHandlerTests"
```

Expected: 2 testes passam.

- [ ] **Step 5: Commit**

```
git add src/VisuFiscalHub.Api/Middleware/GlobalExceptionHandler.cs
git add tests/VisuFiscalHub.Tests/Api/GlobalExceptionHandlerTests.cs
git commit -m "feat: incluir CorrelationId em ProblemDetails 500 via GlobalExceptionHandler"
```

---

## Task 5: CorrelationIdJobFilter para Hangfire

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Jobs/CorrelationIdJobFilter.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Escrever teste para `CorrelationIdJobFilter`**

Criar `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/CorrelationIdJobFilterTests.cs`:

```csharp
using Hangfire.Server;
using Hangfire.Storage;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Infrastructure.Jobs;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

public class CorrelationIdJobFilterTests
{
    [Fact]
    public void OnPerforming_ArmazenaDisposableEmItems()
    {
        var filter = new CorrelationIdJobFilter();
        var context = CreatePerformingContext("job-123");

        filter.OnPerforming(context);

        // Deve haver exatamente um IDisposable armazenado nos Items
        context.Items.Values.OfType<IDisposable>().Count().ShouldBe(1);
    }

    [Fact]
    public void OnPerformed_DescartaDisposable()
    {
        var filter = new CorrelationIdJobFilter();
        var performingContext = CreatePerformingContext("job-456");
        filter.OnPerforming(performingContext);

        var disposable = Substitute.For<IDisposable>();
        // Substituir o IDisposable real por um mock para verificar Dispose
        var key = performingContext.Items.Keys.First();
        performingContext.Items[key] = disposable;

        var performedContext = CreatePerformedContext(performingContext);
        filter.OnPerformed(performedContext);

        disposable.Received(1).Dispose();
    }

    private static PerformingContext CreatePerformingContext(string jobId)
    {
        var connection = Substitute.For<IStorageConnection>();
        var backgroundJob = new BackgroundJob(jobId, new Hangfire.Common.Job(
            typeof(object), typeof(object).GetMethod("ToString")!), DateTime.UtcNow);
        return new PerformingContext(new PerformContext(connection, backgroundJob, null, null));
    }

    private static PerformedContext CreatePerformedContext(PerformingContext performing)
    {
        var connection = Substitute.For<IStorageConnection>();
        var backgroundJob = performing.BackgroundJob;
        var performContext = new PerformContext(connection, backgroundJob, null, null);
        return new PerformedContext(performContext, null, false, null);
    }
}
```

**Nota:** Os testes de `CorrelationIdJobFilter` são difíceis de escrever corretamente com as classes concretas do Hangfire (que têm construtores internos). Se os construtores de `PerformingContext`/`PerformedContext` não forem acessíveis, escrever um teste de smoke simples que apenas instancia o filtro e confirma que ele implementa `IServerFilter`:

```csharp
using Hangfire.Server;
using Shouldly;
using VisuFiscalHub.Infrastructure.Jobs;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

public class CorrelationIdJobFilterTests
{
    [Fact]
    public void CorrelationIdJobFilter_ImplementaIServerFilter()
    {
        var filter = new CorrelationIdJobFilter();
        filter.ShouldBeAssignableTo<IServerFilter>();
    }
}
```

- [ ] **Step 2: Executar teste para confirmar que falha**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CorrelationIdJobFilterTests"
```

Expected: FAIL — `CorrelationIdJobFilter` não existe.

- [ ] **Step 3: Criar `CorrelationIdJobFilter`**

Criar `src/VisuFiscalHub.Infrastructure/Jobs/CorrelationIdJobFilter.cs`:

```csharp
using Hangfire.Server;
using Serilog.Context;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class CorrelationIdJobFilter : IServerFilter
{
    private static readonly object _scopeKey = new();

    public void OnPerforming(PerformingContext context)
    {
        var correlationId = context.BackgroundJob.Id;
        var scope = LogContext.PushProperty("CorrelationId", correlationId);
        context.Items[_scopeKey] = scope;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(_scopeKey, out var scope))
            ((IDisposable)scope).Dispose();
    }
}
```

- [ ] **Step 4: Registrar o filtro em `DependencyInjection.cs`**

Em `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`, localizar o bloco `services.AddHangfire(config =>` e adicionar `config.UseFilter(new CorrelationIdJobFilter())` após `UseRecommendedSerializerSettings()`:

```csharp
services.AddHangfire(config =>
{
    config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseFilter(new CorrelationIdJobFilter());   // ← adicionar
    if (!isTest)
        config.UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(connectionString));
});
```

- [ ] **Step 5: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CorrelationIdJobFilterTests"
```

Expected: passa.

- [ ] **Step 6: Executar suite completa**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```

Expected: todos passam.

- [ ] **Step 7: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Jobs/CorrelationIdJobFilter.cs
git add src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
git add tests/VisuFiscalHub.Tests/Infrastructure/Jobs/CorrelationIdJobFilterTests.cs
git commit -m "feat: CorrelationIdJobFilter (Hangfire IServerFilter) usando BackgroundJob.Id"
```

---

## Task 6: OutboxMessage.CorrelationId + migration EF Core

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/OutboxMessage.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`
- Create: migration (gerada via `dotnet ef`)
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs`

- [ ] **Step 1: Adicionar `CorrelationId` em `OutboxMessage`**

Em `src/VisuFiscalHub.Infrastructure/Persistence/OutboxMessage.cs`:

```csharp
namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? CorrelationId { get; init; }
}
```

- [ ] **Step 2: Mapear a coluna em `OutboxMessageConfiguration`**

Em `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`, adicionar antes da linha do índice:

```csharp
builder.Property(o => o.CorrelationId)
    .HasColumnName("correlation_id")
    .HasMaxLength(128);
```

- [ ] **Step 3: Gerar a migration**

Na raiz do repositório:

```
dotnet ef migrations add AddCorrelationIdToOutboxMessages --project src/VisuFiscalHub.Infrastructure --startup-project src/VisuFiscalHub.Api
```

Expected: arquivo `<timestamp>_AddCorrelationIdToOutboxMessages.cs` criado em `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 4: Verificar o conteúdo da migration gerada**

Abrir o arquivo gerado e confirmar que ele contém:

```csharp
migrationBuilder.AddColumn<string>(
    name: "correlation_id",
    table: "outbox_messages",
    type: "character varying(128)",
    maxLength: 128,
    nullable: true);
```

Se não estiver correto, revisar os passos anteriores.

- [ ] **Step 5: Adicionar `LogContext.PushProperty` por mensagem no `OutboxRelayJob`**

Em `src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs`, dentro do `foreach`, logo após `if (message.ProcessedAt is not null) continue;`, adicionar:

```csharp
using var _corrProp = message.CorrelationId is not null
    ? LogContext.PushProperty("CorrelationId", message.CorrelationId)
    : null;
```

Adicionar o using para Serilog no topo do arquivo:
```csharp
using Serilog.Context;
```

- [ ] **Step 6: Compilar e executar testes**

```
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```

Expected: Build OK, todos os testes passam.

- [ ] **Step 7: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Persistence/OutboxMessage.cs
git add src/VisuFiscalHub.Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs
git add src/VisuFiscalHub.Infrastructure/Persistence/Migrations/
git add src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs
git commit -m "feat: adicionar CorrelationId em OutboxMessage + migration + LogContext no OutboxRelayJob"
```

---

## Task 7: Configuração Serilog (packages, appsettings, Program.cs, Docker Compose)

**Files:**
- Modify: `src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj`
- Modify: `src/VisuFiscalHub.Api/Program.cs`
- Modify: `src/VisuFiscalHub.Api/appsettings.json`
- Modify: `src/VisuFiscalHub.Api/appsettings.Development.json`
- Create: `src/VisuFiscalHub.Api/appsettings.Docker.json`
- Modify: `infra/docker-compose.override.yml`

- [ ] **Step 1: Adicionar packages NuGet ausentes**

```
dotnet add src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj package Serilog.Sinks.Seq
dotnet add src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj package Serilog.Enrichers.Environment
dotnet add src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj package Serilog.Enrichers.Thread
```

- [ ] **Step 2: Remover packages redundantes**

`Serilog.Sinks.Console` e `Serilog.Sinks.File` passam a ser configurados via appsettings. O `Serilog.Sinks.Console` ainda é necessário (referenciado em appsettings); `Serilog.Sinks.File` não é mais usado. Remover apenas o File:

```
dotnet remove src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj package Serilog.Sinks.File
```

- [ ] **Step 3: Atualizar `Program.cs` — remover `.WriteTo.Console()` hardcoded; adicionar enrichers**

Localizar o bloco (linhas 51–55):

```csharp
builder.Host.UseSerilog((ctx, services, config) =>
    config.ReadFrom.Configuration(ctx.Configuration)
          .ReadFrom.Services(services)
          .Enrich.FromLogContext()
          .WriteTo.Console());
```

Substituir por:

```csharp
builder.Host.UseSerilog((ctx, services, config) =>
    config.ReadFrom.Configuration(ctx.Configuration)
          .ReadFrom.Services(services)
          .Enrich.FromLogContext()
          .Enrich.WithMachineName()
          .Enrich.WithThreadId());
```

- [ ] **Step 4: Atualizar `appsettings.json`**

Substituir o arquivo inteiro por (mantendo as outras seções existentes — Jwt, AllowedHosts):

```json
{
  "AllowedHosts": "*",
  "Jwt": {
    "Issuer": "visu-fiscal-hub",
    "Audience": "visu-fiscal-hub-clients",
    "SigningKey": ""
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "Hangfire.Server": "Information",
        "Hangfire": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}"
        }
      }
    ]
  }
}
```

A seção `"Logging"` é removida — fica inativa quando Serilog está em uso e causa confusão.

- [ ] **Step 5: Atualizar `appsettings.Development.json`**

Substituir o arquivo inteiro por (manter `ConnectionStrings` e `Jwt` existentes):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=visufiscalhub;Username=visufiscal;Password=troque_esta_senha_em_producao"
  },
  "Jwt": {
    "SigningKey": "dev-only-signing-key-min-32-chars-placeholder!"
  },
  "Serilog": {
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}"
        }
      },
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341"
        }
      }
    ]
  }
}
```

- [ ] **Step 6: Criar `appsettings.Docker.json`**

Criar `src/VisuFiscalHub.Api/appsettings.Docker.json`:

```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}"
        }
      },
      {
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://seq:5341"
        }
      }
    ]
  }
}
```

- [ ] **Step 7: Atualizar `infra/docker-compose.override.yml`**

Substituir o arquivo inteiro por:

```yaml
# infra/docker-compose.override.yml
# Sobreposições para desenvolvimento local — NÃO usar em produção
services:
  hub:
    environment:
      ASPNETCORE_ENVIRONMENT: Docker
      Serilog__MinimumLevel__Default: Debug
      HangfireDashboard__User: ""
      HangfireDashboard__Password: ""
      POSTGRES_INCLUDE_ERROR_DETAIL: "true"
    volumes:
      - ../src/VisuFiscalHub.Api/appsettings.Development.json:/app/appsettings.Development.json:ro
      - ../src/VisuFiscalHub.Api/appsettings.Docker.json:/app/appsettings.Docker.json:ro

  seq:
    image: datalust/seq:latest
    container_name: visu-fiscal-seq
    environment:
      - ACCEPT_EULA=Y
    ports:
      - "5341:5341"
      - "8081:80"
    volumes:
      - seq-data:/data
    networks:
      - hub-network

  postgres:
    ports:
      - "${POSTGRES_PORT:-5432}:5432"

volumes:
  seq-data:

networks:
  hub-network:
    external: true
```

- [ ] **Step 8: Compilar e executar testes**

```
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```

Expected: Build OK, todos os testes passam.

- [ ] **Step 9: Commit**

```
git add src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj
git add src/VisuFiscalHub.Api/Program.cs
git add src/VisuFiscalHub.Api/appsettings.json
git add src/VisuFiscalHub.Api/appsettings.Development.json
git add src/VisuFiscalHub.Api/appsettings.Docker.json
git add infra/docker-compose.override.yml
git commit -m "feat: configurar Serilog + Seq (appsettings, Docker Compose, enrichers)"
```

---

## Task 8: LogContext + DeliveryAttempt em FiscalDocumentProcessingJob

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs`
- Modify: `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs`

- [ ] **Step 1: Escrever testes para verificar criação de DeliveryAttempt**

Em `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs`, adicionar os seguintes testes (sem remover os existentes):

```csharp
// ── DeliveryAttempt criado em cada caminho ────────────────────────────────

[Fact]
public async Task ExecuteAsync_SefazAutorizado_CriaDeliveryAttemptEnvio()
{
    var (job, documentoRepo, _, sefazClient, dbContext, unitOfWork, _) = CriarJobComDbContext();
    var doc = CriarDocumentoEnfileirado(TipoDocumento.NfCe);
    documentoRepo.GetByIdForUpdateAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);
    sefazClient.SubmeterAutorizacaoAsync(doc.Id, doc.TenantId, Arg.Any<CancellationToken>())
        .Returns(Result.Success(new SefazRetorno(
            Autorizado: true, CStat: "100", XMotivo: "Autorizado",
            NProt: "135260000000001", XmlAutorizado: "<protNFe/>",
            QrCodeUrl: FakeQrUrl, ElapsedMs: 150L)));

    await job.ExecuteAsync(doc.Id, CancellationToken.None);

    dbContext.DeliveryAttempts.ShouldContain(a =>
        a.DocumentoId == doc.Id &&
        a.TipoTentativa == TipoTentativa.Envio &&
        a.Success &&
        a.ResponseCode == "100" &&
        a.ElapsedMs == 150L);
}

[Fact]
public async Task ExecuteAsync_SefazRejeitado_CriaDeliveryAttemptEnvioFalso()
{
    var (job, documentoRepo, _, sefazClient, dbContext, unitOfWork, _) = CriarJobComDbContext();
    var doc = CriarDocumentoEnfileirado(TipoDocumento.NfCe);
    documentoRepo.GetByIdForUpdateAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);
    sefazClient.SubmeterAutorizacaoAsync(doc.Id, doc.TenantId, Arg.Any<CancellationToken>())
        .Returns(Result.Success(new SefazRetorno(
            Autorizado: false, CStat: "999", XMotivo: "Rejeição",
            NProt: null, XmlAutorizado: null,
            QrCodeUrl: null, ElapsedMs: 80L)));

    await job.ExecuteAsync(doc.Id, CancellationToken.None);

    dbContext.DeliveryAttempts.ShouldContain(a =>
        a.DocumentoId == doc.Id &&
        a.TipoTentativa == TipoTentativa.Envio &&
        !a.Success &&
        a.ElapsedMs == 80L);
}
```

**Nota:** Para os testes de `DeliveryAttempt`, o job precisa ter acesso ao `ApplicationDbContext`. Atualmente o job **não** tem `ApplicationDbContext` como dependência — apenas `IUnitOfWork`. Verificar se o `FiscalDocumentProcessingJob` precisa receber `ApplicationDbContext` como nova dependência (similar a `CancelamentoJob`). Se sim, atualizar o construtor do job e os construtores dos testes.

Verificar a assinatura atual do `CancelamentoJob` para ver como ele recebe `ApplicationDbContext`:

```csharp
// CancelamentoJob tem: ApplicationDbContext _dbContext + IUnitOfWork _unitOfWork
// FiscalDocumentProcessingJob terá o mesmo padrão
```

Adicionar `ApplicationDbContext` ao construtor de `FiscalDocumentProcessingJob` e ao método auxiliar `CreateJob()` nos testes.

- [ ] **Step 2: Executar testes para confirmar que falham**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~NfceProcessingJobTests"
```

Expected: os novos testes de `DeliveryAttempt` FAIL.

- [ ] **Step 3: Adicionar `ApplicationDbContext` ao `FiscalDocumentProcessingJob`**

Em `src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs`:

Adicionar ao construtor:

```csharp
private readonly ApplicationDbContext _dbContext;

public FiscalDocumentProcessingJob(
    IDocumentoFiscalRepository documentoRepo,
    ITenantRepository tenantRepo,
    ISefazClient sefazClient,
    ApplicationDbContext dbContext,     // ← adicionar
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<FiscalDocumentProcessingJob> logger)
{
    _documentoRepo = documentoRepo;
    _tenantRepo    = tenantRepo;
    _sefazClient   = sefazClient;
    _dbContext     = dbContext;         // ← adicionar
    _unitOfWork    = unitOfWork;
    _timeProvider  = timeProvider;
    _logger        = logger;
}
```

Adicionar os usings necessários:

```csharp
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Infrastructure.Persistence;
```

- [ ] **Step 4: Adicionar `LogContext.PushProperty` em `ExecuteAsync`**

Em `ExecuteAsync`, após carregar o documento e antes do bloco de idempotência, adicionar:

```csharp
using var _tenantProp = LogContext.PushProperty("TenantId", documento.TenantId.Value);
using var _docProp    = LogContext.PushProperty("DocumentoId", documentoId.Value);
```

Adicionar o using no topo:

```csharp
using Serilog.Context;
```

- [ ] **Step 5: Adicionar `DeliveryAttempt` em `AutorizarAsync`**

No final de `AutorizarAsync`, após `await _unitOfWork.SaveChangesAsync(ct);`:

Remover o `await _documentoRepo.UpdateAsync(documento, ct);` + `await _unitOfWork.SaveChangesAsync(ct);` existentes e substituir por:

```csharp
var attempt = DeliveryAttempt.Criar(
    documento.Id,
    TipoTentativa.Envio,
    _timeProvider.GetUtcNow(),
    success: true,
    responseCode: retorno.CStat,
    responseMessage: null,
    elapsedMs: retorno.ElapsedMs);

await _documentoRepo.UpdateAsync(documento, ct);
_dbContext.DeliveryAttempts.Add(attempt);
await _unitOfWork.SaveChangesAsync(ct);
```

- [ ] **Step 6: Adicionar `DeliveryAttempt` em `RejeitarAsync`**

Mesmo padrão — substituir o bloco de save:

```csharp
var attempt = DeliveryAttempt.Criar(
    documento.Id,
    TipoTentativa.Envio,
    _timeProvider.GetUtcNow(),
    success: false,
    responseCode: retorno.CStat,
    responseMessage: retorno.XMotivo,
    elapsedMs: retorno.ElapsedMs);

await _documentoRepo.UpdateAsync(documento, ct);
_dbContext.DeliveryAttempts.Add(attempt);
await _unitOfWork.SaveChangesAsync(ct);
```

- [ ] **Step 7: Adicionar `DeliveryAttempt` em `DenegarAsync`**

Mesmo padrão:

```csharp
var attempt = DeliveryAttempt.Criar(
    documento.Id,
    TipoTentativa.Envio,
    _timeProvider.GetUtcNow(),
    success: false,
    responseCode: retorno.CStat,
    responseMessage: motivo,
    elapsedMs: retorno.ElapsedMs);

await _documentoRepo.UpdateAsync(documento, ct);
_dbContext.DeliveryAttempts.Add(attempt);
await _unitOfWork.SaveChangesAsync(ct);
```

- [ ] **Step 8: Adicionar dois DeliveryAttempts em `AutorizarPorDuplicidadeAsync`**

No path de sucesso (após confirmar `consulta.Autorizado`), substituir o bloco de save:

```csharp
var attemptEnvio = DeliveryAttempt.Criar(
    documento.Id,
    TipoTentativa.Envio,
    _timeProvider.GetUtcNow(),
    success: false,
    responseCode: "572",
    responseMessage: "Duplicidade detectada — consulta realizada",
    elapsedMs: 0L);  // ElapsedMs do envio não está disponível aqui

var attemptConsulta = DeliveryAttempt.Criar(
    documento.Id,
    TipoTentativa.Consulta,
    _timeProvider.GetUtcNow(),
    success: true,
    responseCode: consulta.CStat,
    responseMessage: null,
    elapsedMs: consulta.ElapsedMs);

await _documentoRepo.UpdateAsync(documento, ct);
_dbContext.DeliveryAttempts.Add(attemptEnvio);
_dbContext.DeliveryAttempts.Add(attemptConsulta);
await _unitOfWork.SaveChangesAsync(ct);
```

**Nota:** O `ElapsedMs` do envio duplicado não está disponível em `AutorizarPorDuplicidadeAsync` porque o retorno original já foi processado em `ExecuteAsync`. Usar `0L` para o attempt de envio no caminho de duplicidade é aceitável — o dado relevante é o `ElapsedMs` da consulta.

- [ ] **Step 9: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~NfceProcessingJobTests"
```

Expected: todos os testes existentes + novos passam.

- [ ] **Step 10: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs
git add tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs
git commit -m "feat: DeliveryAttempt + LogContext em FiscalDocumentProcessingJob"
```

---

## Task 9: LogContext + DeliveryAttempt em ReconciliacaoJobProcessor

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/ReconciliacaoJobProcessor.cs`
- Modify: `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/ReconciliacaoJobProcessorTests.cs`

- [ ] **Step 1: Escrever testes para DeliveryAttempt**

Em `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/ReconciliacaoJobProcessorTests.cs`, adicionar:

```csharp
[Fact]
public async Task ReconciliarAsync_SefazAutorizado_CriaDeliveryAttemptConsulta()
{
    var (job, documentoRepo, sefazClient, dbContext, unitOfWork, _, _) = CriarJobComDbContext();
    var doc = CriarDocumentoProcessando();
    documentoRepo.GetProcessandoAntigoAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
        .Returns(new List<DocumentoFiscal> { doc });
    sefazClient.ConsultarNfeAsync(doc.ChaveAcesso.Valor, doc.TenantId, doc.Tipo, Arg.Any<CancellationToken>())
        .Returns(Result.Success(new SefazConsultaRetorno(
            Encontrado: true, Autorizado: true, CStat: "100",
            NProt: "135260000000001", XmlProtocolo: "<protNFe/>", ElapsedMs: 200L)));

    await job.ExecuteAsync(CancellationToken.None);

    dbContext.DeliveryAttempts.ShouldContain(a =>
        a.DocumentoId == doc.Id &&
        a.TipoTentativa == TipoTentativa.Consulta &&
        a.Success &&
        a.ElapsedMs == 200L);
}
```

- [ ] **Step 2: Executar teste para confirmar que falha**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ReconciliacaoJobProcessorTests"
```

Expected: novo teste FAIL.

- [ ] **Step 3: Adicionar `ApplicationDbContext` ao `ReconciliacaoJobProcessor`**

Mesmo padrão do Task 8 — adicionar `ApplicationDbContext _dbContext` ao construtor.

- [ ] **Step 4: Adicionar `LogContext.PushProperty` por documento em `ReconciliarAsync`**

No início de `ReconciliarAsync`, antes do log existente:

```csharp
using Serilog.Context;
// ...

private async Task ReconciliarAsync(Domain.Entities.DocumentoFiscal documento, CancellationToken ct)
{
    using var _tenantProp = LogContext.PushProperty("TenantId", documento.TenantId.Value);
    using var _docProp    = LogContext.PushProperty("DocumentoId", documento.Id.Value);

    _logger.LogWarning("Reconciliando {DocumentoId} ...", documento.Id.Value, ...);
    // ...
```

- [ ] **Step 5: Adicionar `DeliveryAttempt` em `AutorizarPorConsultaAsync`**

Substituir o bloco de save:

```csharp
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
```

- [ ] **Step 6: Adicionar `DeliveryAttempt` em `FalharAsync`**

Após o bloco de `failResult.IsFailure`, no path de sucesso do `FalharAsync`:

```csharp
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
```

- [ ] **Step 7: Adicionar `DeliveryAttempt` para consulta falha em `ReconciliarAsync`**

No bloco `if (consultaResult.IsFailure)`, antes do `EnqueueProcessingAsync`:

```csharp
// Não cria DeliveryAttempt aqui — consulta falhou com erro de infraestrutura (sem CStat)
// O reenfileiramento via FiscalDocumentProcessingJob gerará o attempt quando for processado
```

Ou, se quiser registrar a tentativa falha:

```csharp
// consultaResult.IsFailure == true significa erro de rede/timeout — não há CStat
// Registrar attempt de consulta com success=false para visibilidade
var attemptFalha = DeliveryAttempt.Criar(
    documento.Id,
    TipoTentativa.Consulta,
    _timeProvider.GetUtcNow(),
    success: false,
    responseCode: null,
    responseMessage: consultaResult.Error.Message,
    elapsedMs: 0L);
_dbContext.DeliveryAttempts.Add(attemptFalha);
await _unitOfWork.SaveChangesAsync(ct);
```

Escolha: registrar é mais observável. Implementar o segundo caminho.

- [ ] **Step 8: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ReconciliacaoJobProcessorTests"
```

Expected: todos passam.

- [ ] **Step 9: Executar suite completa**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```

Expected: todos passam.

- [ ] **Step 10: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Jobs/ReconciliacaoJobProcessor.cs
git add tests/VisuFiscalHub.Tests/Infrastructure/Jobs/ReconciliacaoJobProcessorTests.cs
git commit -m "feat: DeliveryAttempt + LogContext por documento em ReconciliacaoJobProcessor"
```

---

## Task 10: LogContext em CancelamentoJob

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs`

O `CancelamentoJob` já tem `DeliveryAttempt` e `Stopwatch` corretos. Esta task adiciona apenas `LogContext.PushProperty` para `TenantId` e `DocumentoId`.

- [ ] **Step 1: Verificar que não há testes de `CancelamentoJob` quebrados**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CancelamentoJob"
```

Expected: todos passam (sem mudança ainda).

- [ ] **Step 2: Adicionar `LogContext.PushProperty` em `CancelamentoJob.ExecuteAsync`**

Em `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs`, após carregar o documento e o tenant (por volta da linha 77, após `var tenant = ...`), adicionar:

```csharp
using Serilog.Context;
// ...
using var _tenantProp = LogContext.PushProperty("TenantId", documento.TenantId.Value);
using var _docProp    = LogContext.PushProperty("DocumentoId", documentoId.Value);
```

- [ ] **Step 3: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CancelamentoJob"
```

Expected: todos passam.

- [ ] **Step 4: Executar suite completa**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```

Expected: todos passam.

- [ ] **Step 5: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs
git commit -m "feat: LogContext TenantId/DocumentoId em CancelamentoJob"

---

## Task 11: CorrelationId em OutboxMessage via CorrelationIdAmbient

**Context:** `OutboxMessage` é criado pelo `DomainEventsInterceptor`, que é Singleton — não pode injetar `ICorrelationContext` (Scoped). A solução é um `static AsyncLocal<string?>` que o `CorrelationIdMiddleware` popula e o interceptor lê.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Services/HttpCorrelationContext.cs` — expor método estático Set
- Create: `src/VisuFiscalHub.Infrastructure/Persistence/CorrelationIdAmbient.cs` — holder AsyncLocal
- Modify: `src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs` — chamar CorrelationIdAmbient.Set
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/DomainEventsInterceptor.cs` — ler CorrelationIdAmbient.Current
- Modify: `tests/VisuFiscalHub.Tests/Api/CorrelationIdMiddlewareTests.cs` — verificar que o ambient é definido

- [ ] **Step 1: Criar `CorrelationIdAmbient`**

Criar `src/VisuFiscalHub.Infrastructure/Persistence/CorrelationIdAmbient.cs`:

```csharp
namespace VisuFiscalHub.Infrastructure.Persistence;

public static class CorrelationIdAmbient
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string? Current => _current.Value;

    public static void Set(string? correlationId) => _current.Value = correlationId;
}
```

- [ ] **Step 2: Escrever teste para verificar que `DomainEventsInterceptor` usa o ambient**

Adicionar em `tests/VisuFiscalHub.Tests/Api/CorrelationIdMiddlewareTests.cs`:

```csharp
[Fact]
public async Task Invoke_DefinidoCorrelationId_SetsAmbient()
{
    var context = new DefaultHttpContext();
    context.Request.Headers["X-Correlation-Id"] = "test-id-999";
    var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

    string? capturedAmbient = null;
    await middleware.InvokeAsync(context, async _ =>
    {
        capturedAmbient = CorrelationIdAmbient.Current;
        await Task.CompletedTask;
    });

    capturedAmbient.ShouldBe("test-id-999");
}
```

Nota: este teste requer que `CorrelationIdMiddleware` chame `CorrelationIdAmbient.Set(correlationId)`.

- [ ] **Step 3: Executar teste para confirmar que falha**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CorrelationIdMiddlewareTests"
```

Expected: novo teste FAIL.

- [ ] **Step 4: Atualizar `CorrelationIdMiddleware` para chamar `CorrelationIdAmbient.Set`**

Em `src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs`:

```csharp
using Serilog.Context;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var raw = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
        var correlationId = raw is { Length: > 0 }
            ? raw[..Math.Min(raw.Length, 128)]
            : Guid.NewGuid().ToString("N");

        context.Response.Headers["X-Correlation-Id"] = correlationId;
        context.Items["CorrelationId"] = correlationId;
        CorrelationIdAmbient.Set(correlationId);

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        await next(context);
    }
}
```

- [ ] **Step 5: Atualizar `DomainEventsInterceptor` para ler `CorrelationIdAmbient.Current`**

Em `src/VisuFiscalHub.Infrastructure/Persistence/DomainEventsInterceptor.cs`, na projeção de `outboxMessages`:

```csharp
var correlationId = CorrelationIdAmbient.Current;

var outboxMessages = entities
    .SelectMany(e => e.DomainEvents)
    .Select(domainEvent => new OutboxMessage
    {
        Id = Guid.CreateVersion7(),
        EventType = domainEvent.GetType().FullName!,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredAt = domainEvent.OccurredAt,
        CorrelationId = correlationId
    })
    .ToList();
```

- [ ] **Step 6: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~CorrelationIdMiddlewareTests"
```

Expected: todos passam.

- [ ] **Step 7: Executar suite completa**

```
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```

Expected: todos passam.

- [ ] **Step 8: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Persistence/CorrelationIdAmbient.cs
git add src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs
git add src/VisuFiscalHub.Infrastructure/Persistence/DomainEventsInterceptor.cs
git add tests/VisuFiscalHub.Tests/Api/CorrelationIdMiddlewareTests.cs
git commit -m "feat: propagar CorrelationId para OutboxMessage via CorrelationIdAmbient (AsyncLocal)"
```
```
