# Spec Técnica — Fase 5: Application Layer — Emissão de Documentos

**Versão:** 1.0
**Data:** 2026-05-12
**Dependências:** Fases 1 (Domain), 2 (Banco), 3 (App ClienteApp/Tenant), 4 (JWT)
**Esforço estimado:** M (3–5 dias)

---

## 1. Visão Geral da Fase

### Objetivo

Implementar o pipeline CQRS completo de emissão de NFC-e: desde o recebimento do `IssueDocumentCommand` até a persistência do `DocumentoFiscal` com status `Enfileirado` e o enfileiramento do job de processamento. Esta fase não envolve comunicação com o SEFAZ — apenas a orquestração até o ponto em que o job de infraestrutura assume.

### Dependências obrigatórias resolvidas antes desta fase

| Artefato | De onde vem |
|---|---|
| `DocumentoFiscal`, `Tenant`, `ItemDocumento`, `Pagamento` | Fase 1 — Domain |
| `IDocumentoFiscalRepository`, `ITenantRepository`, `IUnitOfWork` | Fase 1 (interface) + Fase 2 (implementação) |
| `IDocumentJobQueue` | Fase 1 (interface) — implementação concreta em Fase 7 |
| `ICurrentUserContext` | Fase 3 — definido em `Application/Common/Interfaces/` |
| `ValidationBehavior`, `LoggingBehavior` | Fase 3 |
| `TenantResponse`, `PagedResult<T>` | Fase 3 |
| `DocumentoFiscalAutorizadoEvent`, `DocumentoFiscalDenegadoEvent` | Fase 1 — Domain Events |
| `IWebhookDeliveryService` | Interface definida na Fase 1/Application; implementação concreta em Fase 6b |

### Critério de Conclusão

- `IssueDocumentCommandHandler` persiste `Enfileirado` **antes** de chamar `IDocumentJobQueue.EnqueueProcessing`
- Segundo comando com mesmo `IdempotencyKey` retorna o mesmo `DocumentoId` sem criar novo documento
- `IDocumentJobQueue.EnqueueProcessing` é chamado somente **após** `IUnitOfWork.SaveChangesAsync` completar
- `IssueDocumentCommandValidator` rejeita: sem itens, pagamentos não fecham total, CPF ausente com valor > R$ 10.000,00, `indPres=2`, NCM fora de 8 dígitos
- `DocumentoStatusResponse` **não** expõe campo `XmlAssinado`
- Handlers compilam com Mediator.SourceGenerator sem warnings

---

## 2. Árvore de Arquivos

```
src/VisuFiscalHub.Application/
  Documents/
    Commands/
      IssueDocument/
        IssueDocumentCommand.cs
        IssueDocumentCommandHandler.cs
        IssueDocumentCommandValidator.cs
    EventHandlers/
      DocumentoFiscalAutorizadoEventHandler.cs
      DocumentoFiscalDenegadoEventHandler.cs
    Queries/
      GetDocumentStatus/
        GetDocumentStatusQuery.cs
        GetDocumentStatusQueryHandler.cs
  Common/
    Models/
      IssueDocumentResponse.cs
      DocumentoStatusResponse.cs
      ItemDocumentoDto.cs
      PagamentoDto.cs
      ConsumidorDto.cs
```

**Total: 10 arquivos .cs**

---

## 3. Especificação por Arquivo

---

### 3.1 `IssueDocumentCommand.cs`

**Caminho:** `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs`

**Namespace:** `VisuFiscalHub.Application.Documents.Commands.IssueDocument`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura:**
```csharp
public sealed record IssueDocumentCommand : IRequest<Result<IssueDocumentResponse>>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string IdempotencyKey { get; init; } = default!;
    public TipoDocumento Tipo { get; init; }
    public IReadOnlyList<ItemDocumentoDto> Itens { get; init; } = [];
    public IReadOnlyList<PagamentoDto> Pagamentos { get; init; } = [];
    public ConsumidorDto? Consumidor { get; init; }
    public int IndPresenca { get; init; } = 1;
}
```

