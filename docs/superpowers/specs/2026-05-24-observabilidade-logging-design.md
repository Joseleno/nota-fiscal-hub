# Fase 13 — Observabilidade e Logging: Design

## Objetivo

Tornar o VisuFiscalHub observável em produção: rastrear uma requisição HTTP ponta-a-ponta (API → job → SEFAZ), identificar qual tenant gerou o problema, medir latência por chamada SEFAZ, e receber alertas quando jobs falham — tudo consultável em Seq via structured logs.

---

## Seção 1 — Correlation ID e Enriquecimento de Contexto

### X-Correlation-Id Header

Cada requisição HTTP recebe um `CorrelationId` único. O middleware:

1. Lê `X-Correlation-Id` do header da requisição (se presente, reusa; se ausente, gera `Guid.NewGuid().ToString("N")`).
2. Escreve `X-Correlation-Id` no header da resposta.
3. Injeta no contexto Serilog via `using var _ = LogContext.PushProperty("CorrelationId", correlationId)` — o `using` garante disposal ao final da requisição, evitando vazamento entre threads do pool.

### Jobs Hangfire

Jobs Hangfire (reconciliação, processamento fiscal, cancelamento) não têm requisição HTTP, mas precisam de rastreabilidade. A solução é um `IServerFilter` (`CorrelationIdJobFilter`) que:

1. Em `OnPerforming`: lê `correlationId` do `PerformContext.Items` (colocado pelo job ao ser enfileirado, se disponível) ou gera novo via fallback: `correlationId ?? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")`.
2. Empurra `CorrelationId` via `LogContext.PushProperty` com `using` em escopo de `IDisposable` armazenado em `PerformContext.Items`.
3. Em `OnPerformed`: descarta o `IDisposable`.

Isso significa que **nenhuma assinatura de método de job é alterada** — `correlationId` não vira parâmetro.

### TenantId no Contexto dos Jobs

No início de cada job (após carregar o documento/tenant), um único `LogContext.PushProperty("TenantId", tenant.Id.Value)` com `using` enriquece todos os logs subsequentes do job. Não é feito por chamada.

### GlobalExceptionHandler

O handler de exceções não tratadas inclui `CorrelationId` no `ProblemDetails` retornado para HTTP 500:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Internal Server Error",
  "status": 500,
  "correlationId": "a3f2b1c4d5e6..."
}
```

Isso permite que o suporte correlacione um erro reportado pelo usuário com um log no Seq.

### OutboxMessage — CorrelationId

A tabela `OutboxMessages` recebe a coluna `CorrelationId string?`. Quando uma mensagem de outbox é criada (dentro de um command handler), o `CorrelationId` atual é propagado. O webhook processor loga `CorrelationId` ao processar cada mensagem.

### Campos estruturados mínimos por log

| Campo | Fonte |
|---|---|
| `CorrelationId` | Middleware HTTP / `CorrelationIdJobFilter` |
| `TenantId` | Job ou command handler |
| `DocumentoId` | Job ou command handler |
| `StatusCode` | Middleware request logging |
| `ElapsedMs` | `UseSerilogRequestLogging` / job |

---

## Seção 2 — Serilog e Seq

### NuGet Packages

Adicionar ao projeto `VisuFiscalHub.Api`:

```
Serilog.AspNetCore
Serilog.Sinks.Seq
Serilog.Enrichers.Environment
Serilog.Enrichers.Thread
```

### Configuração Serilog em `Program.cs`

```csharp
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId());

// No pipeline:
app.UseSerilogRequestLogging();
```

Sem `.WriteTo.Console()` hardcoded — o sink é configurado via `appsettings` para evitar duplicação.

### `appsettings.json` (base — todos os ambientes)

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning",
        "Hangfire": "Information"
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

Nota: `{Properties:j}` **não** está no template — evita inundar o console com JSON verboso.

### `appsettings.Development.json` (dev local com IDE)

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
          "serverUrl": "http://localhost:5341"
        }
      }
    ]
  }
}
```

### Docker Compose

O serviço `seq` recebe `hub-network` e o volume é declarado corretamente. A URL do Seq é injetada via variável de ambiente (override do `appsettings.Development.json`):

```yaml
services:
  api:
    environment:
      - Serilog__WriteTo__1__Args__serverUrl=http://seq:5341

  seq:
    image: datalust/seq:latest
    environment:
      - ACCEPT_EULA=Y
    ports:
      - "5341:5341"
      - "8080:80"
    volumes:
      - seq-data:/data
    networks:
      - hub-network

volumes:
  seq-data:

networks:
  hub-network:
    driver: bridge
```

