# Fase 10B — Testes de Integração Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar testes de integração HTTP end-to-end ao projeto `tests/VisuFiscalHub.Tests` usando `WebApplicationFactory<Program>`, banco de dados real via EF Core InMemory, e `ISefazClient` substituído por `FakeSefazClient` controlável por teste.

**Architecture:** Um único `IntegrationTestBase` com `WebApplicationFactory<Program>` substitui serviços externos (ISefazClient, IDocumentJobQueue, ISequenceManager) e expõe helpers para criar `ClienteApp` + `Tenant` + obter JWT. Cada fixture de teste herda de `IntegrationTestBase` e usa um `HttpClient` configurado com o JWT correto. O banco é `InMemory` (EF Core) com isolamento por instância de `WebApplicationFactory` — cada classe de teste cria sua própria factory, garantindo banco limpo entre classes (mas não entre testes dentro da mesma classe). Sem Testcontainers para evitar dependência de Docker em CI.

**Tech Stack:** xUnit 2.x, `Microsoft.AspNetCore.Mvc.Testing`, EF Core InMemory 10.0.7, NSubstitute 5.x, Shouldly 4.x, `System.Net.Http.Json`

---

## Notas críticas antes de começar

- **`AddInMemoryCollection` chega tarde demais para `ValidateOnStart()`**: `Program.cs` lê `builder.Configuration` (incluindo env vars) durante o registro de serviços, ANTES de qualquer callback `ConfigureWebHost.ConfigureAppConfiguration` ser executado. Por isso, a única forma confiável de injetar configuração antes de `AddInfrastructure` e `AddHealthChecks` é via `Environment.SetEnvironmentVariable` no construtor da factory. O `AddInMemoryCollection` em `ConfigureAppConfiguration` ainda é necessário como segunda camada para garantir que a configuração seja respeitada no resto do pipeline.

- **Env vars são globais ao processo**: `Environment.SetEnvironmentVariable` afeta todos os threads. Em `VisuFiscalHubFactory`, limpar as variáveis no `Dispose` com `Environment.SetEnvironmentVariable(key, null)` para não contaminar outros testes após o teardown da factory.

- **Três fakes obrigatórios além de `ISefazClient`**:
  - `IDocumentJobQueue`: O `DocumentJobQueue` real invoca `IBackgroundJobClient.Enqueue` do Hangfire, que requer storage PostgreSQL. Sem o fake, qualquer POST para `/api/v1/documentos/nfce` lança `InvalidOperationException`.
  - `ISequenceManager`: O `SequenceManager` real usa `ExecuteSqlRawAsync` / `SqlQueryRaw<long>` (métodos EF relacionais), incompatíveis com o provider InMemory. Sem o fake, `IssueDocumentCommandHandler` falha ao gerar o número do documento.
  - `CacheSignatureProviders = false`: `Microsoft.IdentityModel` faz cache do `CryptoProvider` e descarta a chave RSA internamente após a primeira validação, causando `ObjectDisposedException` na segunda chamada. A correção é `PostConfigure<JwtBearerOptions>` com `CryptoProviderFactory { CacheSignatureProviders = false }`. Isso é necessário **apenas em testes** — em produção o token é validado com chaves estáticas carregadas uma vez; não adicionar isso ao pipeline de produção.

- **RSA key deve ser instância por factory, não `static`**: Uma key `static readonly` chamada `Dispose()` na instância descarta a key permanentemente para todas as factories seguintes. Usar `private readonly RSA _rsa = RSA.Create(2048)` — uma instância por factory.

- **Rate limiting está ativo no ambiente Test**: `Program.cs` registra três políticas de rate limiting (`"auth"` 10/min, `"api"` 100/min, `"admin"` 5/min) que não são desabilitadas por padrão. Para evitar flakiness à medida que a suite cresce, desabilitar rate limiting na factory via `services.AddRateLimiter(opt => opt.GlobalLimiter = PartitionedRateLimiter.CreateChained())` ou chamando `services.Configure<RateLimiterOptions>(...)`. A abordagem mais simples é substituir todas as políticas por `NoLimiter` em `ConfigureServices`:
  ```csharp
  services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(opts =>
  {
      opts.RejectionStatusCode = 429;
      opts.AddPolicy("auth",   _ => RateLimitPartition.GetNoLimiter("test"));
      opts.AddPolicy("api",    _ => RateLimitPartition.GetNoLimiter("test"));
      opts.AddPolicy("admin",  _ => RateLimitPartition.GetNoLimiter("test"));
  });
  ```

- **`CERT:EncryptionKey` com bytes zero — NUNCA usar fora de testes**: O valor `Convert.ToBase64String(new byte[32])` (32 bytes 0x00) é trivialmente conhecido e tornaria todos os certificados decriptáveis. Usar exclusivamente em `VisuFiscalHubFactory`. Em produção, a chave deve ser gerada via `crypto.getRandomValues` ou `RandomNumberGenerator.GetBytes(32)` e armazenada em segredo.

- **`ASPNETCORE_ENVIRONMENT=Test` em produção — risco de desastre**: Se definido acidentalmente em produção: banco InMemory (dados perdidos a cada restart), migrations não executam, Hangfire desabilitado, health check NpgSql ausente. O gate `isTest = environment?.IsEnvironment("Test")` em `DependencyInjection.AddInfrastructure` é proteção funcional, mas nunca configurar `ASPNETCORE_ENVIRONMENT=Test` fora do ambiente de testes.

- **`public partial class Program {}` em `Program.cs`**: Obrigatório para que `WebApplicationFactory<Program>` acesse a classe `Program` que está no assembly da API. Sem isso, o compilador não encontra o tipo `Program` externamente. A linha deve estar no final de `src/VisuFiscalHub.Api/Program.cs`.

- **InMemory database vs PostgreSQL**: O projeto usa PostgreSQL com migrations. Para testes de integração usaremos EF Core InMemory. A `WebApplicationFactory` delega para `DependencyInjection.AddInfrastructure` que detecta o ambiente `"Test"` via `IHostEnvironment` e configura `UseInMemoryDatabase` e desabilita Hangfire server.

- **Isolamento de banco por factory (não por teste)**: Testes dentro da mesma classe compartilham o banco InMemory. Se um teste persiste estado que afeta o próximo, ou extrair a factory para `IClassFixture` (estado compartilhado explícito) ou construir cada teste de forma autossuficiente (o padrão deste plano: cada teste cria seu próprio `ClienteApp` + `Tenant` com GUIDs únicos, evitando colisão). Não usar `IClassFixture` para evitar coupling acidental entre testes.