**Invariantes e regras:**
- `IdempotencyKey` é obrigatório — validado pelo `IssueDocumentCommandValidator`
- `IndPresenca` tem padrão 1 (presencial); valores permitidos: 1, 3, 4, 9
- `Tipo` no MVP = `TipoDocumento.NfCe` (65) — validator deve rejeitar outros valores se necessário

**Notas de implementação:**
- `record` imutável — sem setters públicos
- O `TenantId` e `ClienteAppId` são preenchidos pelo endpoint a partir do `ICurrentUserContext`, não pelo caller externo
- `IndPresenca` recebe o valor informado pelo ClienteApp com padrão 1

---

### 3.2 `IssueDocumentCommandHandler.cs`

**Caminho:** `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommandHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Documents.Commands.IssueDocument`

**Usings:**
```csharp
using System.Security.Cryptography;
using Mediator;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
```

**Assinatura:**
```csharp
public sealed class IssueDocumentCommandHandler
    : IRequestHandler<IssueDocumentCommand, Result<IssueDocumentResponse>>
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly IUnitOfWork _uow;
    private readonly IDocumentJobQueue _jobQueue;
    private readonly ILogger<IssueDocumentCommandHandler> _logger;

    public IssueDocumentCommandHandler(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        IUnitOfWork uow,
        IDocumentJobQueue jobQueue,
        ILogger<IssueDocumentCommandHandler> logger);

    public async ValueTask<Result<IssueDocumentResponse>> Handle(
        IssueDocumentCommand request,
        CancellationToken cancellationToken);
}
```

**Pipeline ordenado — passos obrigatórios em Handle:**

```
1. Verifica idempotência
   - GetByIdempotencyKeyAsync(tenantId, idempotencyKey)
   - Se encontrado e status FINAL (Autorizado, Rejeitado, Cancelado, Falhou, Denegado):
       → return Result.Failure(DocumentoFiscalErrors.IdempotencyKeyJaUsada)  [HTTP 409]
   - Se encontrado e status NÃO-FINAL (Criado, Enfileirado, Processando):
       → return Result.Success(Map para IssueDocumentResponse)  [HTTP 200]
   - Se não encontrado → continua

2. Carrega Tenant
   - GetByIdAsync(request.TenantId)
   - Se null → return Result.Failure(TenantErrors.NaoEncontrado)
   - Se tenant.ClienteAppId != request.ClienteAppId → return Result.Failure(TenantErrors.NaoPertenceAoClienteApp)

3. Obtém próximo número
   - await _tenantRepo.GetNextNumeracaoAsync(request.TenantId, tenant.ConfiguracaoFiscal.Serie, ct)

4. Gera cNF
   - byte[] randomBytes = RandomNumberGenerator.GetBytes(4)
   - string cNF = (BitConverter.ToUInt32(randomBytes, 0) % 100_000_000).ToString("D8")
   - NUNCA usar Random.Shared ou Math.Abs

5. Monta ChaveAcesso
   - string aamm = DateTimeOffset.UtcNow.ToString("yyMM")  // usar TimeProvider se injetado
   - ChaveAcesso chave = ChaveAcesso.Gerar(
         cUF: tenant.ConfiguracaoFiscal.UfCodigo,
         aamm: aamm,
         cnpj: tenant.Cnpj.Valor,
         mod: (int)request.Tipo,
         serie: tenant.ConfiguracaoFiscal.Serie,
         nNF: numero,
         tpEmis: 1,
         cNF: cNF)

6. Cria DocumentoFiscal
   - var itens = request.Itens.Select(MapToItemDocumento).ToList()
   - var pagamentos = request.Pagamentos.Select(MapToPagamento).ToList()
   - Result<DocumentoFiscal> criarResult = DocumentoFiscal.Criar(
         tenantId: request.TenantId,
         idempotencyKey: request.IdempotencyKey,
         tipo: request.Tipo,
         chaveAcesso: chave,
         numero: numero,
         serie: tenant.ConfiguracaoFiscal.Serie,
         itens: itens,
         pagamentos: pagamentos)
   - Se criarResult.IsFailure → return criarResult.Error

7. Persiste documento (status Criado)
   - await _documentoRepo.AddAsync(documento, ct)

8. Enfileira na state machine e persiste (status Enfileirado)
   - Result enfileirarResult = documento.Enfileirar()
   - Se enfileirarResult.IsFailure → return enfileirarResult.Error
   - await _uow.SaveChangesAsync(ct)   ← COMMIT da transação

9. Enfileira job (APÓS commit, nunca antes)
   - _jobQueue.EnqueueProcessing(documento.Id)

10. Retorna resposta
    - return Result.Success(new IssueDocumentResponse { ... })
```

