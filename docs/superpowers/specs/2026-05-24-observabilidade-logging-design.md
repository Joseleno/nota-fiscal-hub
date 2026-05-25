# Fase 13 — Observabilidade e Logging: Design

## Objetivo

Tornar o VisuFiscalHub observável em produção: rastrear uma requisição HTTP ponta-a-ponta (API → job → SEFAZ), identificar qual tenant gerou o problema, medir latência por chamada SEFAZ, e receber alertas quando jobs falham — tudo consultável em Seq via structured logs.

---

## Seção 1 — Correlation ID e Enriquecimento de Contexto

### X-Correlation-Id Header — `CorrelationIdMiddleware`

Cada requisição HTTP recebe um `CorrelationId` único. O middleware `CorrelationIdMiddleware`:

1. Lê `X-Correlation-Id` do header da requisição. Se presente, reusa o valor (truncado a 128 chars para evitar log injection). Se ausente, gera `Guid.NewGuid().ToString("N")`.
2. Escreve `X-Correlation-Id` no header da resposta.
3. Armazena o `correlationId` em `HttpContext.Items["CorrelationId"]` para que componentes downstream (ex: `GlobalExceptionHandler`) possam lê-lo sem depender do Serilog.
4. Injeta no contexto Serilog via `LogContext.PushProperty`, com disposal garantido pelo `using` que engloba o `await next(context)`:

```csharp
public async Task InvokeAsync(HttpContext context, RequestDelegate next)
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        is { Length: > 0 } h ? h[..Math.Min(h.Length, 128)] : Guid.NewGuid().ToString("N");

    context.Response.Headers["X-Correlation-Id"] = correlationId;
    context.Items["CorrelationId"] = correlationId;

    using var _ = LogContext.PushProperty("CorrelationId", correlationId);
    await next(context);
}
```

**Posição no pipeline** — o middleware deve ser adicionado em `Program.cs` **antes** de `UseSerilogRequestLogging()` para que o log de request já inclua `CorrelationId`, e logo após `UseExceptionHandler()`:

```csharp
app.UseExceptionHandler("/error");
app.UseMiddleware<CorrelationIdMiddleware>();   // ← antes de UseSerilogRequestLogging
app.UseSerilogRequestLogging();
app.UseRateLimiter();
// ...
```

### `ICorrelationContext` — propagação para a camada Application

Para que command handlers possam propagar `CorrelationId` para `OutboxMessage` sem acessar `HttpContext` diretamente (violação de Clean Architecture), uma interface Scoped é definida na camada Application:

```csharp
// Application.Common.Interfaces
public interface ICorrelationContext
{
    string? CorrelationId { get; }
}
```

A implementação em Infrastructure lê de `IHttpContextAccessor`:

```csharp
// Infrastructure
internal sealed class HttpCorrelationContext(IHttpContextAccessor accessor) : ICorrelationContext
{
    public string? CorrelationId =>
        accessor.HttpContext?.Items["CorrelationId"] as string;
}
```

Registrada como `Scoped` no container. Injetada nos command handlers que criam `OutboxMessage`.

### Jobs Hangfire — `CorrelationIdJobFilter`

Jobs Hangfire não têm requisição HTTP, mas precisam de rastreabilidade. A solução é um `IServerFilter`:

