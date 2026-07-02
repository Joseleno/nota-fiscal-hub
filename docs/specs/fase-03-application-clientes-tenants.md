# Spec Técnica — Fase 3: Application Layer: ClienteApp e Tenant

**Versão:** 1.0  
**Data:** 2026-05-12  
**Dependências:** Fase 1 (Domain Layer) e Fase 2 (Banco de Dados) concluídas  
**Esforço estimado:** M (3–5 dias)

---

## 1. Visão Geral da Fase

### Objetivo

Implementar o CQRS completo para gestão de `ClienteApp` e `Tenant` usando Mediator.SourceGenerator 3.0. Esta fase estabelece os pipelines de `ValidationBehavior` e `LoggingBehavior`, a implementação de `ICurrentUserContext` na camada API, todos os commands e queries de ClienteApp/Tenant, os DTOs de resposta e o helper `ResultExtensions.ToHttpResult`.

### Dependências

| Dependência | O que fornece |
|---|---|
| Fase 1 — Domain Layer | `ClienteApp`, `Tenant`, `IClienteAppRepository`, `ITenantRepository`, `IUnitOfWork`, `ICurrentUserContext` (interface), `ICertificateEncryptionService` (interface), `Result<T>`, `Error`, erros de domínio |
| Fase 2 — Banco de Dados | `ApplicationDbContext`, repositórios concretos, `UnitOfWork`, migrations com tabelas criadas |

### Critério de Conclusão

- Todos os handlers compilam sem erros e Mediator.SourceGenerator gera o código de despacho
- `ValidationBehavior` e `LoggingBehavior` registrados via `services.AddMediator(options => options.AddOpenBehavior(...))`
- `CreateTenantCommandHandler` cria sequence PostgreSQL após persistir o Tenant
- `WebhookSecret` retornado uma única vez na criação; campo ausente em `ClienteAppResponse` e em qualquer query subsequente
- Testes unitários com NSubstitute passam para todos os handlers

---

## 2. Arvore de Arquivos

```
src/
  VisuFiscalHub.Application/
    Common/
      Behaviors/
        ValidationBehavior.cs
        LoggingBehavior.cs
      Models/
        ClienteAppCreatedResponse.cs
        ClienteAppResponse.cs
        TenantResponse.cs
        PagedResult.cs
      ResultExtensions.cs
    Tenants/
      Commands/
        CreateClienteApp/
          CreateClienteAppCommand.cs
          CreateClienteAppCommandHandler.cs
          CreateClienteAppCommandValidator.cs
        RotateClienteAppSecret/
          RotateClienteAppSecretCommand.cs
          RotateClienteAppSecretCommandHandler.cs
        CreateTenant/
          CreateTenantCommand.cs
          CreateTenantCommandHandler.cs
          CreateTenantCommandValidator.cs
        UpdateTenantCertificate/
          UpdateTenantCertificateCommand.cs
          UpdateTenantCertificateCommandHandler.cs
          UpdateTenantCertificateCommandValidator.cs
        UpdateTenantCsc/
          UpdateTenantCscCommand.cs
          UpdateTenantCscCommandHandler.cs
          UpdateTenantCscCommandValidator.cs
      Queries/
        GetTenant/
          GetTenantQuery.cs
          GetTenantQueryHandler.cs
        ListTenants/
          ListTenantsQuery.cs
          ListTenantsQueryHandler.cs
        GetCertificadoStatus/
          GetCertificadoStatusQuery.cs
          GetCertificadoStatusQueryHandler.cs

  VisuFiscalHub.Api/
    Authentication/
      HttpContextCurrentUserContext.cs
```

---

## 3. Especificação de Cada Arquivo

---

### 3.1 `Application/Common/Behaviors/ValidationBehavior.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Behaviors`

**Usings:**
```csharp
using FluentValidation;
using Mediator;
using VisuFiscalHub.Domain.Common;
```

**Assinatura completa:**
```csharp
public sealed class ValidationBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
    where TResponse : Result
{
    private readonly IEnumerable<IValidator<TMessage>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TMessage>> validators);

    public ValueTask<TResponse> Handle(
        TMessage message,
        CancellationToken cancellationToken,
        MessageHandlerDelegate<TMessage, TResponse> next);
}
```

**Invariantes e regras críticas:**

- Quando nenhum validator está registrado para `TMessage`, chamar `next` imediatamente sem alocações adicionais.
- Quando validators existem, executar **todos** (não curto-circuitar na primeira falha) via `await validator.ValidateAsync(message, cancellationToken)`.
- Agregar todos os erros de validação em uma lista antes de retornar.
- Retornar `Result.Failure(...)` com os erros coletados — **nunca lançar exceção** (`ValidationException` ou similar).
- O retorno deve ser `TResponse` — usar cast via `Unsafe.As<TResponse>` ou factory method `Result.Failure` compatível com o tipo genérico.
- Registrado via `services.AddMediator(options => options.AddOpenBehavior(typeof(ValidationBehavior<,>)))` — **nunca** via `services.AddTransient(typeof(IPipelineBehavior<,>), ...)`.

**Nota de implementação:**

O Mediator.SourceGenerator 3.0 usa `IPipelineBehavior<TMessage, TResponse>` onde `TMessage : IMessage`. O tipo `TResponse` para commands é tipicamente `Result<T>`. O cast para retorno de falha deve ser feito via método utilitário que cria `TResponse` como `Result.Failure` usando reflexão mínima ou via interface segregada. Avaliar se `TResponse` implementa um factory method estático para evitar boxing.

---

### 3.2 `Application/Common/Behaviors/LoggingBehavior.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Behaviors`