**Invariantes críticas:**
- `RandomNumberGenerator.GetBytes(4)` — NUNCA `Random.Shared`
- `_jobQueue.EnqueueProcessing` SEMPRE depois de `_uow.SaveChangesAsync`
- Se `EnqueueProcessing` falhar, o documento fica em `Enfileirado` — o `ReconciliacaoJobProcessor` cobre este caso
- `CancellationToken` propagado para TODOS os métodos async
- `TimeProvider` deve ser injetado para `DateTimeOffset.UtcNow` se testabilidade for exigida

**Notas de implementação:**
- Não injetar `IHttpContextAccessor` — usar `ICurrentUserContext` apenas se necessário para logging; `TenantId` e `ClienteAppId` vêm do command
- Status finais para idempotência: `Autorizado`, `Rejeitado`, `Cancelado`, `Falhou`, `Denegado`
- Status não-finais (retornam 200): `Criado`, `Enfileirado`, `Processando`
- Usar `_logger.LogInformation` com structured logging: `{TenantId}`, `{DocumentoId}`, `{IdempotencyKey}`

---

### 3.3 `IssueDocumentCommandValidator.cs`

**Caminho:** `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommandValidator.cs`

**Namespace:** `VisuFiscalHub.Application.Documents.Commands.IssueDocument`

**Usings:**
```csharp
using FluentValidation;
```

**Assinatura:**
```csharp
public sealed class IssueDocumentCommandValidator : AbstractValidator<IssueDocumentCommand>
{
    private const decimal LimiteIdentificacaoConsumidor = 10_000.00m;
    private const decimal ToleranciaFechamentoPagamentos = 0.01m;

    public IssueDocumentCommandValidator();

    // Métodos auxiliares privados:
    private static decimal CalcularTotalItens(IssueDocumentCommand command);
    private static decimal CalcularTotalPagamentos(IssueDocumentCommand command);
}
```

**Regras obrigatórias no construtor:**