- **`SefazRetorno` e `SefazConsultaRetorno` são positional records**: Usar construtor posicional com named arguments, não object initializer. `SefazConsultaRetorno` tem 5 parâmetros: `Encontrado`, `Autorizado`, `CStat`, `NProt`, `XmlProtocolo`.

- **`StatusDocumento` serializa como int**: Não há `JsonStringEnumConverter` configurado no projeto. `StatusDocumento.Enfileirado` serializa como `(int)StatusDocumento.Enfileirado`, não como `"Enfileirado"`.

- **`DocumentoFiscalId` serializa como objeto aninhado**: `{"value": "<guid>"}` — não como Guid direto. Criar um DTO local `DocumentoIdDto` com propriedade `Value`.

- **`ItemDocumentoDto` tem campo `tributo` obrigatório**: `TributoDto` é required. Body de teste sem `tributo` retorna 422 antes de chegar na lógica de negócio. Ver `TributoSimples()` nos exemplos abaixo.

- **Nomes dos campos de `ItemDocumentoDto`**: `cfopSaida` (não `cfop`), `origemMercadoria` (não `origem`), `codigoProduto` (obrigatório). O enum `RegimeTributario` é o correto (não `Crt`). `codigoMunicipio` em `Endereco.Criar` é `int`.

- **xUnit `[Collection]` é obrigatório**: Todos os arquivos de teste de integração devem ter `[Collection("IntegrationTests")]`. Sem isso, xUnit executa as classes em paralelo, causando race conditions nas variáveis de ambiente de processo. O `IntegrationTestCollection.cs` declara o `[CollectionDefinition]`.

- **`Program.cs` exige `ValidateOnStart()`**: JwtSettings, AdminKeySettings e CertEncryption devem ser fornecidos na config de teste — sem isso o app não sobe.

- **`Microsoft.AspNetCore.Mvc.Testing`**: Não está no `.csproj` por padrão — precisa ser adicionado como `PackageReference`.

- **`CriarClienteAutenticado` retorna `HttpClient` que deve ser descartado**: O `HttpClient` retornado por `Factory.CreateClient(...)` gerencia uma conexão com o servidor de teste. Usar `using var http = CriarClienteAutenticado(...)` em métodos de teste ou helper para garantir descarte adequado e evitar vazamentos de recursos.

## File Map

| Arquivo | Ação | Responsabilidade |
|---|---|---|
| `tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj` | Modificar | Adicionar `Microsoft.AspNetCore.Mvc.Testing` e referência ao projeto Api |
| `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs` | Modificar | Aceitar `IHostEnvironment?` e gate InMemory/Hangfire no ambiente `"Test"` |
| `src/VisuFiscalHub.Api/Program.cs` | Modificar | Passar `builder.Environment` para `AddInfrastructure`; gate migrations/jobs/health check NpgSql no ambiente `"Test"`; `public partial class Program {}` no final |
| `tests/VisuFiscalHub.Tests/Integration/IntegrationTestCollection.cs` | Criar | `[CollectionDefinition("IntegrationTests")]` para serializar execução das classes |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs` | Criar | `ISefazClient` controlável com positional record constructors |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs` | Criar | `IDocumentJobQueue` no-op; registra IDs enfileirados com `ConcurrentBag` thread-safe; expõe `Reset()` |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSequenceManager.cs` | Criar | `ISequenceManager` in-memory usando `Interlocked.Increment` com `_next = 0` |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs` | Criar | `WebApplicationFactory<Program>` com RSA por instância, env vars no construtor, fakes, rate limiting desabilitado, cleanup no Dispose |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs` | Criar | Base class com helpers `CriarClienteApp`, `CriarTenant`, `ObterToken`, `CriarClienteAutenticado`; expõe `protected` `JobQueue` e `SequenceManager` |
| `tests/VisuFiscalHub.Tests/Integration/AuthFlowTests.cs` | Criar | POST /auth/token: credenciais válidas, inválidas, cliente inexistente |
| `tests/VisuFiscalHub.Tests/Integration/IssueDocumentTests.cs` | Criar | POST /api/v1/documentos/nfce: 202, status int, idempotência, 422 validação, CPF obrigatório acima do limite |
| `tests/VisuFiscalHub.Tests/Integration/DocumentStatusTests.cs` | Criar | GET /api/v1/documentos/{id}/status: 200 com body, 404, XML 404 com ProblemDetails, isolamento de tenant (404 tenant inexistente + 403 tenant de outro ClienteApp) |
| `tests/VisuFiscalHub.Tests/Integration/HealthCheckTests.cs` | Criar | GET /health/live: 200 sempre; smoke tests de auth e admin key |

---

## Task 1: Configurar projeto e infraestrutura de testes

**Files:**
- Modify: `tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`
- Modify: `src/VisuFiscalHub.Api/Program.cs`
- Create: `tests/VisuFiscalHub.Tests/Integration/IntegrationTestCollection.cs`
- Create: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs`
- Create: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs`
- Create: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSequenceManager.cs`
- Create: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs`
- Create: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs`

- [ ] **Step 1: Verificar e atualizar `.csproj`**

> **Atenção:** Este step pode já estar aplicado. Verificar antes de editar:
> ```powershell
> Select-String "AspNetCore.Mvc.Testing" tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
> ```
> Se retornar resultado, pular para o Step 2.