**Usings:**
```csharp
using Mediator;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Domain.Common;
```

**Assinatura completa:**
```csharp
public sealed class LoggingBehavior<TMessage, TResponse>
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
    where TResponse : Result
{
    private readonly ILogger<LoggingBehavior<TMessage, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TMessage, TResponse>> logger);

    public async ValueTask<TResponse> Handle(
        TMessage message,
        CancellationToken cancellationToken,
        MessageHandlerDelegate<TMessage, TResponse> next);
}
```

**Invariantes e regras críticas:**

- Loga **apenas** quando `response.IsFailure == true` — nível `Warning` ou `Error` conforme o tipo de erro.
- **Nunca** loga o happy path (caminho de sucesso) — isso é responsabilidade do tracing OpenTelemetry (spans automáticos de ASP.NET Core).
- Não loga dados sensíveis presentes no `message` (ClientSecret, PFX bytes, CSC). Logar apenas o tipo da mensagem e o código do erro.
- Registrado via `services.AddMediator(options => options.AddOpenBehavior(typeof(LoggingBehavior<,>)))`.
- A ordem de registro deve ser: ValidationBehavior primeiro, LoggingBehavior segundo — o LoggingBehavior envolve o ValidationBehavior na cadeia.

**Nota de implementação:**

```csharp
// Exemplo de body do Handle:
var response = await next(message, cancellationToken);
if (response.IsFailure)
{
    _logger.LogWarning(
        "Request {RequestType} falhou com erro {ErrorCode}: {ErrorMessage}",
        typeof(TMessage).Name,
        response.Error.Code,
        response.Error.Message);
}
return response;
```

---

### 3.3 `Api/Authentication/HttpContextCurrentUserContext.cs`

**Namespace:** `VisuFiscalHub.Api.Authentication`

**Usings:**
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed class HttpContextCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserContext(IHttpContextAccessor httpContextAccessor);

    public ClienteAppId ClienteAppId { get; }
    public TenantId TenantId { get; }
}
```

**Invariantes e regras críticas:**

- `ClienteAppId` é lido de `_httpContextAccessor.HttpContext!.User.FindFirstValue("sub")` — o claim `sub` do JWT contém o `clienteAppId` como string Guid.
- `TenantId` é lido de `_httpContextAccessor.HttpContext!.Items["TenantContext"]` — o objeto armazenado é um record `TenantContext` (definido na Fase 4). Fazer cast: `((TenantContext)items["TenantContext"]).TenantId`.
- Se `HttpContext` for nulo (fora de request) ou o claim `sub` ausente, lançar `InvalidOperationException` com mensagem descritiva — nunca retornar valores padrão silenciosos.
- Registrado como `Scoped` no `Program.cs`: `services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()`.
- Handlers na camada Application **nunca** recebem `IHttpContextAccessor` — recebem apenas `ICurrentUserContext`.
- `IHttpContextAccessor` deve estar registrado no container: `services.AddHttpContextAccessor()`.

**Nota de implementação:**

A propriedade `TenantId` só é válida após o `TenantValidationMiddleware` (Fase 4) ter populado `HttpContext.Items["TenantContext"]`. Em endpoints que não requerem tenant (ex: `POST /api/v1/clientes`), acessar `TenantId` lançará exceção — isso é correto e esperado.

---

### 3.4 `Application/Tenants/Commands/CreateClienteApp/CreateClienteAppCommand.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
```

**Assinatura completa:**
```csharp
public sealed record CreateClienteAppCommand : ICommand<Result<ClienteAppCreatedResponse>>
{
    public string Name { get; init; } = default!;
    public string ClientId { get; init; } = default!;
    public string ClientSecret { get; init; } = default!;
    public string? WebhookUrl { get; init; }
}
```

**Invariantes:**

- `ICommand<TResponse>` é o tipo do Mediator.SourceGenerator para comandos (não `IRequest<TResponse>` do MediatR).
- Propriedades `init`-only — imutável após construção.
- `ClientSecret` recebido em texto claro — o handler é responsável por derivar o hash via PBKDF2.

---

### 3.5 `Application/Tenants/Commands/CreateClienteApp/CreateClienteAppCommandHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp`

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
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class CreateClienteAppCommandHandler
    : ICommandHandler<CreateClienteAppCommand, Result<ClienteAppCreatedResponse>>
{
    private readonly IClienteAppRepository _clienteAppRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICertificateEncryptionService _encryptionService;
    private readonly ILogger<CreateClienteAppCommandHandler> _logger;

    public CreateClienteAppCommandHandler(
        IClienteAppRepository clienteAppRepository,
        IUnitOfWork unitOfWork,
        ICertificateEncryptionService encryptionService,
        ILogger<CreateClienteAppCommandHandler> logger);

    public async ValueTask<Result<ClienteAppCreatedResponse>> Handle(
        CreateClienteAppCommand command,
        CancellationToken cancellationToken);

    private static string DerivarHashSenha(string clientSecret);
    private static string GerarWebhookSecretHex();
}
```

**Invariantes e regras críticas:**

1. Verificar unicidade do `ClientId` antes de persistir — retornar `Result.Failure(ClienteAppErrors.ClientIdJaExiste)` se já existir.
2. `ClientSecretHash`: PBKDF2 com SHA-256, **mínimo 600.000 iterações** (OWASP 2024+):
   ```csharp
   // Implementação obrigatória:
   var salt = RandomNumberGenerator.GetBytes(32);
   var hash = Rfc2898DeriveBytes.Pbkdf2(
       password: clientSecret,
       salt: salt,
       iterations: 600_000,
       hashAlgorithm: HashAlgorithmName.SHA256,
       outputLength: 32);
   // Armazenar: Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash)
   ```