```
RuleFor(x => x.IdempotencyKey)
    .NotEmpty().WithMessage("IdempotencyKey é obrigatório.")
    .MaximumLength(100).WithMessage("IdempotencyKey não pode exceder 100 caracteres.")

RuleFor(x => x.Itens)
    .NotEmpty().WithMessage("A nota deve conter pelo menos um item.")

RuleForEach(x => x.Itens).ChildRules(item =>
    item.RuleFor(i => i.Ncm)
        .NotEmpty()
        .Length(8).WithMessage("NCM deve ter exatamente 8 dígitos.")
        .Matches(@"^\d{8}$").WithMessage("NCM deve conter apenas dígitos.")

    item.RuleFor(i => i.Quantidade)
        .GreaterThan(0).WithMessage("Quantidade deve ser maior que zero.")

    item.RuleFor(i => i.ValorUnitario)
        .GreaterThan(0).WithMessage("Valor unitário deve ser maior que zero.")
)

// CPF obrigatório se total > R$ 10.000,00 (condição ESTRITA >)
RuleFor(x => x.Consumidor)
    .Must((cmd, consumidor) =>
        CalcularTotalItens(cmd) <= LimiteIdentificacaoConsumidor
        || consumidor?.Cpf != null)
    .WithMessage("CPF do consumidor é obrigatório para notas acima de R$ 10.000,00.")

// Fechamento de pagamentos (tolerância R$ 0,01)
RuleFor(x => x)
    .Must(cmd =>
        Math.Abs(CalcularTotalItens(cmd) - CalcularTotalPagamentos(cmd))
        <= ToleranciaFechamentoPagamentos)
    .WithMessage("O total dos pagamentos não corresponde ao valor total dos itens (tolerância R$ 0,01).")

// indPresenca: proibir valor 2; aceitar 1, 3, 4, 9
RuleFor(x => x.IndPresenca)
    .Must(v => v != 2)
    .WithMessage("indPres=2 (internet) é proibido para NFC-e (rejeição SEFAZ 717).")
    .Must(v => new[] { 1, 3, 4, 9 }.Contains(v))
    .WithMessage("indPresenca deve ser 1 (presencial), 3 (delivery), 4 (autoatendimento) ou 9 (outros).")
```

**Invariantes críticas:**
- Condição CPF é `> 10.000,00` (maior estrito), NUNCA `>= 10.000,00`
- Uma nota com valor exato de R$ 10.000,00 SEM CPF é PERMITIDA
- Uma nota com valor de R$ 10.000,01 SEM CPF é REJEITADA
- `indPres=2` deve ser **explicitamente rejeitado** (mensagem específica referenciando rejeição 717)
- `ToleranciaFechamentoPagamentos = 0.01m` — R$ 0,01, não R$ 0,50 (o R$ 0,50 é do SEFAZ para `vNF`)

**Notas de implementação:**
- `CalcularTotalItens`: soma de `(quantidade * valorUnitario) - valorDesconto` por item
- `CalcularTotalPagamentos`: soma de `Valor` por pagamento
- Mensagens de erro em português-BR
- Registrado via DI: `services.AddValidatorsFromAssemblyContaining<IssueDocumentCommandValidator>()`

---

### 3.4 `DocumentoFiscalAutorizadoEventHandler.cs`

**Caminho:** `src/VisuFiscalHub.Application/Documents/EventHandlers/DocumentoFiscalAutorizadoEventHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Documents.EventHandlers`

**Usings:**
```csharp
using Mediator;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Events;
```

**Assinatura:**
```csharp
public sealed class DocumentoFiscalAutorizadoEventHandler
    : INotificationHandler<DocumentoFiscalAutorizadoEvent>
{
    private readonly IWebhookDeliveryService _webhookDeliveryService;
    private readonly ILogger<DocumentoFiscalAutorizadoEventHandler> _logger;

    public DocumentoFiscalAutorizadoEventHandler(
        IWebhookDeliveryService webhookDeliveryService,
        ILogger<DocumentoFiscalAutorizadoEventHandler> logger);

    public async ValueTask Handle(
        DocumentoFiscalAutorizadoEvent notification,
        CancellationToken cancellationToken);
}
```

**Invariantes e regras:**
- Chama `_webhookDeliveryService.DeliverAsync(notification.DocumentoFiscalId, notification.ClienteAppId, cancellationToken)`
- O dispatch do webhook NUNCA deve ser inline no handler de emissão — este event handler é o único ponto de disparo
- Erros de webhook são logados mas NÃO relançados — o retry é responsabilidade do `WebhookDeliveryService` via Hangfire

**Notas de implementação:**
- Publicado via Outbox Pattern: o `OutboxRelayJob` publica o evento; este handler é chamado pelo `IMediator.Publish`
- Registrado automaticamente pelo Mediator.SourceGenerator ao scanear o assembly