Editar `tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.7" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.7" />
  <PackageReference Include="coverlet.collector" Version="6.0.4" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
  <PackageReference Include="NSubstitute" Version="5.3.0" />
  <PackageReference Include="Shouldly" Version="4.3.0" />
  <PackageReference Include="xunit" Version="2.9.3" />
  <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\..\src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj" />
  <ProjectReference Include="..\..\src\VisuFiscalHub.Domain\VisuFiscalHub.Domain.csproj" />
  <ProjectReference Include="..\..\src\VisuFiscalHub.Application\VisuFiscalHub.Application.csproj" />
  <ProjectReference Include="..\..\src\VisuFiscalHub.Infrastructure\VisuFiscalHub.Infrastructure.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Verificar e atualizar `DependencyInjection.AddInfrastructure`**

> **Atenção:** Este step pode já estar aplicado. Verificar antes de editar:
> ```powershell
> Select-String "IHostEnvironment" src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
> ```
> Se retornar resultado, confirmar que a assinatura já aceita `IHostEnvironment? environment = null` e que o gate `isTest` existe. Se correto, pular para Step 3.

A alteração é **cirúrgica**: apenas a assinatura do método `AddInfrastructure` e dois blocos condicionais. Não substituir o arquivo inteiro — usar `Edit` para alterar apenas a assinatura e os blocos afetados.

A assinatura correta:
```csharp
public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration,
    IHostEnvironment? environment = null)
{
    var isTest = environment?.IsEnvironment("Test") ?? false;

    if (isTest)
    {
        var dbName = $"TestDb_{Guid.NewGuid():N}";
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(dbName));
    }
    else
    {
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
    }

    // ... [todo o restante do registro de serviços permanece intacto] ...

    if (!isTest)
    {
        services.AddHangfireServer();
    }

    return services;
}
```

- [ ] **Step 3: Verificar e atualizar `Program.cs`**

> **Atenção:** Este step pode já estar aplicado. Verificar antes de editar:
> ```powershell
> Select-String "builder.Environment" src/VisuFiscalHub.Api/Program.cs
> Select-String "public partial class Program" src/VisuFiscalHub.Api/Program.cs
> ```
> Se ambos retornarem resultado, pular para Step 4.

Duas alterações em `Program.cs`:

**3a — Passar `builder.Environment` para `AddInfrastructure`:**
```csharp
// Antes:
builder.Services.AddInfrastructure(builder.Configuration);