3. `WebhookSecret`: `RandomNumberGenerator.GetBytes(32)` convertido para hex string de 64 chars — nunca `Random.Shared`.
4. `WebhookSecretCriptografado`: criptografar o webhook secret via `ICertificateEncryptionService.Encrypt(webhookSecretHex)` — armazenar apenas o resultado criptografado.
5. Usar factory `ClienteApp.Criar(name, clientId, clientSecretHash)` do domínio.
6. Chamar `AtualizarWebhookSecret(webhookSecretCriptografado)` após criar a entidade.
7. Persistir via `_clienteAppRepository.AddAsync(clienteApp)` + `_unitOfWork.SaveChangesAsync(cancellationToken)`.
8. Retornar `ClienteAppCreatedResponse` com `WebhookSecret` em **texto claro** — esta é a ÚNICA vez que o valor é exposto.
9. Propagar `cancellationToken` para todos os métodos async.

---

### 3.6 `Application/Tenants/Commands/CreateClienteApp/CreateClienteAppCommandValidator.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.CreateClienteApp`

**Usings:**
```csharp
using FluentValidation;
```

**Assinatura completa:**
```csharp
public sealed class CreateClienteAppCommandValidator
    : AbstractValidator<CreateClienteAppCommand>
{
    public CreateClienteAppCommandValidator();
}
```

**Regras de validação (implementar no construtor):**

| Campo | Regras |
|---|---|
| `Name` | `NotEmpty()` — mensagem: "Nome do ClienteApp é obrigatório" |
| `Name` | `MaximumLength(100)` |
| `ClientId` | `NotEmpty()` — mensagem: "client_id é obrigatório" |
| `ClientId` | `Matches(@"^[a-z0-9\-_]{3,50}$")` — mensagem: "client_id deve conter apenas letras minúsculas, números, hífens e underscores (3–50 chars)" |
| `ClientSecret` | `NotEmpty()` — mensagem: "client_secret é obrigatório" |
| `ClientSecret` | `MinimumLength(16)` — mensagem: "client_secret deve ter no mínimo 16 caracteres" |
| `WebhookUrl` | Quando não nulo: `Must(BeValidHttpsUrl)` — mensagem: "webhookUrl deve ser HTTPS e não pode apontar para endereços privados (RFC 1918)" |

**Nota:** A validação de SSRF do WebhookUrl (bloqueio de RFC 1918) é validada apenas no cadastro. O método `BeValidHttpsUrl` deve verificar: schema `https`, não loopback (127.x.x.x, ::1), não RFC 1918 (10.x, 192.168.x, 172.16-31.x), não link-local (169.254.x).

---

### 3.7 `Application/Tenants/Commands/RotateClienteAppSecret/RotateClienteAppSecretCommand.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed record RotateClienteAppSecretCommand : ICommand<Result<RotateClienteAppSecretResponse>>
{
    public ClienteAppId ClienteAppId { get; init; }
}
```

---

### 3.8 `Application/Tenants/Commands/RotateClienteAppSecret/RotateClienteAppSecretCommandHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.RotateClienteAppSecret`

**Usings:**
```csharp
using System.Security.Cryptography;
using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class RotateClienteAppSecretCommandHandler
    : ICommandHandler<RotateClienteAppSecretCommand, Result<RotateClienteAppSecretResponse>>
{
    private readonly IClienteAppRepository _clienteAppRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RotateClienteAppSecretCommandHandler(
        IClienteAppRepository clienteAppRepository,
        IUnitOfWork unitOfWork);

    public async ValueTask<Result<RotateClienteAppSecretResponse>> Handle(
        RotateClienteAppSecretCommand command,
        CancellationToken cancellationToken);
}
```

**Invariantes:**

- Carregar `ClienteApp` pelo `Id` — `Result.Failure(ClienteAppErrors.NaoEncontrado)` se não encontrar.
- Gerar novo `clientSecret` com `RandomNumberGenerator.GetBytes(32)` convertido para hex (64 chars).
- Derivar novo hash PBKDF2 (mesma configuração: SHA-256, 600.000 iterações).
- Chamar método de domínio adequado para atualizar o hash (ou definir `ClienteApp.AtualizarSecret(novoHash)` se não existir).
- Persistir via `IUnitOfWork.SaveChangesAsync`.
- Retornar `RotateClienteAppSecretResponse` com `ClientId` e `NewClientSecret` em texto claro — **única vez**.
- Tokens JWT emitidos com o secret anterior expiram naturalmente (sem blacklist).

**DTO adicional necessário:**
```csharp
public sealed record RotateClienteAppSecretResponse(
    string ClientId,
    string NewClientSecret);
```

---

### 3.9 `Application/Tenants/Commands/CreateTenant/CreateTenantCommand.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.CreateTenant`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed record CreateTenantCommand : ICommand<Result<TenantResponse>>
{
    public ClienteAppId ClienteAppId { get; init; }
    public string Cnpj { get; init; } = default!;
    public string RazaoSocial { get; init; } = default!;
    public string? NomeFantasia { get; init; }
    public RegimeTributario RegimeTributario { get; init; }
    public AmbienteSefaz Ambiente { get; init; }
    public int UfCodigo { get; init; }
    public string Serie { get; init; } = default!;
    public EnderecoDto Endereco { get; init; } = default!;
}

public sealed record EnderecoDto(
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    int CodigoMunicipio,
    string Uf,
    string Cep,
    int CodigoPais,
    string? Telefone);
```

---