---

### 3.5 `DocumentoFiscalDenegadoEventHandler.cs`

**Caminho:** `src/VisuFiscalHub.Application/Documents/EventHandlers/DocumentoFiscalDenegadoEventHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Documents.EventHandlers`

**Usings:**
```csharp
using Mediator;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Events;
```

**Assinatura:**
```csharp
public sealed class DocumentoFiscalDenegadoEventHandler
    : INotificationHandler<DocumentoFiscalDenegadoEvent>
{
    private readonly IWebhookDeliveryService _webhookDeliveryService;
    private readonly ILogger<DocumentoFiscalDenegadoEventHandler> _logger;

    public DocumentoFiscalDenegadoEventHandler(
        IWebhookDeliveryService webhookDeliveryService,
        ILogger<DocumentoFiscalDenegadoEventHandler> logger);

    public async ValueTask Handle(
        DocumentoFiscalDenegadoEvent notification,
        CancellationToken cancellationToken);
}
```

**Invariantes e regras:**
- Loga em nível `Critical` com campos estruturados: `{TenantId}`, `{Cnpj}`, `{XMotivo}`, `{DocumentoFiscalId}`
- Mensagem de log: `"DENEGACAO FISCAL: Tenant {TenantId} CNPJ {Cnpj} teve documento denegado (cStat=110). Motivo: {XMotivo}. Revisão manual necessária antes de novas emissões."`
- Chama `_webhookDeliveryService.DeliverAsync` para notificar o ClienteApp
- Denegação (cStat=110) é DISTINTA de rejeição: indica CNPJ irregular junto ao SEFAZ

**Notas de implementação:**
- O payload do webhook deve informar `status: "denied"` — distinto de `"rejected"`
- Futuramente: marcar o Tenant para revisão manual. No MVP, o log `Critical` é suficiente para alerta operacional

---

### 3.6 `GetDocumentStatusQuery.cs`

**Caminho:** `src/VisuFiscalHub.Application/Documents/Queries/GetDocumentStatus/GetDocumentStatusQuery.cs`

**Namespace:** `VisuFiscalHub.Application.Documents.Queries.GetDocumentStatus`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura:**
```csharp
public sealed record GetDocumentStatusQuery : IRequest<Result<DocumentoStatusResponse>>
{
    public DocumentoFiscalId DocumentoId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
}
```

**Notas de implementação:**
- `ClienteAppId` é obrigatório para validação de pertencimento — preenchido pelo endpoint via `ICurrentUserContext`

---

### 3.7 `GetDocumentStatusQueryHandler.cs`

**Caminho:** `src/VisuFiscalHub.Application/Documents/Queries/GetDocumentStatus/GetDocumentStatusQueryHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Documents.Queries.GetDocumentStatus`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura:**
```csharp
public sealed class GetDocumentStatusQueryHandler
    : IRequestHandler<GetDocumentStatusQuery, Result<DocumentoStatusResponse>>
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;

    public GetDocumentStatusQueryHandler(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo);

    public async ValueTask<Result<DocumentoStatusResponse>> Handle(
        GetDocumentStatusQuery request,
        CancellationToken cancellationToken);
}
```

**Invariantes e regras:**
- Carrega `DocumentoFiscal` — se null, retorna `DocumentoFiscalErrors.NaoEncontrado` (404)
- Carrega `Tenant` do documento — verifica `tenant.ClienteAppId == request.ClienteAppId`
- Se Tenant pertence a outro ClienteApp → `TenantErrors.NaoPertenceAoClienteApp` (403)
- Usa `AsNoTracking()` — query read-only
- Retorna `DocumentoStatusResponse` — SEM campo `XmlAssinado`

**Notas de implementação:**
- Alternativa: join no repositório para validar em uma única query
- A verificação de pertencimento garante isolamento multi-tenant

---