```csharp
internal sealed class CorrelationIdJobFilter : IServerFilter
{
    private static readonly object _scopeKey = new();

    public void OnPerforming(PerformingContext context)
    {
        // Usa o JobId como CorrelationId — garante o mesmo valor em todos os retries
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

**Por que usar `BackgroundJob.Id`:** O `PerformContext` é recriado a cada tentativa — não há mecanismo nativo do Hangfire para persistir dados entre tentativas via `Items`. Usar o `JobId` (que é estável entre retries) garante que todos os retries do mesmo job apareçam no Seq sob o mesmo `CorrelationId`, facilitando o diagnóstico de falhas transientes.

**Chave tipada em `Items`:** O uso de `private static readonly object _scopeKey` evita colisão com outros filtros que usam chaves string genéricas.

**Registro do filtro** em `DependencyInjection.cs`, na configuração do Hangfire:

```csharp
services.AddHangfire(config =>
{
    // ...configuração existente...
    config.UseFilter(new CorrelationIdJobFilter());
});
```

### TenantId no Contexto dos Jobs

No início de cada job, após carregar o documento e o tenant, empurra `TenantId` e `DocumentoId` via `LogContext.PushProperty` **uma vez**:

```csharp
using var _tenantProp = LogContext.PushProperty("TenantId", tenant.Id.Value);
using var _docProp    = LogContext.PushProperty("DocumentoId", documentoId.Value);
```

**`ReconciliacaoJobProcessor` é multi-tenant e multi-documento.** Nesse job, os campos `TenantId` e `DocumentoId` devem ser empurrados **dentro do loop**, em cada iteração de `ReconciliarAsync`, não no topo do job — caso contrário todos os logs do loop carregarão os valores do primeiro documento.

### GlobalExceptionHandler

O handler de exceções não tratadas inclui `CorrelationId` no `ProblemDetails` de HTTP 500. O valor é lido de `HttpContext.Items["CorrelationId"]` (escrito pelo `CorrelationIdMiddleware`) e adicionado via `Extensions`:

```csharp
var correlationId = httpContext.Items["CorrelationId"] as string;
var details = new ProblemDetails
{
    Status = StatusCodes.Status500InternalServerError,
    Title = "Internal Server Error",
    Extensions = { ["correlationId"] = correlationId }
};
```

Resultado JSON:
```json
{
  "title": "Internal Server Error",
  "status": 500,
  "correlationId": "a3f2b1c4d5e6..."
}
```

### OutboxMessage — CorrelationId

A tabela `OutboxMessages` recebe a coluna `CorrelationId varchar(128)` nullable. O command handler que cria a `OutboxMessage` injeta `ICorrelationContext` e propaga o valor:

```csharp
var outbox = new OutboxMessage { ..., CorrelationId = _correlationContext.CorrelationId };
```

O `OutboxRelayJob` empurra o `CorrelationId` da mensagem via `LogContext.PushProperty` **dentro do loop**, uma vez por mensagem, para que o log de entrega de cada webhook carregue a rastreabilidade da requisição original.

**Migration necessária:** `ALTER TABLE outbox_messages ADD COLUMN correlation_id varchar(128) NULL`.

### Campos estruturados mínimos por log

| Campo | Fonte |
|---|---|
| `CorrelationId` | `CorrelationIdMiddleware` / `CorrelationIdJobFilter` |
| `TenantId` | Job (por iteração) ou command handler |
| `DocumentoId` | Job (por iteração) |
| `JobId` | `CorrelationIdJobFilter` — mesmo valor que `CorrelationId` nos jobs |
| `StatusCode` | `UseSerilogRequestLogging` |
| `ElapsedMs` | `UseSerilogRequestLogging` / `DeliveryAttempt` |
| `RequestPath` | `UseSerilogRequestLogging` |
| `MachineName` | `Enrich.WithMachineName()` |

---

## Seção 2 — Serilog e Seq

### NuGet Packages

`Serilog.AspNetCore` já está presente no projeto (`10.0.0`). Adicionar apenas os ausentes:

```
Serilog.Sinks.Seq
Serilog.Enrichers.Environment
Serilog.Enrichers.Thread
```

### Configuração Serilog em `Program.cs`

Remover o `.WriteTo.Console()` hardcoded existente (linha 55 atual) e manter `ReadFrom.Services(services)` para suporte a enrichers que dependem de DI:

```csharp
builder.Host.UseSerilog((ctx, services, config) =>
    config.ReadFrom.Configuration(ctx.Configuration)
          .ReadFrom.Services(services)
          .Enrich.FromLogContext()
          .Enrich.WithMachineName()
          .Enrich.WithThreadId());