### 3.10 `Application/Tenants/Commands/CreateTenant/CreateTenantCommandHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.CreateTenant`

**Usings:**
```csharp
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
```

**Assinatura completa:**
```csharp
public sealed class CreateTenantCommandHandler
    : ICommandHandler<CreateTenantCommand, Result<TenantResponse>>
{
    private static readonly Regex SerieRegex = new(@"^[0-9]{1,3}$", RegexOptions.Compiled);

    private readonly ITenantRepository _tenantRepository;
    private readonly IClienteAppRepository _clienteAppRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DbContext _dbContext;
    private readonly ILogger<CreateTenantCommandHandler> _logger;

    public CreateTenantCommandHandler(
        ITenantRepository tenantRepository,
        IClienteAppRepository clienteAppRepository,
        IUnitOfWork unitOfWork,
        ApplicationDbContext dbContext,
        ILogger<CreateTenantCommandHandler> logger);

    public async ValueTask<Result<TenantResponse>> Handle(
        CreateTenantCommand command,
        CancellationToken cancellationToken);
}
```

**Invariantes e regras críticas (pipeline do handler):**

1. Verificar existência do `ClienteApp` — `Result.Failure(ClienteAppErrors.NaoEncontrado)` se não encontrar.
2. Verificar que `ClienteApp.IsActive` — `Result.Failure(ClienteAppErrors.Inativo)` se inativo.
3. Criar value object `Cnpj.Criar(command.Cnpj)` — retornar `Result.Failure(TenantErrors.CnpjInvalido)` se falhar.
4. Verificar unicidade `(cnpj, clienteAppId)` via `_tenantRepository.GetByCnpjAsync` — `Result.Failure(TenantErrors.CnpjJaCadastrado)` se já existir.
5. Construir `ConfiguracaoFiscal` value object com `Crt=command.RegimeTributario`, `Serie=command.Serie`, `Ambiente=command.Ambiente`, `UfCodigo=command.UfCodigo`.
6. Construir `Endereco` value object a partir de `command.Endereco`.
7. Chamar factory `Tenant.Criar(...)` — propagar erros de domínio.
8. Persistir via `_tenantRepository.AddAsync(tenant)` + `_unitOfWork.SaveChangesAsync(cancellationToken)`.
9. **DDL da sequence** — executar após persistir:
   ```csharp
   var serieSanitizada = command.Serie; // já validada pelo validator com regex ^[0-9]{1,3}$
   var tenantIdHex = tenant.Id.Value.ToString("N"); // UUID sem hífens, 32 chars hex
   var sequenceName = $"seq_nfe_{tenantIdHex}_{serieSanitizada}";
   await _dbContext.Database.ExecuteSqlRawAsync(
       $"CREATE SEQUENCE IF NOT EXISTS {sequenceName} START 1 INCREMENT 1",
       cancellationToken);
   ```
   - `tenantId.ToString("N")` é obrigatório — sem hífens no nome da sequence para nome PostgreSQL válido.
   - `serie` **deve** ser validada com `SerieRegex` ANTES da interpolação (prevenção de SQL injection no DDL). Se o validator FluentValidation já validou, esta é a segunda linha de defesa.
   - A sequence é criada na mesma transaction de provisionamento via `ExecuteSqlRawAsync`.
10. Publicar `TenantProvisionadoEvent` (via domain event na entidade — `AddDomainEvent` chamado dentro de `Tenant.Criar`).
11. Retornar `TenantResponse` mapeado da entidade persistida.

**CRITICO — injeção de `ApplicationDbContext`:**

O `ApplicationDbContext` é injetado diretamente no handler (não via interface) **apenas** para executar o DDL da sequence via `Database.ExecuteSqlRawAsync`. Esta é a única exceção ao padrão de não usar DbContext na Application layer. Alternativa limpa: expor método `IUnitOfWork.ExecuteSqlRawAsync(string sql, CancellationToken ct)` — avaliar se preferível para manter a camada Application livre de referência direta ao EF Core.

---

### 3.11 `Application/Tenants/Commands/CreateTenant/CreateTenantCommandValidator.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.CreateTenant`

**Usings:**
```csharp
using System.Text.RegularExpressions;
using FluentValidation;
using VisuFiscalHub.Domain.Enums;
```

**Assinatura completa:**
```csharp
public sealed class CreateTenantCommandValidator
    : AbstractValidator<CreateTenantCommand>
{
    private static readonly Regex SerieRegex = new(@"^[0-9]{1,3}$", RegexOptions.Compiled);

    public CreateTenantCommandValidator();
}
```

**Regras de validação:**

| Campo | Regras |
|---|---|
| `ClienteAppId` | `NotEmpty()` — mensagem: "ClienteAppId é obrigatório" |
| `Cnpj` | `NotEmpty()` — mensagem: "CNPJ é obrigatório" |
| `Cnpj` | `Matches(@"^[\d.\-/]{14,18}$")` — aceita com ou sem máscara |
| `RazaoSocial` | `NotEmpty(); MaximumLength(150)` |
| `RegimeTributario` | `IsInEnum()` — mensagem: "Regime tributário inválido" |
| `Ambiente` | `IsInEnum()` — mensagem: "Ambiente SEFAZ inválido (1=Produção, 2=Homologação)" |
| `UfCodigo` | `InclusiveBetween(11, 53)` — mensagem: "Código IBGE da UF inválido" |
| `Serie` | `NotEmpty(); Matches(@"^[0-9]{1,3}$")` — mensagem: "Série deve conter apenas dígitos (1 a 3 caracteres)" |
| `Endereco` | `NotNull(); ChildRules(...)` para validar `Logradouro`, `Numero`, `Bairro`, `Municipio`, `CodigoMunicipio`, `Uf`, `Cep` |
| `Endereco.Cep` | `Matches(@"^[0-9]{8}$")` — CEP sem máscara |