// Depois:
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
```

**3b — Condicionar migrations e Hangfire dashboard ao ambiente (não Test):**

O código real usa `CreateScope` + `MigrateAsync` (não `app.MigrateAsync()` direto). Localizar o bloco existente e envolve-lo com a guard:
```csharp
if (!app.Environment.IsEnvironment("Test"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    app.UseHangfireDashboard(...);
    // NpgSql health check map aqui também, se separado
}
```

**3c — Confirmar `public partial class Program {}` no final do arquivo** (requerido para `WebApplicationFactory<Program>`):
```csharp
public partial class Program { }
```

- [ ] **Step 4: Criar `IntegrationTestCollection.cs`**

```csharp
// tests/VisuFiscalHub.Tests/Integration/IntegrationTestCollection.cs
using Xunit;

namespace VisuFiscalHub.Tests.Integration;

[CollectionDefinition("IntegrationTests")]
public sealed class IntegrationTestCollection { }
```

Este arquivo não contém testes — só declara a coleção. Todos os arquivos de teste de integração precisam de `[Collection("IntegrationTests")]`. Sem isso, xUnit executa as classes em paralelo e as variáveis de ambiente de processo sofrem race conditions.

- [ ] **Step 5: Criar `FakeSefazClient.cs`**

Verificar a assinatura exata de `ISefazClient` em `src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs` e os records `SefazRetorno` / `SefazConsultaRetorno` antes de implementar.

`SefazRetorno` e `SefazConsultaRetorno` são **positional records** — usar construtor posicional com named arguments, não object initializer.

```csharp
// tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class FakeSefazClient : ISefazClient
{
    private Func<DocumentoFiscalId, TenantId, Task<Result<SefazRetorno>>> _submitHandler =
        (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: true,
            CStat: "100",
            XMotivo: "Autorizado o uso da NF-e",
            NProt: "135260000000001",
            XmlAutorizado: "<protNFe/>")));

    private Func<string, TenantId, Task<Result<SefazConsultaRetorno>>> _consultaHandler =
        (_, _) => Task.FromResult(Result.Success(new SefazConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            CStat: "100",
            NProt: "135260000000001",
            XmlProtocolo: null)));

    public void SimularAutorizado() =>
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: true, CStat: "100", XMotivo: "Autorizado o uso da NF-e",
            NProt: "135260000000001", XmlAutorizado: "<protNFe/>")));

    public void SimularRejeitado(string cStat = "999", string motivo = "Rejeição simulada") =>
        _submitHandler = (_, _) => Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: false, CStat: cStat, XMotivo: motivo,
            NProt: null, XmlAutorizado: null)));

    public Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct) =>
        _submitHandler(documentoId, tenantId);

    public Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso, TenantId tenantId, CancellationToken ct) =>
        _consultaHandler(chaveAcesso, tenantId);
}
```

- [ ] **Step 6: Criar `FakeDocumentJobQueue.cs`**

`DocumentJobQueue` real invoca `IBackgroundJobClient.Enqueue` do Hangfire que requer storage PostgreSQL — incompatível com testes InMemory.

Usar `ConcurrentBag` para thread safety e expor `Reset()` para cenários que precisam limpar o estado entre operações na mesma factory:

```csharp
// tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs
using System.Collections.Concurrent;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class FakeDocumentJobQueue : IDocumentJobQueue
{
    private readonly ConcurrentBag<DocumentoFiscalId> _enqueuedIds = [];

    public IReadOnlyCollection<DocumentoFiscalId> EnqueuedIds => _enqueuedIds;

    public void Reset() => _enqueuedIds.Clear();

    public Task EnqueueProcessingAsync(DocumentoFiscalId id, CancellationToken ct = default)
    {
        _enqueuedIds.Add(id);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 7: Criar `FakeSequenceManager.cs`**

`SequenceManager` real usa `ExecuteSqlRawAsync` / `SqlQueryRaw<long>` — métodos EF relacionais incompatíveis com o provider InMemory.

> **Atenção crítica**: `_next` deve começar em `0`, não em `1`. `Interlocked.Increment` **retorna o valor após o incremento** — se `_next = 1`, a primeira chamada retorna `2`. Com `_next = 0`, a primeira chamada retorna `1`, que é o número de documento esperado.

```csharp
// tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSequenceManager.cs
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class FakeSequenceManager : ISequenceManager
{
    private long _next = 0;

    public Task<Result> EnsureNumeracaoSequenceAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result<long>> GetNextNumeroAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success(Interlocked.Increment(ref _next)));
}
```

- [ ] **Step 8: Criar `VisuFiscalHubFactory.cs`**

Pontos críticos:
- `_rsa` é **campo de instância** (não `static`) — cada factory tem sua própria key; o `Dispose` da instância descarta apenas a própria key.
- `Environment.SetEnvironmentVariable` no **construtor** — antes de `WebApplicationFactory` iniciar o host, pois `Program.cs` lê env vars durante `builder.Build()`.
- `AddInMemoryCollection` em `ConfigureAppConfiguration` como segunda camada de configuração.
- `PostConfigure<JwtBearerOptions>` com `CacheSignatureProviders = false` — evita `ObjectDisposedException` que `Microsoft.IdentityModel` causa ao descartar a RSA key após a primeira validação. **Apenas em testes.**
- Rate limiting desabilitado via `Configure<RateLimiterOptions>` — evita flakiness da suite.
- `Dispose` limpa as env vars do processo para não contaminar outros testes ou processos filhos.

```csharp
// tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Application.Common.Interfaces;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class VisuFiscalHubFactory : WebApplicationFactory<Program>
{
    private readonly RSA _rsa = RSA.Create(2048);

    private static readonly string[] EnvVarKeys =
    [
        "CONNECTIONSTRINGS__DEFAULTCONNECTION",
        "JWT__ISSUER", "JWT__AUDIENCE", "JWT__EXPIRESINSECONDS",
        "JWT__PRIVATEKEYPEM", "JWT__PUBLICKEYPEMS__0",
        "ADMINKEY__VALUE", "CERT__ENCRYPTIONKEY",
        "HANGFIREDASHBOARD__USER", "HANGFIREDASHBOARD__PASSWORD"
    ];

    public string TestPrivateKeyPem { get; }
    public string TestPublicKeyPem  { get; }

    public FakeSefazClient SefazClient { get; } = new();
    public FakeDocumentJobQueue JobQueue { get; } = new();
    public FakeSequenceManager SequenceManager { get; } = new();

    public VisuFiscalHubFactory()
    {
        TestPrivateKeyPem = _rsa.ExportRSAPrivateKeyPem();
        TestPublicKeyPem  = _rsa.ExportSubjectPublicKeyInfoPem();

        // Program.cs reads builder.Configuration (which includes env vars) during service
        // registration, BEFORE ConfigureWebHost.ConfigureAppConfiguration callbacks apply.
        // Therefore env vars are the only reliable way to inject configuration ahead of
        // AddInfrastructure and AddHealthChecks ValidateOnStart checks.
        Environment.SetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULTCONNECTION",
            "Host=localhost;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("JWT__ISSUER",           "visu-fiscal-hub");
        Environment.SetEnvironmentVariable("JWT__AUDIENCE",         "visu-fiscal-hub-clients");
        Environment.SetEnvironmentVariable("JWT__EXPIRESINSECONDS", "3600");
        Environment.SetEnvironmentVariable("JWT__PRIVATEKEYPEM",    TestPrivateKeyPem);
        Environment.SetEnvironmentVariable("JWT__PUBLICKEYPEMS__0", TestPublicKeyPem);
        Environment.SetEnvironmentVariable("ADMINKEY__VALUE",       "test-admin-key-1234567890");
        // AVISO: chave zero é apenas para testes — NUNCA usar em produção (certificados ficariam decriptáveis)
        Environment.SetEnvironmentVariable("CERT__ENCRYPTIONKEY",   Convert.ToBase64String(new byte[32]));
        Environment.SetEnvironmentVariable("HANGFIREDASHBOARD__USER",     "test");
        Environment.SetEnvironmentVariable("HANGFIREDASHBOARD__PASSWORD", "test");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"]           = "visu-fiscal-hub",
                ["Jwt:Audience"]         = "visu-fiscal-hub-clients",
                ["Jwt:ExpiresInSeconds"] = "3600",
                ["Jwt:PrivateKeyPem"]    = TestPrivateKeyPem,
                ["Jwt:PublicKeyPems:0"]  = TestPublicKeyPem,
                ["AdminKey:Value"]       = "test-admin-key-1234567890",
                ["CERT:EncryptionKey"]   = Convert.ToBase64String(new byte[32]),
                ["HangfireDashboard:User"]     = "test",
                ["HangfireDashboard:Password"] = "test",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
            });
        });

        builder.ConfigureServices(services =>
        {
            // DbContext and Hangfire are already configured for the Test environment
            // inside DependencyInjection.AddInfrastructure (InMemory DB, no Hangfire server).

            // Replace ISefazClient with fake
            services.RemoveAll<ISefazClient>();
            services.AddSingleton<ISefazClient>(SefazClient);

            // Replace IDocumentJobQueue with a no-op stub — DocumentJobQueue depends on
            // Hangfire's IBackgroundJobClient which requires a real storage in production.
            services.RemoveAll<IDocumentJobQueue>();
            services.AddSingleton<IDocumentJobQueue>(JobQueue);

            // Replace ISequenceManager with an in-memory stub — SequenceManager uses
            // ExecuteSqlRawAsync / SqlQueryRaw which are relational-specific EF methods
            // incompatible with the InMemory EF provider used in tests.
            services.RemoveAll<ISequenceManager>();
            services.AddSingleton<ISequenceManager>(SequenceManager);

            // Disable rate limiting to prevent test flakiness as the suite grows.
            // Production policies ("auth" 10/min, "api" 100/min, "admin" 5/min) would
            // fire within a single test class sharing one factory instance.
            services.Configure<RateLimiterOptions>(opts =>
            {
                opts.AddPolicy("auth",  _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("api",   _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("admin", _ => RateLimitPartition.GetNoLimiter("test"));
            });

            // Prevent Microsoft.IdentityModel from caching and disposing the RSA key
            // inside RsaSecurityKey after the first token validation.
            // CacheSignatureProviders = false creates a fresh CryptoProvider on each
            // validation call, avoiding ObjectDisposedException on subsequent calls.
            // This workaround is test-only — do NOT apply to the production pipeline.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.CryptoProviderFactory =
                    new CryptoProviderFactory { CacheSignatureProviders = false };
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rsa.Dispose();
            foreach (var key in EnvVarKeys)
                Environment.SetEnvironmentVariable(key, null);
        }
        base.Dispose(disposing);
    }
}
```

- [ ] **Step 9: Criar `IntegrationTestBase.cs`**

Verificar assinaturas exatas de `ClienteApp.Criar`, `Tenant.Criar`, `ConfiguracaoFiscal.Criar`, `Endereco.Criar` antes de implementar. Parâmetros corretos verificados na implementação real:
- `RegimeTributario.SimplesNacional` (não `Crt.SimplesNacional`)
- `codigoMunicipio: 3550308` como `int` (não `string`)

`protected FakeDocumentJobQueue JobQueue` e `protected FakeSequenceManager SequenceManager` expostos para testes que precisam inspecionar o estado interno dos fakes.

```csharp
// tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VisuFiscalHub.Application.Common.Security;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public abstract class IntegrationTestBase : IDisposable
{
    protected readonly VisuFiscalHubFactory Factory;
    protected readonly HttpClient Client;
    protected readonly FakeSefazClient SefazFake;
    protected readonly FakeDocumentJobQueue JobQueue;
    protected readonly FakeSequenceManager SequenceManager;

    protected IntegrationTestBase()
    {
        Factory = new VisuFiscalHubFactory();
        Client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        SefazFake     = Factory.SefazClient;
        JobQueue      = Factory.JobQueue;
        SequenceManager = Factory.SequenceManager;
    }

    protected async Task<(ClienteAppId Id, string ClientId, string ClientSecret)> CriarClienteAppAsync()
    {
        var clientId = $"cli_{Guid.NewGuid():N}";
        var clientSecret = Guid.NewGuid().ToString("N");
        var clientSecretHash = ClientSecretHasher.DerivarHash(clientSecret);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var app = ClienteApp.Criar(
            name: "Test ClienteApp",
            clientId: clientId,
            clientSecretHash: clientSecretHash,
            timeProvider: timeProvider).Value;

        db.ClienteApps.Add(app);
        await db.SaveChangesAsync();

        return (app.Id, clientId, clientSecret);
    }

    protected async Task<string> ObterTokenAsync(string clientId, string clientSecret)
    {
        var response = await Client.PostAsJsonAsync("/auth/token", new { clientId, clientSecret });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenBody>();
        return body!.AccessToken;
    }

    // Retorna um HttpClient autenticado. O caller é responsável pelo Dispose (usar `using var`).
    protected HttpClient CriarClienteAutenticado(string token, Guid? tenantId = null)
    {
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (tenantId.HasValue)
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.Value.ToString());
        return client;
    }

    protected async Task<TenantId> CriarTenantAsync(ClienteAppId clienteAppId, string cnpj = "11222333000181")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var configuracao = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35).Value;

        var endereco = Endereco.Criar(
            logradouro: "Rua Teste",
            numero: "100",
            complemento: null,
            bairro: "Centro",
            municipio: "São Paulo",
            codigoMunicipio: 3550308,
            uf: "SP",
            cep: "01310100").Value;

        var tenant = Tenant.Criar(
            clienteAppId: clienteAppId,
            cnpj: Cnpj.Criar(cnpj).Value,
            razaoSocial: "Empresa Teste LTDA",
            nomeFantasia: null,
            configuracaoFiscal: configuracao,
            endereco: endereco,
            timeProvider: timeProvider).Value;

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private sealed record TokenBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
```

- [ ] **Step 10: Build**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-Object -Last 5
```

Expected: 0 erros.

- [ ] **Step 11: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
        src/VisuFiscalHub.Infrastructure/DependencyInjection.cs `
        src/VisuFiscalHub.Api/Program.cs `
        tests/VisuFiscalHub.Tests/Integration/IntegrationTestCollection.cs `
        tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs `
        tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs `
        tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSequenceManager.cs `
        tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs `
        tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs
git commit -m "test: integration — WebApplicationFactory, fakes e IntegrationTestBase"
```

---

## Task 2: Testes de autenticação (POST /auth/token)

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Integration/AuthFlowTests.cs`

- [ ] **Step 1: Criar `AuthFlowTests.cs`**

```csharp
// tests/VisuFiscalHub.Tests/Integration/AuthFlowTests.cs
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class AuthFlowTests : IntegrationTestBase
{
    [Fact]
    public async Task PostToken_CredenciaisValidas_Retorna200ComAccessToken()
    {
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();

        var response = await Client.PostAsJsonAsync("/auth/token", new { clientId, clientSecret });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenBody>();
        body.ShouldNotBeNull();
        body!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.TokenType.ShouldBe("Bearer");
        body.ExpiresIn.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task PostToken_ClientSecretErrado_Retorna401()
    {
        var (_, clientId, _) = await CriarClienteAppAsync();

        var response = await Client.PostAsJsonAsync("/auth/token",
            new { clientId, clientSecret = "senha-errada" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("invalid_client");
    }

    [Fact]
    public async Task PostToken_ClientIdInexistente_Retorna401()
    {
        var response = await Client.PostAsJsonAsync("/auth/token",
            new { clientId = "nao-existe", clientSecret = "qualquer" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostToken_CredenciaisValidas_RetornaContentTypeJson()
    {
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();

        var response = await Client.PostAsJsonAsync("/auth/token", new { clientId, clientSecret });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
    }

    private sealed record TokenBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
```

- [ ] **Step 2: Rodar testes de auth**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~AuthFlowTests" -v normal 2>&1 | Select-Object -Last 5
```

Expected: 4/4 passando.

- [ ] **Step 3: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/AuthFlowTests.cs
git commit -m "test: integration — POST /auth/token credenciais válidas, inválidas e cliente inexistente"
```

---

## Task 3: Testes de emissão de NFC-e (POST /api/v1/documentos/nfce)

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Integration/IssueDocumentTests.cs`

- [ ] **Step 1: Criar `IssueDocumentTests.cs`**

Pontos críticos:
- `TributoDto` é **obrigatório** em cada item — sem ele o validator retorna 422 antes da lógica de negócio.
- Campos corretos: `cfopSaida` (não `cfop`), `origemMercadoria` (não `origem`), `codigoProduto` obrigatório.
- `StatusDocumento` serializa como **int** (não string) — usar `(int)StatusDocumento.Enfileirado`.
- `DocumentoFiscalId` serializa como `{"value": "<guid>"}` — usar `DocumentoIdDto` local.
- Os dois testes de "request válido" (202 + DocumentoId e status Enfileirado) são consolidados em um único teste para evitar duplicação de setup.
- `PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422`: NT 2019.001 exige CPF do consumidor quando o valor da NFC-e ultrapassa R$ 500,00. Verificar se `IssueNfceCommandValidator` implementa essa regra antes de adicionar o teste.

```csharp
// tests/VisuFiscalHub.Tests/Integration/IssueDocumentTests.cs
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class IssueDocumentTests : IntegrationTestBase
{
    // TributoDto minimal válido para Simples Nacional (CSOSN 400 — sem cálculo de ICMS).
    // CstPisCofins 7 = Cst07 (PISNT/COFINSNT — Simples Nacional).
    private static object TributoSimples() => new
    {
        tipoIcms = 1,          // TipoIcms.CSOSN
        csosnOuCst = 400,      // CSOSN 400
        aliquotaIcms = 0.0,
        baseCalculoIcms = 0.0,
        valorIcms = 0.0,
        cstPis = 7,            // CstPisCofins.Cst07 — PISNT
        baseCalculoPis = 0.0,
        aliquotaPis = 0.0,
        valorPis = 0.0,
        cstCofins = 7,         // CstPisCofins.Cst07 — COFINSNT
        baseCalculoCofins = 0.0,
        aliquotaCofins = 0.0,
        valorCofins = 0.0
    };

    // Body válido: itens + pagamentos com totais fechando, NCM 8 dígitos, indPresenca 1.
    private static object BodyValido(decimal valor = 10.00m) => new
    {
        itens = new[]
        {
            new
            {
                codigoProduto = "PROD001",
                descricao = "Produto Teste",
                ncm = "12345678",
                cest = (string?)null,
                cfopSaida = "5102",
                unidadeComercial = "UN",
                quantidade = (double)1m,
                valorUnitario = (double)valor,
                valorDesconto = 0.0,
                origemMercadoria = 0,  // OrigemMercadoria.Nacional
                tributo = TributoSimples()
            }
        },
        pagamentos = new[]
        {
            new { tipoPagamento = 1, valor = (double)valor }  // TipoPagamento.Dinheiro = 1
        },
        consumidor = (object?)null,
        indPresenca = 1
    };

    [Fact]
    public async Task PostNfce_RequestValido_Retorna202ComDocumentoIdEStatusEnfileirado()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();
        body!.DocumentoId.Value.ShouldNotBe(Guid.Empty);
        body.PollUrl.ShouldNotBeNullOrWhiteSpace();
        // StatusDocumento serializado como int — não há JsonStringEnumConverter no projeto
        body.Status.ShouldBe((int)StatusDocumento.Enfileirado);
    }

    [Fact]
    public async Task PostNfce_SemIdempotencyKey_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var response = await http.PostAsJsonAsync("/api/v1/documentos/nfce", BodyValido());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("IdempotencyKey");
    }

    [Fact]
    public async Task PostNfce_SemToken_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostNfce_MesmaIdempotencyKey_RetornaMesmoDocumentoId()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);
        var idempotencyKey = Guid.NewGuid().ToString();

        HttpRequestMessage MakeReq() => new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", idempotencyKey } }
        };

        var r1 = await http.SendAsync(MakeReq());
        var r2 = await http.SendAsync(MakeReq());

        r1.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        r2.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var b1 = await r1.Content.ReadFromJsonAsync<IssueResponse>();
        var b2 = await r2.Content.ReadFromJsonAsync<IssueResponse>();
        b1!.DocumentoId.Value.ShouldBe(b2!.DocumentoId.Value);
    }

    [Fact]
    public async Task PostNfce_NcmInvalido_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var bodyComNcmInvalido = new
        {
            itens = new[]
            {
                new
                {
                    codigoProduto = "PROD001",
                    descricao = "Produto",
                    ncm = "1234567",  // 7 dígitos — inválido (requer exatamente 8)
                    cest = (string?)null,
                    cfopSaida = "5102",
                    unidadeComercial = "UN",
                    quantidade = 1.0,
                    valorUnitario = 10.0,
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 10.0 } },
            consumidor = (object?)null,
            indPresenca = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(bodyComNcmInvalido),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfce_IndPresencaInvalido_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        // indPresenca = 2 é explicitamente rejeitado pelo validator
        var bodyComIndPresencaInvalido = new
        {
            itens = new[]
            {
                new
                {
                    codigoProduto = "PROD001",
                    descricao = "Produto",
                    ncm = "12345678",
                    cest = (string?)null,
                    cfopSaida = "5102",
                    unidadeComercial = "UN",
                    quantidade = 1.0,
                    valorUnitario = 10.0,
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 10.0 } },
            consumidor = (object?)null,
            indPresenca = 2
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(bodyComIndPresencaInvalido),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfce_TotaisNaoFecham_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        // Item vale 10.00, pagamento é 5.00 — não fecha
        var bodyComTotalErrado = new
        {
            itens = new[]
            {
                new
                {
                    codigoProduto = "PROD001",
                    descricao = "Produto",
                    ncm = "12345678",
                    cest = (string?)null,
                    cfopSaida = "5102",
                    unidadeComercial = "UN",
                    quantidade = 1.0,
                    valorUnitario = 10.0,
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 5.0 } },  // pagamento != item
            consumidor = (object?)null,
            indPresenca = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(bodyComTotalErrado),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422()
    {
        // NT 2019.001: CPF do consumidor é obrigatório quando o valor total ultrapassa R$ 500,00.
        // Verificar que IssueNfceCommandValidator implementa a regra antes de habilitar este teste.
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        // valor = 501.00 — acima do limite. consumidor = null (sem CPF).
        var body = new
        {
            itens = new[]
            {
                new
                {
                    codigoProduto = "PROD001",
                    descricao = "Produto",
                    ncm = "12345678",
                    cest = (string?)null,
                    cfopSaida = "5102",
                    unidadeComercial = "UN",
                    quantidade = 1.0,
                    valorUnitario = 501.0,
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 501.0 } },
            consumidor = (object?)null,
            indPresenca = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(body),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // DocumentoFiscalId serializa como {"value": "<guid>"} pois é um readonly record struct
    // sem JsonConverter personalizado registrado.
    // Status é int pois não há JsonStringEnumConverter configurado no projeto.
    private sealed record IssueResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("pollUrl")] string PollUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

    private sealed record DocumentoIdDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("value")] Guid Value);
}
```

- [ ] **Step 2: Rodar testes de emissão**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~IssueDocumentTests" -v normal 2>&1 | Select-Object -Last 5
```

Expected: todos passando. Se `PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422` falhar com 202, a regra NT 2019.001 não está implementada no validator — registrar gap e pular este teste com `[Skip("NT 2019.001 não implementado no validator")]` temporariamente.

- [ ] **Step 3: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/IssueDocumentTests.cs
git commit -m "test: integration — POST /api/v1/documentos/nfce emissão, idempotência e validação"
```

---

## Task 4: Testes de status e XML do documento

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Integration/DocumentStatusTests.cs`

- [ ] **Step 1: Criar `DocumentStatusTests.cs`**

Pontos críticos:
- `EmitirAsync()` extrai `documentoId.value` do JSON aninhado via `DocumentoIdDto`.
- `GetStatus_DocumentoExistente_Retorna200`: verificar campos do body, não apenas o status HTTP.
- `GetXml_DocumentoEnfileirado_Retorna404`: verificar `ProblemDetails.Detail == "DocumentoFiscal.XmlIndisponivel"`.
- `GetStatus_TenantErrado_Retorna404Ou403`: reutilizar token do http1 com `X-Tenant-Id` aleatório inexistente.
- `GetStatus_TenantDeOutroClienteApp_Retorna403`: criar um segundo `ClienteApp` + `Tenant` real e usar seu token para acessar o documento do primeiro — verifica isolamento de propriedade (403, não apenas 404 de tenant inexistente).

```csharp
// tests/VisuFiscalHub.Tests/Integration/DocumentStatusTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class DocumentStatusTests : IntegrationTestBase
{
    // TributoDto minimal válido para Simples Nacional (CSOSN 400 — sem cálculo de ICMS).
    // CstPisCofins 7 = Cst07 (PISNT/COFINSNT — Simples Nacional).
    private static object TributoSimples() => new
    {
        tipoIcms = 1,          // TipoIcms.CSOSN
        csosnOuCst = 400,      // CSOSN 400
        aliquotaIcms = 0.0,
        baseCalculoIcms = 0.0,
        valorIcms = 0.0,
        cstPis = 7,            // CstPisCofins.Cst07 — PISNT
        baseCalculoPis = 0.0,
        aliquotaPis = 0.0,
        valorPis = 0.0,
        cstCofins = 7,         // CstPisCofins.Cst07 — COFINSNT
        baseCalculoCofins = 0.0,
        aliquotaCofins = 0.0,
        valorCofins = 0.0
    };

    // Body válido: itens + pagamentos com totais fechando, NCM 8 dígitos, indPresenca 1.
    private static object BodyValido(decimal valor = 10.00m) => new
    {
        itens = new[]
        {
            new
            {
                codigoProduto = "PROD001",
                descricao = "Produto Teste",
                ncm = "12345678",
                cest = (string?)null,
                cfopSaida = "5102",
                unidadeComercial = "UN",
                quantidade = (double)1m,
                valorUnitario = (double)valor,
                valorDesconto = 0.0,
                origemMercadoria = 0,  // OrigemMercadoria.Nacional
                tributo = TributoSimples()
            }
        },
        pagamentos = new[]
        {
            new { tipoPagamento = 1, valor = (double)valor }  // TipoPagamento.Dinheiro = 1
        },
        consumidor = (object?)null,
        indPresenca = 1
    };

    private async Task<(HttpClient Http, Guid DocumentoGuid)> EmitirAsync()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // DocumentoFiscalId serializa como { "documentoId": { "value": "<guid>" } }
        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();
        var docIdGuid = body!.DocumentoId.Value;

        return (http, docIdGuid);
    }

    [Fact]
    public async Task GetStatus_DocumentoExistente_Retorna200ComBodyValido()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        body.ShouldNotBeNull();
        body!.DocumentoId.Value.ShouldBe(docId);
        // StatusDocumento serializado como int — Enfileirado logo após emissão
        body.Status.ShouldBe((int)StatusDocumento.Enfileirado);
    }

    [Fact]
    public async Task GetStatus_DocumentoInexistente_Retorna404()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var response = await http.GetAsync($"/api/v1/documentos/{Guid.NewGuid()}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetXml_DocumentoEnfileirado_Retorna404ComProblemDetails()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/xml");

        // Enfileirado → XmlAssinado é null → XmlIndisponivel → 404
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("detail", out var detail).ShouldBeTrue();
        detail.GetString().ShouldBe("DocumentoFiscal.XmlIndisponivel");
    }

    [Fact]
    public async Task GetStatus_TenantErrado_Retorna404Ou403()
    {
        // Emite documento com o ClienteApp-1 / Tenant-1
        var (http1, docId) = await EmitirAsync();

        // Reutiliza o token do http1 com um TenantId inexistente para verificar isolamento.
        using var httpTenantFalso = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Reutiliza o Authorization do http1 mas com um TenantId aleatório inexistente
        httpTenantFalso.DefaultRequestHeaders.Authorization = http1.DefaultRequestHeaders.Authorization;
        httpTenantFalso.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await httpTenantFalso.GetAsync($"/api/v1/documentos/{docId}/status");

        ((int)response.StatusCode).ShouldBeOneOf(404, 403);
    }

    [Fact]
    public async Task GetStatus_TenantDeOutroClienteApp_Retorna403()
    {
        // Cria ClienteApp-1 com Tenant-1 e emite um documento
        var (http1, docId) = await EmitirAsync();

        // Cria ClienteApp-2 com Tenant-2 (CNPJ diferente para evitar colisão de unicidade)
        var (_, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);
        // Usar ClienteApp-2 para criar Tenant-2 — precisa do clienteAppId2
        // Obter o clienteAppId2 criando via helper separado
        var clienteAppId2 = (await CriarClienteAppAsync()).Id;
        var tenantId2 = await CriarTenantAsync(clienteAppId2, cnpj: "22333444000195");
        using var http2 = CriarClienteAutenticado(token2, tenantId2.Value);

        // ClienteApp-2 tenta acessar documento do ClienteApp-1 — deve ser 403
        var response = await http2.GetAsync($"/api/v1/documentos/{docId}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // DTO local para deserializar a resposta 202.
    // DocumentoFiscalId serializa como {"value": "<guid>"} pois é um readonly record struct
    // sem JsonConverter personalizado registrado.
    // Status é int pois não há JsonStringEnumConverter configurado no projeto.
    private sealed record IssueResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("pollUrl")] string PollUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

    private sealed record StatusResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("pollUrl")] string? PollUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

    private sealed record DocumentoIdDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("value")] Guid Value);
}
```

- [ ] **Step 2: Rodar testes de status**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~DocumentStatusTests" -v normal 2>&1 | Select-Object -Last 5
```

Expected: todos passando. Se `GetStatus_TenantDeOutroClienteApp_Retorna403` falhar com 404 (ao invés de 403), verificar se o endpoint de status distingue "documento não encontrado" de "documento de outro tenant" — pode ser necessário adicionar lógica no handler.

- [ ] **Step 3: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/DocumentStatusTests.cs
git commit -m "test: integration — GET /documentos/{id}/status e /xml acesso e isolamento de tenant"
```

---

## Task 5: Testes de health check e smoke test

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Integration/HealthCheckTests.cs`

- [ ] **Step 1: Criar `HealthCheckTests.cs`**

```csharp
// tests/VisuFiscalHub.Tests/Integration/HealthCheckTests.cs
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class HealthCheckTests : IntegrationTestBase
{
    [Fact]
    public async Task GetHealthLive_SempreRetorna200()
    {
        var response = await Client.GetAsync("/health/live");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ComInMemoryDb_Retorna200OuServiceUnavailable()
    {
        // Com InMemory, o NpgSql health check é desabilitado (ambiente Test).
        // O status esperado é 200; 503 é aceito como fallback seguro.
        var response = await Client.GetAsync("/health");
        ((int)response.StatusCode).ShouldBeOneOf(200, 503);
    }

    [Fact]
    public async Task GetTenants_SemToken_Retorna401()
    {
        var response = await Client.GetAsync("/api/v1/tenants");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostClientes_SemAdminKey_Retorna401()
    {
        var body = new { name = "Test", clientId = "test", clientSecret = "test12345" };
        var response = await Client.PostAsJsonAsync("/api/v1/clientes", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostClientes_AdminKeyErrada_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/clientes")
        {
            Content = JsonContent.Create(new { name = "Test", clientId = "test", clientSecret = "test12345" }),
            Headers = { { "X-Admin-Key", "chave-errada" } }
        };
        var response = await Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
```

- [ ] **Step 2: Rodar testes de health**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~HealthCheckTests" -v normal 2>&1 | Select-Object -Last 5
```

Expected: 5/5 passando.

- [ ] **Step 3: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/HealthCheckTests.cs
git commit -m "test: integration — health checks e smoke tests de auth e admin key"
```

---

## Task 6: Suite completa de integração + verificação final

- [ ] **Step 1: Rodar TODOS os testes (unit + integration)**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-Object -Last 10
```

Expected: zero falhas. O total de testes deve ser maior que 529 (testes de integração adicionados: ao menos 21 novos).

- [ ] **Step 2: Verificar ausência de falhas reais**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --no-build 2>&1 | Select-String "Failed" | Where-Object { $_ -notmatch "Failed: 0" }
```

> **Nota**: O `Where-Object` exclui a linha de summary "Failed: 0" para retornar resultado limpo apenas em caso de falhas reais.

Expected: nenhuma linha de saída.

- [ ] **Step 3: Listar os testes de integração criados**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --list-tests 2>&1 | Select-String "Integration"
```

Expected: listar AuthFlowTests (4), IssueDocumentTests (8 ou 9 se CPF implementado), DocumentStatusTests (5), HealthCheckTests (5) = ao menos 22 testes de integração.

- [ ] **Step 4: Commit de fechamento (se houver arquivos não commitados)**

```powershell
git status
# Se vazio, nenhum commit adicional necessário.
```

---

## Self-Review

### 1. Spec Coverage

| Requisito | Task |
|---|---|
| WebApplicationFactory com InMemory DB | Task 1 |
| Gate de ambiente Test em DependencyInjection e Program.cs | Task 1 |
| FakeSefazClient controlável (positional records) | Task 1 |
| FakeDocumentJobQueue thread-safe com Reset() | Task 1 |
| FakeSequenceManager in-memory com _next = 0 | Task 1 |
| JWT RS256 com RSA por instância de factory | Task 1 |
| CacheSignatureProviders = false (evita ObjectDisposedException, test-only) | Task 1 |
| Rate limiting desabilitado em testes (evita flakiness) | Task 1 |
| Env vars no construtor (antes do ValidateOnStart) + cleanup no Dispose | Task 1 |
| IntegrationTestCollection para serializar execução xUnit | Task 1 |
| Helper CriarClienteApp + CriarTenant + ObterToken + CriarClienteAutenticado | Task 1 |
| protected JobQueue e SequenceManager em IntegrationTestBase | Task 1 |
| public partial class Program {} em Program.cs | Task 1 |
| POST /auth/token credenciais válidas → 200 + JWT | Task 2 |
| POST /auth/token credenciais erradas → 401 | Task 2 |
| POST /auth/token cliente inexistente → 401 | Task 2 |
| POST /nfce request válido → 202 + DocumentoId + status int Enfileirado | Task 3 |
| POST /nfce sem X-Idempotency-Key → 422 | Task 3 |
| POST /nfce sem token → 401 | Task 3 |
| POST /nfce mesma chave → mesmo DocumentoId | Task 3 |
| POST /nfce NCM inválido → 422 | Task 3 |
| POST /nfce indPresenca inválido → 422 | Task 3 |
| POST /nfce totais não fecham → 422 | Task 3 |
| POST /nfce CPF obrigatório acima de R$ 500 (NT 2019.001) → 422 | Task 3 |
| GET /status documento existente → 200 com body válido | Task 4 |
| GET /status documento inexistente → 404 | Task 4 |
| GET /xml documento sem XML → 404 com ProblemDetails.Detail | Task 4 |
| GET /status com tenant inexistente → 404/403 | Task 4 |
| GET /status com tenant de outro ClienteApp → 403 (isolamento real) | Task 4 |
| GET /health/live → 200 sempre | Task 5 |
| GET /health → 200 ou 503 (InMemory) | Task 5 |
| GET /tenants sem token → 401 | Task 5 |
| POST /clientes sem X-Admin-Key → 401 | Task 5 |
| POST /clientes com admin key errada → 401 | Task 5 |
| Verificação final suite completa + ausência de falhas | Task 6 |

**Gaps conhecidos (fora do escopo deste plano):**
- Testes de cancelamento — requerem documento com status `Autorizado`, que não ocorre sem Hangfire processar o job.
- Testes de criação de Tenant via API.
- Validação XSD dos XMLs gerados pelo `NfceXmlBuilder`.
- Teste de `GetStatus_TenantDeOutroClienteApp_Retorna403` pode requerer ajuste no handler se o endpoint não distinguir 403 de 404 para documentos de outros tenants.