```

O sink Console é configurado via `appsettings` — sem hardcoded — eliminando duplicação.

### `appsettings.json` (base — todos os ambientes)

Substituir a seção `"Logging"` existente (inativa quando Serilog está em uso) pela seção `"Serilog"`. A seção `"Logging"` deve ser removida para evitar configuração morta:

```json
{
  "AllowedHosts": "*",
  "Jwt": { ... },
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

Notas:
- `{Properties:j}` **não** está no template — evita inundar o console com JSON verboso.
- `Hangfire.Server: Information` loga execuções; `Hangfire: Warning` silencia ruído de enfileiramento. O namespace mais específico tem precedência.
- `Hangfire` em `Warning` evita ~30k logs/dia de enfileiramento em produção.

### `appsettings.Development.json` (dev local com IDE)

O arquivo define o array `WriteTo` completo (Console + Seq). O sistema de configuração do ASP.NET Core **substitui** arrays por completo — não faz merge — portanto o Console deve ser repetido para não ser perdido:

```json
{
  "ConnectionStrings": { ... },
  "Jwt": { ... },
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

### `appsettings.Docker.json` (ambiente Docker)

Em vez de sobrescrever elementos de array por variável de ambiente (frágil — depende de índice fixo), criar `appsettings.Docker.json` com a URL correta para o container:

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

### Docker Compose

O `docker-compose.override.yml` (dev local) recebe o serviço `seq` e monta `appsettings.Docker.json`. O serviço se chama `hub` (nome correto do projeto):

```yaml
# infra/docker-compose.override.yml
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

O `ASPNETCORE_ENVIRONMENT: Docker` faz o ASP.NET Core carregar `appsettings.Docker.json`, que aponta para `http://seq:5341` — sem índice fixo, sem variável de ambiente frágil.

**Nota:** A versão gratuita do Seq tem limite de ingestão. Para uso em produção, avaliar licença ou alternativas (Grafana Loki, Elastic).

---

## Seção 3 — DeliveryAttempt nos Jobs

### Estado atual (o que existe hoje)

- `CancelamentoJob`: cria `DeliveryAttempt` com `Stopwatch` próprio — `ElapsedMs` correto.
- `FiscalDocumentProcessingJob`: **não cria nenhum `DeliveryAttempt`** hoje. `ISefazClient` não expõe tempo de resposta.
- `ReconciliacaoJobProcessor`: **não cria nenhum `DeliveryAttempt`** hoje.
- `SefazClient`: **não tem `Stopwatch`** — tempo de resposta SEFAZ não é medido em nenhum lugar nesses fluxos.

O escopo desta seção é **adicionar `DeliveryAttempt` do zero** nesses dois jobs, com `ElapsedMs` correto.

### Solução: `ElapsedMs` em `SefazRetorno` e `SefazConsultaRetorno`

Em vez de criar um wrapper genérico `SefazResponse<T>` (que resulta em double-envelope `Result<SefazResponse<T>>` e acesso via `.Value.Value`), adicionar `ElapsedMs` diretamente nos records existentes:

```csharp
// Application.Common.Interfaces — ISefazClient.cs
public sealed record SefazRetorno(
    bool Autorizado,
    string? NProtAutorizacao,
    string? QrCodeUrl,
    string CStat,
    string XMotivo,
    long ElapsedMs);        // ← novo campo

public sealed record SefazConsultaRetorno(
    bool Autorizado,
    string? NProtAutorizacao,
    string? QrCodeUrl,
    string CStat,
    string XMotivo,
    long ElapsedMs);        // ← novo campo
```

O `SefazClient` mede com `Stopwatch` internamente e popula `ElapsedMs` em ambos os records. A assinatura de `ISefazClient` **não muda** — apenas os tipos de retorno recebem um campo extra.

**Impacto em testes:** `NfceProcessingJobTests`, `ReconciliacaoJobProcessorTests` e `FakeSefazClient` instanciam `SefazRetorno` e `SefazConsultaRetorno` diretamente. Todos precisarão receber o argumento `ElapsedMs` (ex: `ElapsedMs: 0L` nos testes — valor irrelevante para a lógica testada).

### TipoTentativa — remoção de `Retry` sem renumeração

`Retry = 3` não é usado em nenhum lugar do codebase. É removido do enum. `Cancelamento` mantém o valor `4` para não corromper dados históricos já persistidos no banco:

```csharp
public enum TipoTentativa
{
    Envio = 1,
    Consulta = 2,
    // 3 removido (era Retry — nunca usado)
    Cancelamento = 4
}
```

**Nenhuma data migration necessária** — apenas a remoção do valor do enum C#.

### DeliveryAttempt em `FiscalDocumentProcessingJob`

Adicionar criação de `DeliveryAttempt` nos helpers privados `AutorizarAsync`, `RejeitarAsync`, `DenegarAsync` e `AutorizarPorDuplicidadeAsync`. O attempt é salvo na **mesma `SaveChangesAsync`** que persiste o novo estado do documento (atomicidade):

```csharp
// Exemplo em AutorizarAsync:
var attempt = DeliveryAttempt.Criar(
    documentoId,
    TipoTentativa.Envio,
    _timeProvider.GetUtcNow(),
    success: true,
    responseCode: retorno.CStat,
    responseMessage: null,
    elapsedMs: retorno.ElapsedMs);

_dbContext.DeliveryAttempts.Add(attempt);
await _unitOfWork.SaveChangesAsync(ct);   // salva documento + attempt juntos
```

**Caminho de duplicidade (`AutorizarPorDuplicidadeAsync`):** registra **dois** attempts:
1. `TipoTentativa.Envio`, `Success = false`, `ResponseCode = "572"` — o envio que retornou duplicidade.
2. `TipoTentativa.Consulta`, `Success = true`, `ResponseCode = retornoConsulta.CStat` — a consulta de resolução.

Ambos salvos na mesma `SaveChangesAsync` que persiste o estado `Autorizado`.

### DeliveryAttempt em `ReconciliacaoJobProcessor`

Adicionar attempt em `ReconciliarAsync` após cada chamada a `ConsultarNfeAsync`:

```csharp
var attempt = DeliveryAttempt.Criar(
    documento.Id,
    TipoTentativa.Consulta,
    _timeProvider.GetUtcNow(),
    success: retorno.Autorizado,
    responseCode: retorno.CStat,
    responseMessage: retorno.XMotivo,
    elapsedMs: retorno.ElapsedMs);

_dbContext.DeliveryAttempts.Add(attempt);
await _unitOfWork.SaveChangesAsync(ct);   // salva documento + attempt juntos
```

### CancelamentoJob — padrão existente mantido

`CancelamentoJob` já registra `DeliveryAttempt` corretamente. Mantém o padrão de dois saves distintos porque:
- **Falha HTTP** (catch): attempt salvo com `CancellationToken.None` antes do `throw` — documento não foi alterado.
- **Falha de parse**: idem.
- **Sucesso/rejeição**: documento salvo primeiro; attempt salvo em `RegistrarAttemptAsync` separado com `CancellationToken.None`.

Este padrão é intencional no `CancelamentoJob` e não é alterado nesta fase.

### Log estruturado nos jobs

Cada job empurra `TenantId` e `DocumentoId` via `LogContext.PushProperty` com `using`. Para `FiscalDocumentProcessingJob` e `CancelamentoJob` (um documento por execução), no início do método `ExecuteAsync`. Para `ReconciliacaoJobProcessor` (múltiplos documentos), **dentro do loop** em `ReconciliarAsync`:

```csharp
// FiscalDocumentProcessingJob.ExecuteAsync — após carregar tenant e documento
using var _tenantProp = LogContext.PushProperty("TenantId", tenant.Id.Value);
using var _docProp    = LogContext.PushProperty("DocumentoId", documentoId.Value);
```

Campos adicionais nos logs de chamada SEFAZ: `{CStat}`, `{ElapsedMs}`, `{Url}`.

---

## Migrations EF Core necessárias

| Migration | Mudança |
|---|---|
| `AddCorrelationIdToOutboxMessages` | `ALTER TABLE outbox_messages ADD COLUMN correlation_id varchar(128) NULL` |

A remoção de `TipoTentativa.Retry = 3` do enum C# **não requer migration de schema** (coluna `tipo_tentativa` permanece `integer`). Nenhuma data migration é necessária porque `Retry = 3` nunca foi persistido no banco.

---

## Escopo desta fase

**Incluído:**
- `CorrelationIdMiddleware` com `HttpContext.Items` e `LogContext.PushProperty`
- `ICorrelationContext` (Application) + `HttpCorrelationContext` (Infrastructure)
- `CorrelationIdJobFilter` (Hangfire `IServerFilter`) usando `BackgroundJob.Id`
- `GlobalExceptionHandler` com `CorrelationId` em `ProblemDetails.Extensions`
- `OutboxMessage.CorrelationId` + migration
- Configuração Serilog completa (`Program.cs`, `appsettings.*`, `appsettings.Docker.json`)
- Serviço `seq` no `docker-compose.override.yml`
- `ElapsedMs` em `SefazRetorno` / `SefazConsultaRetorno` + `Stopwatch` no `SefazClient`
- `DeliveryAttempt` em `FiscalDocumentProcessingJob` (Envio + Consulta + duplicidade)
- `DeliveryAttempt` em `ReconciliacaoJobProcessor`
- `TipoTentativa.Retry` removido (valor `4` = `Cancelamento` mantido)
- Atomicidade attempt + documento em `FiscalDocumentProcessingJob` e `ReconciliacaoJobProcessor`
- `LogContext.PushProperty` com `using` em todos os jobs

**Excluído (fora de escopo):**
- Dashboard de métricas (Prometheus/Grafana)
- Alertas automáticos (webhooks Seq, PagerDuty)
- Distributed tracing (OpenTelemetry)
- Log sampling / rate limiting
- Retenção e rotação de logs em produção