---

### 3.12 `Application/Tenants/Commands/UpdateTenantCertificate/UpdateTenantCertificateCommand.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed record UpdateTenantCertificateCommand : ICommand<Result>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public byte[] PfxBytes { get; init; } = default!;
    public string Senha { get; init; } = default!;
    public DateTime Vencimento { get; init; }
}
```

**Nota:** `ClienteAppId` é populado a partir de `ICurrentUserContext.ClienteAppId` no endpoint — não vem do body do request.

---

### 3.13 `Application/Tenants/Commands/UpdateTenantCertificate/UpdateTenantCertificateCommandHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class UpdateTenantCertificateCommandHandler
    : ICommandHandler<UpdateTenantCertificateCommand, Result>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICertificateEncryptionService _encryptionService;

    public UpdateTenantCertificateCommandHandler(
        ITenantRepository tenantRepository,
        IUnitOfWork unitOfWork,
        ICertificateEncryptionService encryptionService);

    public async ValueTask<Result> Handle(
        UpdateTenantCertificateCommand command,
        CancellationToken cancellationToken);
}
```

**Invariantes:**

1. Carregar Tenant pelo `TenantId`.
2. Validar `Tenant.ClienteAppId == command.ClienteAppId` — `Result.Failure(TenantErrors.NaoPertenceAoClienteApp)` se não pertencer.
3. Criptografar `PfxBytes` via `ICertificateEncryptionService.Encrypt(pfxBytes)`.
4. Criptografar `Senha` via sobrecarga de string `Encrypt(senha)`.
5. Chamar `tenant.AtualizarCertificado(pfxCriptografado, senhaCriptografada, command.Vencimento)`.
6. `_unitOfWork.SaveChangesAsync(cancellationToken)`.

---

### 3.14 `Application/Tenants/Commands/UpdateTenantCertificate/UpdateTenantCertificateCommandValidator.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCertificate`

**Usings:**
```csharp
using FluentValidation;
```

**Assinatura:**
```csharp
public sealed class UpdateTenantCertificateCommandValidator
    : AbstractValidator<UpdateTenantCertificateCommand>
{
    public UpdateTenantCertificateCommandValidator();
}
```

**Regras:**

| Campo | Regras |
|---|---|
| `TenantId` | `NotEmpty()` |
| `ClienteAppId` | `NotEmpty()` |
| `PfxBytes` | `NotNull(); Must(b => b.Length > 0 && b.Length <= 51_200)` — máximo 50KB; mensagem: "PFX deve ter entre 1 byte e 50KB" |
| `Senha` | `NotEmpty()` |
| `Vencimento` | `GreaterThan(DateTime.UtcNow)` — mensagem: "Data de vencimento deve ser futura" |

---

### 3.15 `Application/Tenants/Commands/UpdateTenantCsc/UpdateTenantCscCommand.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed record UpdateTenantCscCommand : ICommand<Result>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string Csc { get; init; } = default!;
    public string CIdToken { get; init; } = default!;
}
```

---

### 3.16 `Application/Tenants/Commands/UpdateTenantCsc/UpdateTenantCscCommandHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class UpdateTenantCscCommandHandler
    : ICommandHandler<UpdateTenantCscCommand, Result>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICertificateEncryptionService _encryptionService;

    public UpdateTenantCscCommandHandler(
        ITenantRepository tenantRepository,
        IUnitOfWork unitOfWork,
        ICertificateEncryptionService encryptionService);

    public async ValueTask<Result> Handle(
        UpdateTenantCscCommand command,
        CancellationToken cancellationToken);
}
```

**Invariantes:**

1. Carregar e validar pertencimento (mesmo padrão de `UpdateTenantCertificateCommandHandler`).
2. Criptografar CSC via `ICertificateEncryptionService.Encrypt(command.Csc)`.
3. Chamar `tenant.AtualizarCsc(cscCriptografado, command.CIdToken)`.
4. Persistir.

---

### 3.17 `Application/Tenants/Commands/UpdateTenantCsc/UpdateTenantCscCommandValidator.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Commands.UpdateTenantCsc`

**Assinatura:**
```csharp
public sealed class UpdateTenantCscCommandValidator : AbstractValidator<UpdateTenantCscCommand>
{
    public UpdateTenantCscCommandValidator();
}
```

**Regras:**

| Campo | Regras |
|---|---|
| `TenantId` | `NotEmpty()` |
| `ClienteAppId` | `NotEmpty()` |
| `Csc` | `NotEmpty(); MinimumLength(8); MaximumLength(36)` — mensagem: "CSC deve ter entre 8 e 36 caracteres" |
| `CIdToken` | `NotEmpty(); Matches(@"^[0-9]{6}$")` — mensagem: "cIdToken deve ter exatamente 6 dígitos" |

---

### 3.18 `Application/Tenants/Queries/GetTenant/GetTenantQuery.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Queries.GetTenant`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed record GetTenantQuery : IQuery<Result<TenantResponse>>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
}
```

**Nota:** `IQuery<TResponse>` é o tipo do Mediator.SourceGenerator para queries (não `IRequest<TResponse>`).

---