### 3.8 `IssueDocumentResponse.cs`

**Caminho:** `src/VisuFiscalHub.Application/Common/Models/IssueDocumentResponse.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Models`

**Usings:**
```csharp
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura:**
```csharp
public sealed record IssueDocumentResponse
{
    public Guid DocumentoId { get; init; }
    public StatusDocumento Status { get; init; }
    public string ChaveAcesso { get; init; } = default!;
    public string PollUrl { get; init; } = default!;
    public DateTimeOffset CreatedAt { get; init; }
}
```

**Notas de implementação:**
- `PollUrl` deve ser montado como `/api/v1/documentos/{documentoId}/status`
- `ChaveAcesso` é string de 44 dígitos — não expõe o value object do domínio

---

### 3.9 `DocumentoStatusResponse.cs`

**Caminho:** `src/VisuFiscalHub.Application/Common/Models/DocumentoStatusResponse.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Models`

**Usings:**
```csharp
using VisuFiscalHub.Domain.Enums;
```

**Assinatura:**
```csharp
public sealed record DocumentoStatusResponse
{
    public Guid DocumentoId { get; init; }
    public StatusDocumento Status { get; init; }
    public string? ChaveAcesso { get; init; }
    public string? QrCode { get; init; }
    public string? Protocolo { get; init; }
    public string? MotivoRejeicao { get; init; }
    public DateTimeOffset? AuthorizedAt { get; init; }

    // AUSENTE intencionalmente: XmlAssinado
    // Endpoint dedicado: GET /api/v1/documentos/{id}/xml
}
```

**Invariantes críticas:**
- `XmlAssinado` NUNCA deve ser adicionado a este DTO
- A ausência de `XmlAssinado` é uma decisão arquitetural (DA-06) — XML completo disponível em endpoint separado
- Todos os campos exceto `DocumentoId` e `Status` são nullable

---

### 3.10 `ItemDocumentoDto.cs`, `PagamentoDto.cs`, `ConsumidorDto.cs`

**Caminho:** `src/VisuFiscalHub.Application/Common/Models/`

**Namespace:** `VisuFiscalHub.Application.Common.Models`

**Assinaturas:**

```csharp
// ItemDocumentoDto.cs
public sealed record ItemDocumentoDto
{
    public string CodigoProduto { get; init; } = default!;
    public string Descricao { get; init; } = default!;
    public string Ncm { get; init; } = default!;
    public string? Cest { get; init; }
    public string CfopSaida { get; init; } = default!;
    public string UnidadeComercial { get; init; } = default!;
    public decimal Quantidade { get; init; }
    public decimal ValorUnitario { get; init; }
    public decimal ValorDesconto { get; init; }
    public int OrigemMercadoria { get; init; }
}

// PagamentoDto.cs
public sealed record PagamentoDto
{
    public int TipoPagamento { get; init; }
    public decimal Valor { get; init; }
}

// ConsumidorDto.cs
public sealed record ConsumidorDto
{
    public string? Cpf { get; init; }
    public string? Nome { get; init; }
}
```

**Notas de implementação:**
- `ConsumidorDto.Cpf` aceita entrada com ou sem máscara — normalização aplicada no handler
- `TipoPagamento` como `int` conforme valores do enum `TipoPagamento` do domínio
- `OrigemMercadoria` como `int` conforme enum `OrigemMercadoria` do domínio

---

## 4. Fluxo de Dados

```
ClienteApp (HTTP POST /api/v1/documentos/nfce)
    │
    │  Headers: Authorization: Bearer JWT
    │           X-Tenant-Id: {uuid}
    │           X-Idempotency-Key: {uuid}
    │  Body: { itens, pagamentos, consumidor?, indPresenca? }
    ▼
[TenantValidationMiddleware]
    │  Valida tenant pertence ao ClienteApp do JWT
    │  Popula HttpContext.Items["TenantContext"]
    ▼