### Campos no Seq

Com `Enrich.FromLogContext()`, todos os campos empurrados via `LogContext.PushProperty` aparecem como propriedades estruturadas no Seq, consultáveis por:

```
CorrelationId = 'abc123'
TenantId = '...'
DocumentoId = '...'
```

---

## Seção 3 — DeliveryAttempt nos Jobs

### Problema atual

`FiscalDocumentProcessingJob` e `ReconciliacaoJobProcessor` registram `DeliveryAttempt` mas com `ElapsedMs = 0` porque o tempo de resposta SEFAZ é medido internamente no `SefazClient` e não é retornado. `CancelamentoJob` usa `Stopwatch` próprio e já tem `ElapsedMs` correto.

### Solução: `SefazResponse<T>`

`ISefazClient` passa a retornar um VO que inclui o tempo medido:

```csharp
public sealed record SefazResponse<T>(T Value, long ElapsedMs);
```

Métodos afetados em `ISefazClient`:
- `SubmeterAutorizacaoAsync` → retorna `SefazResponse<AutorizacaoRetorno>`
- `ConsultarNfeAsync` → retorna `SefazResponse<ConsultaRetorno>`

O `SefazClient` mede com `Stopwatch` internamente e popula `ElapsedMs`. Os jobs leem `response.ElapsedMs` ao criar o `DeliveryAttempt`.

### AutorizarPorDuplicidadeAsync — DeliveryAttempt ausente

O caminho de duplicidade (cStat=573) não registrava `DeliveryAttempt`. Passa a registrar com:
- `TipoTentativa.Consulta` (não `Retry` — é uma consulta de status, não uma retransmissão)
- `Success = true` (duplicidade = SEFAZ já aceitou)
- `ElapsedMs` da consulta

### TipoTentativa

Enum revisado:

```csharp
public enum TipoTentativa
{
    Envio = 1,
    Consulta = 2,
    Cancelamento = 3
}
```

`Retry` é removido — semanticamente incorreto para o caminho de duplicidade. Todos os caminhos de consulta usam `TipoTentativa.Consulta`.

### Atomicidade: attempt + estado do documento

O padrão atual chama `SaveChangesAsync` duas vezes (uma para o documento, uma para o attempt). O design revisado salva ambos na mesma `SaveChangesAsync`:

```csharp
// Atualiza documento (estado)
// Adiciona DeliveryAttempt
_dbContext.DeliveryAttempts.Add(attempt);
// Um único SaveChangesAsync
await _unitOfWork.SaveChangesAsync(ct);
```

Se a transação falhar, nenhum dos dois é persistido — consistente. `CancelamentoJob` mantém o padrão de attempt em `CancellationToken.None` separado apenas para o registro de falha HTTP (onde o documento não foi atualizado ainda).

### Log estruturado nos jobs

Cada job, ao iniciar, empurra `TenantId` e `DocumentoId` via `LogContext.PushProperty` uma vez, não por chamada SEFAZ:

```csharp
using var _tenantProp = LogContext.PushProperty("TenantId", tenant.Id.Value);
using var _docProp    = LogContext.PushProperty("DocumentoId", documentoId.Value);
```

Campos adicionais nos logs de chamada SEFAZ: `{CStat}`, `{ElapsedMs}`, `{Url}`.

---

## Escopo desta fase

**Incluído:**
- Middleware `CorrelationIdMiddleware`
- `CorrelationIdJobFilter` (Hangfire `IServerFilter`)
- `GlobalExceptionHandler` com `CorrelationId` em ProblemDetails 500
- Coluna `CorrelationId` em `OutboxMessages`
- Configuração Serilog completa (packages, `appsettings`, Docker Compose)
- `SefazResponse<T>` VO + `ElapsedMs` em `DeliveryAttempt`
- `DeliveryAttempt` no caminho `AutorizarPorDuplicidadeAsync`
- `TipoTentativa` sem `Retry` (renomear para `Consulta`)
- Atomicidade attempt + documento
- `LogContext.PushProperty` com `using` em todos os jobs

**Excluído (fora de escopo):**
- Dashboard de métricas (Prometheus/Grafana)
- Alertas automáticos (webhooks Seq, PagerDuty)
- Distributed tracing (OpenTelemetry)
- Log sampling / rate limiting
- Retenção e rotação de logs em produção