### 3.19 `Application/Tenants/Queries/GetTenant/GetTenantQueryHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Queries.GetTenant`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class GetTenantQueryHandler
    : IQueryHandler<GetTenantQuery, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenantRepository;

    public GetTenantQueryHandler(ITenantRepository tenantRepository);

    public async ValueTask<Result<TenantResponse>> Handle(
        GetTenantQuery query,
        CancellationToken cancellationToken);
}
```

**Invariantes:**

- Buscar via `_tenantRepository.GetByIdAsync(query.TenantId, cancellationToken)` — `AsNoTracking()` obrigatório na query de repositório.
- Validar `tenant.ClienteAppId == query.ClienteAppId` — `Result.Failure(TenantErrors.NaoPertenceAoClienteApp)` se não pertencer.
- Mapear para `TenantResponse` — **nunca** retornar a entidade.
- `TenantResponse` **não expõe** dados criptografados (`CscCriptografado`, `CertificadoPfxCriptografado`, `CertificadoSenhaCriptografada`).

---

### 3.20 `Application/Tenants/Queries/ListTenants/ListTenantsQuery.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Queries.ListTenants`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed record ListTenantsQuery : IQuery<Result<PagedResult<TenantResponse>>>
{
    public ClienteAppId ClienteAppId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
```

---

### 3.21 `Application/Tenants/Queries/ListTenants/ListTenantsQueryHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Queries.ListTenants`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class ListTenantsQueryHandler
    : IQueryHandler<ListTenantsQuery, Result<PagedResult<TenantResponse>>>
{
    private readonly ITenantRepository _tenantRepository;

    public ListTenantsQueryHandler(ITenantRepository tenantRepository);

    public async ValueTask<Result<PagedResult<TenantResponse>>> Handle(
        ListTenantsQuery query,
        CancellationToken cancellationToken);
}
```

**Invariantes:**

- `PageSize` limitado a máximo 100 no handler (não confiar apenas no validator): `pageSize = Math.Min(query.PageSize, 100)`.
- Usar `GetByClienteAppIdAsync(clienteAppId, page, pageSize)` — `AsNoTracking()` no repositório.
- Retornar `PagedResult<TenantResponse>` com `TotalCount`, `Page`, `PageSize`, `TotalPages`.
- `TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)`.

---

### 3.22 `Application/Tenants/Queries/GetCertificadoStatus/GetCertificadoStatusQuery.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
public sealed record GetCertificadoStatusQuery : IQuery<Result<CertificadoStatusResponse>>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
}
```

---

### 3.23 `Application/Tenants/Queries/GetCertificadoStatus/GetCertificadoStatusQueryHandler.cs`

**Namespace:** `VisuFiscalHub.Application.Tenants.Queries.GetCertificadoStatus`

**Usings:**
```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
public sealed class GetCertificadoStatusQueryHandler
    : IQueryHandler<GetCertificadoStatusQuery, Result<CertificadoStatusResponse>>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly TimeProvider _timeProvider;

    public GetCertificadoStatusQueryHandler(
        ITenantRepository tenantRepository,
        TimeProvider timeProvider);

    public async ValueTask<Result<CertificadoStatusResponse>> Handle(
        GetCertificadoStatusQuery query,
        CancellationToken cancellationToken);
}
```

**Invariantes:**

- Validar pertencimento ao ClienteApp.
- Calcular `DiasRestantes` usando `TimeProvider.GetUtcNow()` — nunca `DateTime.UtcNow`.
- Se `CertificadoVencimento` for `null`, retornar `DiasRestantes = null` indicando que nenhum certificado foi cadastrado.

**DTO adicional necessário:**
```csharp
public sealed record CertificadoStatusResponse(
    DateTime? VencimentoEm,
    int? DiasRestantes,
    bool TemCertificado);
```

---

### 3.24 `Application/Common/Models/ClienteAppCreatedResponse.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Models`

**Assinatura completa:**
```csharp
public sealed record ClienteAppCreatedResponse(
    Guid Id,
    string Name,
    string ClientId,
    string? WebhookUrl,
    string WebhookSecret,  // Texto claro — retornado APENAS na criação
    bool IsActive,
    DateTime CreatedAt);
```

---

### 3.25 `Application/Common/Models/ClienteAppResponse.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Models`

**Assinatura completa:**
```csharp
public sealed record ClienteAppResponse(
    Guid Id,
    string Name,
    string ClientId,
    string? WebhookUrl,
    bool IsActive,
    DateTime CreatedAt);
// WebhookSecret AUSENTE deliberadamente — irrecuperável após criação
```

---

### 3.26 `Application/Common/Models/TenantResponse.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Models`

**Usings:**
```csharp
using VisuFiscalHub.Domain.Enums;
```

**Assinatura completa:**
```csharp
public sealed record TenantResponse(
    Guid Id,
    Guid ClienteAppId,
    string Cnpj,
    string RazaoSocial,
    string? NomeFantasia,
    RegimeTributario RegimeTributario,
    AmbienteSefaz Ambiente,
    int UfCodigo,
    string Serie,
    bool TemCertificado,
    DateTime? CertificadoVencimento,
    bool IsActive,
    DateTime CreatedAt);
// CSC, PFX, senha e WebhookSecret NUNCA presentes neste DTO
```

---

### 3.27 `Application/Common/Models/PagedResult.cs`

**Namespace:** `VisuFiscalHub.Application.Common.Models`

**Assinatura completa:**
```csharp
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

---

### 3.28 `Application/Common/ResultExtensions.cs`

**Namespace:** `VisuFiscalHub.Application.Common`

**Usings:**
```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
```

**Assinatura completa:**
```csharp
public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result);
    public static IResult ToHttpResult<T>(this Result<T> result);
    public static IResult ToHttpResult<T>(
        this Result<T> result,
        Func<T, IResult> onSuccess);
}
```