[Endpoint: POST /api/v1/documentos/nfce]
    │  Constrói IssueDocumentCommand com TenantId + ClienteAppId
    │  do ICurrentUserContext
    ▼
[ValidationBehavior] ── FluentValidation ──►  422 Unprocessable se inválido
    │
    ▼
[IssueDocumentCommandHandler]
    │
    ├─1─► IDocumentoFiscalRepository.GetByIdempotencyKeyAsync()
    │       └─► Encontrado + status final ──────────────────────► 409 Conflict
    │       └─► Encontrado + status não-final ──────────────────► 200 OK (documento existente)
    │       └─► Não encontrado → continua
    │
    ├─2─► ITenantRepository.GetByIdAsync()
    │       └─► Null ─────────────────────────────────────────── → 404 Not Found
    │       └─► ClienteAppId incorreto ─────────────────────────► 403 Forbidden
    │
    ├─3─► ITenantRepository.GetNextNumeracaoAsync()
    │       └─► SELECT nextval('seq_nfe_{tenantId:N}_{serie}')
    │
    ├─4─► RandomNumberGenerator.GetBytes(4) → cNF (8 dígitos decimais)
    │
    ├─5─► ChaveAcesso.Gerar(cUF, aamm, cnpj, 65, serie, numero, 1, cNF)
    │       └─► Módulo 11 → cDV
    │       └─► ChaveAcesso (44 dígitos)
    │
    ├─6─► DocumentoFiscal.Criar(tenantId, idempotencyKey, NfCe, chave, num, serie, itens, pags)
    │       └─► Status = Criado
    │
    ├─7─► IDocumentoFiscalRepository.AddAsync(documento)
    │
    ├─8─► documento.Enfileirar()   → Status = Enfileirado
    │     IUnitOfWork.SaveChangesAsync()   ← COMMIT
    │       └─► DomainEventsInterceptor coleta eventos pendentes → OutboxMessage
    │
    ├─9─► IDocumentJobQueue.EnqueueProcessing(documentoId)   ← APÓS commit
    │       └─► Hangfire: BackgroundJob.Enqueue(HangfireJobProcessor.ProcessarDocumentoAsync)
    │
    └─10─► return Result.Success(IssueDocumentResponse)
                │
                ▼
           202 Accepted
           { documentoId, status: "Enfileirado", chaveAcesso, pollUrl, createdAt }

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
FLUXO ASSÍNCRONO (após 202 retornar ao ClienteApp)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[Hangfire: HangfireJobProcessor.ProcessarDocumentoAsync] (Fase 7)
    │  Carrega DocumentoFiscal do banco
    │  documento.IniciarProcessamento()  → Status = Processando
    │  ISefazClient.SubmeterAutorizacaoAsync()
    │
    ├─► cStat=100 → documento.Autorizar() → publishes DocumentoFiscalAutorizadoEvent (via Outbox)
    ├─► cStat=110 → documento.Denegar()  → publishes DocumentoFiscalDenegadoEvent (via Outbox)
    └─► cStat=4xx → documento.Rejeitar() → publishes DocumentoFiscalRejeitadoEvent (via Outbox)

[Hangfire: OutboxRelayJob] (Fase 6b)
    │  SELECT outbox_messages WHERE processed_at IS NULL LIMIT 50 FOR UPDATE SKIP LOCKED
    │  IMediator.Publish(DocumentoFiscalAutorizadoEvent)
    ▼
[DocumentoFiscalAutorizadoEventHandler]
    │  IWebhookDeliveryService.DeliverAsync(documentoId, clienteAppId)
    ▼
[POST webhookUrl ClienteApp]
    Headers: X-Hub-Signature-256: sha256=<hex>
    Body: { documentId, status: "authorized", chaveAcesso, qrCode, protocolo }