**Mapeamento de Error.Code para HTTP status:**

| Padrão de `Error.Code` | HTTP Status | Justificativa |
|---|---|---|
| `*NaoEncontrado` | 404 Not Found | Recurso não existe |
| `*NaoPertenceAoClienteApp` | 403 Forbidden | Acesso negado — isolamento multi-tenant |
| `*Inativo` | 403 Forbidden | Cliente ou tenant desativado |
| `*IdempotencyKeyJaUsada` | 409 Conflict | Operação já processada |
| `*TransicaoInvalida` | 422 Unprocessable Entity | State machine violation |
| `*Validacao*` ou código de validator FluentValidation | 422 Unprocessable Entity | Dados inválidos |
| `*CnpjInvalido` | 422 Unprocessable Entity | Validação de negócio |
| `*ClientIdJaExiste` | 409 Conflict | Recurso já existe |
| `*CnpjJaCadastrado` | 409 Conflict | Recurso já existe |
| `*PrazoDeCancel amentoExpirado` | 422 Unprocessable Entity | Regra de negócio violada |
| Outros / desconhecidos | 500 Internal Server Error | Erro inesperado — logar |

**Nota de implementação:** O mapeamento é por sufixo do `Error.Code` usando `EndsWith` ou `Contains`. Usar `TypedResults.Problem(...)` para retornar `ProblemDetails` conforme RFC 7807:
```csharp
// Exemplo de mapeamento:
private static IResult MapErrorToResult(Error error)
{
    return error.Code switch
    {
        var c when c.EndsWith("NaoEncontrado") =>
            TypedResults.NotFound(new ProblemDetails { Title = error.Message, Detail = error.Code }),
        var c when c.EndsWith("NaoPertenceAoClienteApp") || c.EndsWith("Inativo") =>
            TypedResults.Forbid(),
        var c when c.EndsWith("IdempotencyKeyJaUsada") || c.EndsWith("ClientIdJaExiste") || c.EndsWith("CnpjJaCadastrado") =>
            TypedResults.Conflict(new ProblemDetails { Title = error.Message }),
        var c when c.EndsWith("TransicaoInvalida") || c.EndsWith("Validacao") || c.EndsWith("CnpjInvalido") =>
            TypedResults.UnprocessableEntity(new ProblemDetails { Title = error.Message }),
        _ => TypedResults.Problem(error.Message, statusCode: 500)
    };
}
```

---

## 4. Fluxo de Dados

### 4.1 Pipeline `CreateClienteAppCommand`

```
POST /api/v1/clientes
        │
        │  (X-Admin-Key validado no endpoint — CryptographicOperations.FixedTimeEquals)
        ▼
  [CreateClienteAppCommand]
        │
        ▼
  ValidationBehavior
  ├── IValidator<CreateClienteAppCommand>
  │     ├── Name NotEmpty                  → OK / Result.Failure
  │     ├── ClientId regex slug            → OK / Result.Failure
  │     ├── ClientSecret minLength 16      → OK / Result.Failure
  │     └── WebhookUrl HTTPS + anti-SSRF  → OK / Result.Failure
  │
  ▼ (se falha: retorna Result.Failure com erros FluentValidation)
  │
  LoggingBehavior
  │  (envolve o handler — loga apenas se IsFailure)
  ▼
  CreateClienteAppCommandHandler
  ├── 1. _clienteAppRepository.GetByClientIdAsync(clientId) — verifica unicidade
  │       └── se existe: Result.Failure(ClientIdJaExiste)
  ├── 2. PBKDF2(clientSecret, salt, 600_000, SHA256) → clientSecretHash
  ├── 3. RandomNumberGenerator.GetBytes(32) → webhookSecretHex (64 chars)
  ├── 4. _encryptionService.Encrypt(webhookSecretHex) → webhookSecretCriptografado
  ├── 5. ClienteApp.Criar(name, clientId, clientSecretHash)
  ├── 6. clienteApp.AtualizarWebhookSecret(webhookSecretCriptografado)
  ├── 7. _clienteAppRepository.AddAsync(clienteApp)
  ├── 8. _unitOfWork.SaveChangesAsync(ct)
  └── 9. return Result.Ok(ClienteAppCreatedResponse com webhookSecret em claro)
        │
        ▼
  LoggingBehavior — sem log (IsSuccess)
        │
        ▼
  ResultExtensions.ToHttpResult → TypedResults.Created(location, response)
        │
        ▼
  HTTP 201 Created
  Body: { id, name, clientId, webhookUrl, webhookSecret (ÚNICA VEZ), isActive, createdAt }
```

---

### 4.2 Pipeline `CreateTenantCommand`

```
POST /api/v1/tenants
  Headers: Authorization: Bearer JWT
        │
        │  JWT validado pelo middleware Bearer
        │  ICurrentUserContext.ClienteAppId lido do claim "sub"
        ▼
  [CreateTenantCommand]
        │
        ▼
  ValidationBehavior
  ├── ClienteAppId NotEmpty
  ├── Cnpj regex (com ou sem máscara)
  ├── RazaoSocial NotEmpty/MaxLength
  ├── RegimeTributario IsInEnum
  ├── Ambiente IsInEnum
  ├── UfCodigo 11..53
  ├── Serie ^[0-9]{1,3}$
  └── Endereco ChildRules
        │
        ▼
  CreateTenantCommandHandler
  ├── 1. _clienteAppRepository.GetByIdAsync(clienteAppId) — existência
  ├── 2. ClienteApp.IsActive check
  ├── 3. Cnpj.Criar(command.Cnpj) — valida dígitos verificadores
  ├── 4. _tenantRepository.GetByCnpjAsync(cnpj, clienteAppId) — unicidade
  ├── 5. ConfiguracaoFiscal value object construction
  ├── 6. Endereco value object construction
  ├── 7. Tenant.Criar(...) — factory DDD; adiciona TenantProvisionadoEvent
  ├── 8. _tenantRepository.AddAsync(tenant)
  ├── 9. _unitOfWork.SaveChangesAsync(ct) — commit com domain event → OutboxMessage
  ├── 10. [DDL] ExecuteSqlRawAsync(
  │         "CREATE SEQUENCE IF NOT EXISTS seq_nfe_{tenantId:N}_{serie}")
  │         onde tenantId.ToString("N") = UUID sem hífens (32 chars hex)
  │         e serie já validada com ^[0-9]{1,3}$ (2ª linha de defesa anti-SQL-injection)
  └── 11. return Result.Ok(TenantResponse)
        │
        ▼
  HTTP 201 Created
  Body: TenantResponse (sem CSC, sem PFX, sem secrets)
```

---

### 4.3 Pipeline `GetTenantQuery` (leitura)

```
GET /api/v1/tenants/{id}
  Headers: Authorization: Bearer JWT
           X-Tenant-Id: {uuid}  (opcional neste endpoint)
        │
        ▼
  [GetTenantQuery]
        │
        ▼
  ValidationBehavior (sem validators registrados → passa direto)
        │
        ▼
  GetTenantQueryHandler
  ├── _tenantRepository.GetByIdAsync(tenantId) — AsNoTracking() obrigatório
  │     └── se null: Result.Failure(TenantErrors.NaoEncontrado)
  ├── tenant.ClienteAppId == query.ClienteAppId
  │     └── se diferente: Result.Failure(TenantErrors.NaoPertenceAoClienteApp)
  └── return Result.Ok(TenantResponse)  — mapeamento manual, sem dados sensíveis
        │
        ▼
  ResultExtensions.ToHttpResult → TypedResults.Ok(tenantResponse)
        │
        ▼
  HTTP 200 OK
  Body: TenantResponse
```

---

## 5. Checklist de Conclusão

### Behaviors
- [ ] `ValidationBehavior<TMessage, TResponse>` compila com Mediator.SourceGenerator 3.0 (`IPipelineBehavior<TMessage, TResponse>` onde `TMessage : IMessage`)
- [ ] `ValidationBehavior` retorna `Result.Failure` com todos os erros FluentValidation coletados — nunca lança exceção
- [ ] `LoggingBehavior` loga apenas quando `response.IsFailure == true`
- [ ] Ambos registrados via `services.AddMediator(options => options.AddOpenBehavior(...))` — não via `AddTransient<IPipelineBehavior<,>>`
- [ ] Ordem de registro: ValidationBehavior primeiro, LoggingBehavior segundo

### HttpContextCurrentUserContext
- [ ] Implementa `ICurrentUserContext` da Application layer
- [ ] Reside em `Api/Authentication/HttpContextCurrentUserContext.cs`
- [ ] Registrado como `Scoped`
- [ ] Lê `ClienteAppId` do claim `sub` do JWT
- [ ] Lê `TenantId` de `HttpContext.Items["TenantContext"]` (record `TenantContext`)
- [ ] Nenhum handler da Application layer injeta `IHttpContextAccessor`

### CreateClienteAppCommandHandler
- [ ] PBKDF2 SHA-256 com exatamente **600.000 iterações** (não menos)
- [ ] Salt gerado com `RandomNumberGenerator.GetBytes(32)`
- [ ] `WebhookSecret` = `RandomNumberGenerator.GetBytes(32)` → hex 64 chars
- [ ] `WebhookSecret` criptografado via `ICertificateEncryptionService` antes de persistir
- [ ] `ClienteAppCreatedResponse` inclui `WebhookSecret` em texto claro
- [ ] `ClienteAppResponse` (queries) NÃO inclui `WebhookSecret`

### CreateTenantCommandHandler
- [ ] DDL executado APÓS `SaveChangesAsync` (tenant já tem Id gerado)
- [ ] `tenantId.ToString("N")` usado no nome da sequence (sem hífens, 32 chars)
- [ ] Regex `^[0-9]{1,3}$` verificado antes da interpolação DDL
- [ ] `TenantProvisionadoEvent` publicado via domain event no aggregate

### Queries
- [ ] Todos os query handlers usam `AsNoTracking()` (aplicado no repositório)
- [ ] `ListTenantsQueryHandler` tem paginação — `PageSize` máximo 100
- [ ] `GetCertificadoStatusQueryHandler` usa `TimeProvider` (não `DateTime.UtcNow`)
- [ ] Nenhum query handler retorna entidade diretamente

### ResultExtensions
- [ ] Mapeia todos os códigos de erro definidos em `Domain/Errors/`
- [ ] Usa `TypedResults` (não `Results`)
- [ ] Retorna `ProblemDetails` para erros (não string simples)
- [ ] Erro desconhecido → 500 com log

### Testes Unitários (NSubstitute)
- [ ] `CreateClienteAppCommandHandlerTests` — verifica PBKDF2, WebhookSecret em claro, unicidade ClientId
- [ ] `CreateTenantCommandHandlerTests` — verifica criação de sequence, TenantProvisionadoEvent
- [ ] `ValidationBehaviorTests` — pipeline rejeita command inválido antes do handler
- [ ] `LoggingBehaviorTests` — não loga em caso de sucesso
- [ ] `GetTenantQueryHandlerTests` — retorna 403 para tenant de outro ClienteApp