```

---

## 5. Checklist de Conclusão

### CQRS
- [ ] `IssueDocumentCommand` implementa `IRequest<Result<IssueDocumentResponse>>`
- [ ] `IssueDocumentCommandHandler` usa `IUnitOfWork`, não `DbContext` diretamente
- [ ] `IssueDocumentCommandHandler` retorna DTO (`IssueDocumentResponse`), nunca entidade
- [ ] `GetDocumentStatusQueryHandler` usa `AsNoTracking()` (obrigatório — checar na implementação do repositório)
- [ ] `GetDocumentStatusQueryHandler` retorna DTO (`DocumentoStatusResponse`)
- [ ] `CancellationToken` propagado em todos os métodos async

### Pipeline de Emissão
- [ ] Idempotência verificada no passo 1 (antes de qualquer operação de escrita)
- [ ] Tenant carregado e pertencimento validado no passo 2
- [ ] `cNF` gerado com `RandomNumberGenerator.GetBytes(4)` — nunca `Random.Shared`
- [ ] `documento.Enfileirar()` chamado antes de `SaveChangesAsync`
- [ ] `IDocumentJobQueue.EnqueueProcessing` chamado APÓS `SaveChangesAsync`
- [ ] Documentos com status não-final retornam 200 com documento existente (sem novo processamento)
- [ ] Documentos com status final retornam `Result.Failure` (409)

### Validações
- [ ] CPF obrigatório quando valor total `> R$ 10.000,00` (condição estrita `>`)
- [ ] Valor `R$ 10.000,00` exato SEM CPF é ACEITO pelo validator
- [ ] `indPres=2` explicitamente rejeitado com mensagem referenciando rejeição 717
- [ ] NCM validado com exatamente 8 dígitos numéricos
- [ ] Quantidade e valor unitário validados como `> 0`
- [ ] Fechamento de pagamentos com tolerância de `R$ 0,01`

### DTOs
- [ ] `DocumentoStatusResponse` SEM campo `XmlAssinado`
- [ ] `IssueDocumentResponse` com `PollUrl` montada
- [ ] Nenhum DTO expõe entidades de domínio

### Event Handlers
- [ ] `DocumentoFiscalAutorizadoEventHandler` delega para `IWebhookDeliveryService`
- [ ] `DocumentoFiscalDenegadoEventHandler` loga em nível `Critical` com `TenantId`, `Cnpj`, `XMotivo`
- [ ] Erros de webhook não relançados — retry é responsabilidade do WebhookDeliveryService

### Clean Architecture
- [ ] Handlers não referenciam `Microsoft.EntityFrameworkCore`
- [ ] Handlers não referenciam `VisuFiscalHub.Infrastructure`
- [ ] Handlers não referenciam `IHttpContextAccessor` — usar `ICurrentUserContext`
- [ ] Todos os repositórios acessados via interfaces do Domain layer

### Testes Obrigatórios (Fase 10)
- [ ] `Handle_QuandoComandoValido_DeveEnfileirarJob()` — verifica ordem: SaveChanges antes de EnqueueProcessing
- [ ] `Handle_QuandoIdempotencyKeyJaUsadaStatusFinal_DeveRetornar409()`
- [ ] `Handle_IdempotencyKey_QuandoStatusProcessando_DeveRetornar200ComDocumentoExistente()`
- [ ] `Handle_QuandoTenantNaoPertenceAoClienteApp_DeveRetornarErro()`
- [ ] `Validar_QuandoValorAcimaDe10000SemCpf_DeveRejeitar()` — R$ 10.000,01
- [ ] `Validar_QuandoValorExatamente10000SemCpf_DevePermitir()` — boundary R$ 10.000,00
- [ ] `Validar_QuandoIndPresencaProibido_DeveRejeitar()` — `indPres=2`
- [ ] `Validar_QuandoPagamentosNaoFechamTotal_DeveRejeitar()`
- [ ] `Validar_QuandoNcmComMenosDe8Digitos_DeveRejeitar()`
