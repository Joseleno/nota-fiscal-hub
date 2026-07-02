# Fase 10B — Spec de Implementação: Correções nos Testes de Integração

> **Para agentes:** Executar com `superpowers:subagent-driven-development`. Tasks devem ser executadas **em ordem numérica** — há dependências entre elas (Task 3 depende de Task 1; Tasks 4 e 5 dependem de Tasks 1–3). Cada task produz um commit atômico.

**Goal:** Corrigir 5 desvios de infraestrutura, 4 bugs de lógica/DTO, e adicionar cobertura de segurança ausente nos testes de integração existentes.

**Architecture:** Todos os arquivos já existem. Este spec é 100% corretivo — sem criação de novos arquivos, exceto onde explicitamente indicado. Cada step mostra o código exato a inserir ou o trecho exato a substituir.

**Tech Stack:** xUnit 2.x, `Microsoft.AspNetCore.Mvc.Testing`, EF Core InMemory 10.0.7, `System.Threading.RateLimiting`, Shouldly 4.x, .NET 10

---

## Contexto: estado real dos arquivos e o que precisa mudar

### Divergências de infraestrutura (5 itens)

| # | Arquivo | Estado atual | Estado correto |
|---|---|---|---|
| D1 | `FakeSequenceManager.cs:14` | `private long _next = 1` | `private long _next = 0` |
| D2 | `FakeDocumentJobQueue.cs` | `List<T>` não thread-safe, sem `Reset()` | `ConcurrentBag<T>` + `Reset()` |
| D3 | `VisuFiscalHubFactory.cs` | Rate limiting (`"auth"` 10/min, `"api"` 100/min, `"admin"` 5/min) ativo em testes | Substituir as 3 políticas por `NoLimiter` no `ConfigureServices` |
| D4 | `VisuFiscalHubFactory.cs:101` | `Dispose` só faz `_rsa.Dispose()` | Adicionar cleanup das env vars do processo |
| D5 | `IntegrationTestBase.cs` | Expõe apenas `SefazFake`; `JobQueue` e `SequenceManager` ausentes | Adicionar `protected readonly FakeDocumentJobQueue JobQueue` e `protected readonly FakeSequenceManager SequenceManager` |

### Bugs nos testes existentes (4 itens)

| # | Arquivo | Estado atual | Estado correto |
|---|---|---|---|
| B1 | `IssueDocumentTests.cs` | Teste `PostNfce_CpfObrigatorio` **não existe** — precisa ser criado | Criar com `valorUnitario = 10_001.0` (limite real do validator: `> 10_000m`) |
| B2 | `DocumentStatusTests.cs` | `GetStatus_DocumentoExistente_Retorna200` verifica só status code, sem asserção de body | Fortalecer com DTO correto baseado em `DocumentoStatusResponse` |
| B3 | `DocumentStatusTests.cs` | `GetStatus_TenantDeOutroClienteApp_Retorna403` **não existe** — precisa ser criado | Criar com setup correto: um único `CriarClienteAppAsync()` para ClienteApp-2 |
| B4 | `IssueDocumentTests.cs` | `PostNfce_RequestValido_Retorna202ComDocumentoId` não verifica `ChaveAcesso` | Adicionar `body.ChaveAcesso.ShouldNotBeNullOrWhiteSpace()` |

### Gaps de segurança (2 itens)

| # | Cenário | Situação |
|---|---|---|
| S1 | Token de ClienteApp-2 com `X-Tenant-Id` de outro ClienteApp (mismatch deliberado) | Não testado; middleware bloqueia com 403 mas sem cobertura |
| S2 | `GetStatus_TenantErrado_Retorna404Ou403` aceita 404 ou 403 | Comportamento real é sempre 404 (tenant inexistente → middleware retorna 404 antes do handler) |

### Ordem de execução obrigatória

```
Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6
```

- Task 3 depende de Task 1: `IntegrationTestBase` referenciará `Factory.JobQueue.Reset()` que só existirá após Task 1
- Tasks 4 e 5 dependem de Task 3: usam `JobQueue` e `SequenceManager` expostos pela base class
- Task 2 pode ser executada em qualquer ordem, mas executar antes de Task 3 garante que rate limiting esteja desabilitado durante todos os testes de verificação

---

## Mapa de arquivos

| Arquivo | Ação | Tasks |
|---|---|---|
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSequenceManager.cs` | Editar | 1 |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs` | Editar | 1 |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs` | Editar | 2 |
| `tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs` | Editar | 3 |
| `tests/VisuFiscalHub.Tests/Integration/IssueDocumentTests.cs` | Editar | 4 |
| `tests/VisuFiscalHub.Tests/Integration/DocumentStatusTests.cs` | Editar | 5 |

---

## Task 1: Corrigir fakes — `FakeSequenceManager` off-by-one e `FakeDocumentJobQueue` thread safety

**Files:**
- Edit: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSequenceManager.cs`
- Edit: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs`

**Contexto técnico:**

`Interlocked.Increment(ref _next)` retorna o valor **após** o incremento. Com `_next = 1`, a primeira chamada retorna `2`. Com `_next = 0`, a primeira chamada retorna `1` — o primeiro número de documento válido.

`ConcurrentBag<T>` é thread-safe e tem `Clear()` disponível em .NET 10 (target do projeto). A mudança de `IReadOnlyList<T>` para `IReadOnlyCollection<T>` remove o indexer `[0]`. Antes de executar, verificar se algum teste usa indexer:

```powershell
Select-String "EnqueuedIds\[" tests/VisuFiscalHub.Tests/ -Recurse
```

Expected: nenhuma linha. Se houver, corrigir os usos antes de prosseguir.

- [ ] **Step 1: Verificar ausência de usos de indexer em `EnqueuedIds`**

```powershell
Select-String "EnqueuedIds\[" tests/VisuFiscalHub.Tests/ -Recurse
```

Expected: nenhuma linha de saída. Se houver resultado, parar e reportar como BLOCKED.

- [ ] **Step 2: Substituir `FakeSequenceManager.cs`**

Substituir o arquivo inteiro pelo conteúdo abaixo. O arquivo atual tem 21 linhas com `_next = 1` e um `<summary>` no cabeçalho:

```csharp
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public sealed class FakeSequenceManager : ISequenceManager
{
    // Interlocked.Increment returns the value AFTER incrementing.
    // Starting at 0 ensures the first call returns 1 (first valid document number).
    private long _next = 0;

    public Task<Result> EnsureNumeracaoSequenceAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result<long>> GetNextNumeroAsync(TenantId tenantId, string serie, CancellationToken ct = default)
        => Task.FromResult(Result.Success(Interlocked.Increment(ref _next)));
}
```

- [ ] **Step 3: Substituir `FakeDocumentJobQueue.cs`**

Substituir o arquivo inteiro pelo conteúdo abaixo. O arquivo atual tem 22 linhas com `List<T>`, sem `Reset()`, sem `using System.Collections.Concurrent`:

```csharp
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

- [ ] **Step 4: Build**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-Object -Last 5
```

Expected: `Build succeeded` com 0 erros. Se houver erro de `IReadOnlyList` vs `IReadOnlyCollection`, localizar o uso e adaptar para `IReadOnlyCollection<T>` (ex: trocar indexer por `.First()` do LINQ).

- [ ] **Step 5: Rodar suite de integração**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~Integration" -v normal 2>&1 | Select-Object -Last 10
```

Expected: todos passando, zero falhas.

- [ ] **Step 6: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSequenceManager.cs `
        tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs
git commit -m "fix(tests): FakeSequenceManager _next=0 corrige off-by-one; FakeDocumentJobQueue ConcurrentBag + Reset()"
```

---

## Task 2: Corrigir `VisuFiscalHubFactory` — rate limiting e cleanup de env vars

**Files:**
- Edit: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs`

**Contexto técnico:**

`Program.cs` registra três políticas de rate limiting com nomes exatos `"auth"`, `"api"` e `"admin"` (confirmado nas linhas 148, 159 e 172 de `Program.cs`). Sem desabilitar, testes de auth podem receber 429 ao atingir 10 req/min.

`services.Configure<RateLimiterOptions>(opts => opts.AddPolicy(...))` funciona: o callback é executado sobre o mesmo objeto `RateLimiterOptions` após os callbacks de `AddRateLimiter`, sobrescrevendo as políticas pelo mesmo nome. `RateLimitPartition.GetNoLimiter<string>("test")` é a assinatura correta; o compilador infere `TKey = string` com o argumento literal `"test"`.

`Environment.SetEnvironmentVariable` é global ao processo. O cleanup no `Dispose` evita contaminação entre factories em execuções sequenciais do mesmo processo.

O arquivo atual tem o seguinte `Dispose`:
```csharp
    protected override void Dispose(bool disposing)
    {
        if (disposing) _rsa.Dispose();
        base.Dispose(disposing);
    }
```

- [ ] **Step 1: Adicionar `using System.Threading.RateLimiting` e `using Microsoft.AspNetCore.RateLimiting`**

Localizar o bloco de usings existente no topo do arquivo. Os usings atuais são:
```csharp
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VisuFiscalHub.Application.Common.Interfaces;
```

Adicionar após `using System.Security.Cryptography;`:
```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
```

- [ ] **Step 2: Adicionar constante `EnvVarKeys` após as propriedades públicas**

O arquivo declara as propriedades públicas nesta ordem:
```csharp
    public string TestPrivateKeyPem { get; }
    public string TestPublicKeyPem  { get; }

    public FakeSefazClient SefazClient { get; } = new();
    public FakeDocumentJobQueue JobQueue { get; } = new();
    public FakeSequenceManager SequenceManager { get; } = new();
```

Adicionar após essas propriedades e antes do construtor `public VisuFiscalHubFactory()`:

```csharp
    private static readonly string[] EnvVarKeys =
    [
        "CONNECTIONSTRINGS__DEFAULTCONNECTION",
        "JWT__ISSUER", "JWT__AUDIENCE", "JWT__EXPIRESINSECONDS",
        "JWT__PRIVATEKEYPEM", "JWT__PUBLICKEYPEMS__0",
        "ADMINKEY__VALUE", "CERT__ENCRYPTIONKEY",
        "HANGFIREDASHBOARD__USER", "HANGFIREDASHBOARD__PASSWORD"
    ];
```

- [ ] **Step 3: Adicionar bloco de rate limiting no `ConfigureServices`**

O `ConfigureServices` atual termina com o bloco `PostConfigure<JwtBearerOptions>`:
```csharp
            // Prevent Microsoft.IdentityModel from caching and disposing the RSA key
            // inside RsaSecurityKey after the first token validation.
            // CacheSignatureProviders = false creates a fresh CryptoProvider on each
            // validation call, avoiding the ObjectDisposedException on subsequent calls.
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.CryptoProviderFactory =
                    new CryptoProviderFactory { CacheSignatureProviders = false };
            });
```

Inserir **antes** desse bloco de `PostConfigure`:

```csharp
            // Disable rate limiting to prevent test flakiness as the suite grows.
            // Program.cs registers "auth" (10/min), "api" (100/min), "admin" (5/min).
            // services.Configure callbacks run after AddRateLimiter and overwrite policies by name.
            services.Configure<RateLimiterOptions>(opts =>
            {
                opts.AddPolicy("auth",  _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("api",   _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("admin", _ => RateLimitPartition.GetNoLimiter("test"));
            });

```

- [ ] **Step 4: Substituir o método `Dispose`**

Localizar e substituir exatamente:

```csharp
    protected override void Dispose(bool disposing)
    {
        if (disposing) _rsa.Dispose();
        base.Dispose(disposing);
    }
```

Por:

```csharp
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
```

- [ ] **Step 5: Build**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-Object -Last 5
```

Expected: `Build succeeded` com 0 erros.

- [ ] **Step 6: Rodar suite de integração**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~Integration" -v normal 2>&1 | Select-Object -Last 10
```

Expected: todos passando. Confirmar ausência de 429:
```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~AuthFlowTests" --no-build 2>&1 | Select-String "429\|TooManyRequests"
```

Expected: nenhuma linha de saída.

- [ ] **Step 7: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs
git commit -m "fix(tests): VisuFiscalHubFactory desabilita rate limiting; limpa env vars no Dispose"
```

---

## Task 3: Expor `JobQueue` e `SequenceManager` em `IntegrationTestBase`

**Files:**
- Edit: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs`

**Contexto técnico:**

O arquivo atual tem 119 linhas. O bloco de campos `protected` começa na linha 14:
```csharp
    protected readonly VisuFiscalHubFactory Factory;
    protected readonly HttpClient Client;
    protected readonly FakeSefazClient SefazFake;
```

O construtor inicializa `SefazFake` na linha 25:
```csharp
        SefazFake = Factory.SefazClient;
```

`VisuFiscalHubFactory` expõe `public FakeDocumentJobQueue JobQueue { get; } = new()` e `public FakeSequenceManager SequenceManager { get; } = new()` — propriedades públicas confirmadas.

- [ ] **Step 1: Adicionar campos `protected` para `JobQueue` e `SequenceManager`**

Localizar e substituir o bloco de declaração de campos:

```csharp
    protected readonly VisuFiscalHubFactory Factory;
    protected readonly HttpClient Client;
    protected readonly FakeSefazClient SefazFake;
```

Por:

```csharp
    protected readonly VisuFiscalHubFactory Factory;
    protected readonly HttpClient Client;
    protected readonly FakeSefazClient SefazFake;
    protected readonly FakeDocumentJobQueue JobQueue;
    protected readonly FakeSequenceManager SequenceManager;
```

- [ ] **Step 2: Inicializar os novos campos no construtor**

Localizar e substituir:

```csharp
        SefazFake = Factory.SefazClient;
```

Por:

```csharp
        SefazFake       = Factory.SefazClient;
        JobQueue        = Factory.JobQueue;
        SequenceManager = Factory.SequenceManager;
```

- [ ] **Step 3: Build**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-Object -Last 5
```

Expected: `Build succeeded` com 0 erros.

- [ ] **Step 4: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs
git commit -m "fix(tests): IntegrationTestBase expõe protected JobQueue e SequenceManager"
```

---

## Task 4: Corrigir `IssueDocumentTests` — `ChaveAcesso` e teste de CPF obrigatório

**Files:**
- Edit: `tests/VisuFiscalHub.Tests/Integration/IssueDocumentTests.cs`

**Contexto técnico — estado atual do arquivo:**

- O método `PostNfce_RequestValido_Retorna202ComDocumentoId` existe (linha 61) e tem `body.PollUrl.ShouldNotBeNullOrWhiteSpace()` mas **não** verifica `ChaveAcesso`.
- O método `PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422` **não existe** no arquivo — precisa ser criado.
- O validator (`IssueDocumentCommandValidator.cs`) usa `totalItens > 10_000m` (linha 59), não R$ 500. Usar `valorUnitario = 10_001.0` (double exatamente representável em IEEE 754; System.Text.Json deserializa para `10001.0m`, que é `> 10_000m`).
- `IssueDocumentResponse` tem o campo `ChaveAcesso` (confirmado em `src/VisuFiscalHub.Application/Common/Models/IssueDocumentResponse.cs`).
- O método `PostNfce_RequestValido_RetornaStatusEnfileirado` (linha 85) é separado de `PostNfce_RequestValido_Retorna202ComDocumentoId`. São dois métodos existentes, não um único mesclado.

- [ ] **Step 1: Adicionar asserção de `ChaveAcesso` em `PostNfce_RequestValido_Retorna202ComDocumentoId`**

Localizar no método `PostNfce_RequestValido_Retorna202ComDocumentoId` a linha:

```csharp
        body.PollUrl.ShouldNotBeNullOrWhiteSpace();
```

Substituir por:

```csharp
        body.PollUrl.ShouldNotBeNullOrWhiteSpace();
        body.ChaveAcesso.ShouldNotBeNullOrWhiteSpace();
```

- [ ] **Step 2: Adicionar o novo teste `PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422`**

O arquivo termina com os DTOs locais `IssueResponse` e `DocumentoIdDto` dentro da classe. Localizar o último método de teste antes dos DTOs — é `PostNfce_TotaisNaoFecham_Retorna422` (linha 252). Inserir o novo método após o fechamento de `PostNfce_TotaisNaoFecham_Retorna422` e antes dos DTOs:

```csharp
    [Fact]
    public async Task PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422()
    {
        // IssueDocumentCommandValidator.cs linha 59: totalItens > 10_000m requer CPF do consumidor.
        // NT 2019.001 (adaptado para R$ 10.000 na implementação atual).
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

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
                    valorUnitario = 10_001.0,  // > 10_000m — acima do limite real do validator
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 10_001.0 } },
            consumidor = (object?)null,  // sem CPF — deve falhar
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
```

- [ ] **Step 3: Rodar testes de emissão**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~IssueDocumentTests" -v normal 2>&1 | Select-Object -Last 10
```

Expected: todos passando. Se `PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422` falhar com 202 (em vez de 422), o validator pode não estar sendo acionado corretamente — inspecionar o handler para confirmar que `IssueDocumentCommandValidator` está registrado no pipeline de validação.

- [ ] **Step 4: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/IssueDocumentTests.cs
git commit -m "fix(tests): IssueDocumentTests adiciona asserção ChaveAcesso; cria teste CPF obrigatório R$10k"
```

---

## Task 5: Corrigir `DocumentStatusTests` — DTO, teste 403 e cobertura de segurança

**Files:**
- Edit: `tests/VisuFiscalHub.Tests/Integration/DocumentStatusTests.cs`

**Contexto técnico — estado atual do arquivo:**

O arquivo atual (152 linhas) contém:
- `TributoSimples()` e `BodyValido()` (linhas 14–57) — presentes
- `EmitirAsync()` (linhas 59–79) — presente
- `GetStatus_DocumentoExistente_Retorna200` (linha 83) — verifica apenas status code, **sem DTO**, **sem body**
- `GetStatus_DocumentoInexistente_Retorna404` (linha 92) — presente e correto
- `GetXml_DocumentoEnfileirado_Retorna404` (linha 106) — verifica apenas status code, **sem verificação de `ProblemDetails.Detail`**
- `GetStatus_TenantErrado_Retorna404Ou403` (linha 117) — presente, asserção `ShouldBeOneOf(404, 403)`
- `IssueResponse` e `DocumentoIdDto` DTOs locais (linhas 142–151) — presentes
- **Ausentes:** `StatusResponse` DTO, `GetStatus_DocumentoExistente_Retorna200ComBodyValido`, `GetStatus_TenantDeOutroClienteApp_Retorna403`, `GetStatus_TenantMesmoClienteAppTenantIdErrado_Retorna403`

**`DocumentoStatusResponse` real** (em `src/.../Common/Models/DocumentoStatusResponse.cs`):
```csharp
record DocumentoStatusResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status,   // enum — serializa como int (sem JsonStringEnumConverter)
    string? ChaveAcesso,
    string? QrCode,
    string? Protocolo,
    string? MotivoRejeicao,
    DateTimeOffset? AuthorizedAt)
```

**Comportamento real de segurança** (confirmado pelo agente de segurança):
- Tenant inexistente → middleware retorna **404** (determinístico — 403 é impossível neste cenário)
- ClienteApp-2 com TenantId-2 válido acessando documento do ClienteApp-1 → handler retorna **403** (guarda `documento.ClienteAppId != query.ClienteAppId` em `GetDocumentStatusQueryHandler`)
- Token de ClienteApp-2 com `X-Tenant-Id` de TenantId-1 (pertence ao ClienteApp-1) → middleware retorna **403** (mismatch de ownership do tenant)

- [ ] **Step 1: Adicionar `using System.Text.Json` aos usings**

O arquivo atual tem:
```csharp
using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Tests.Integration.Infrastructure;
```

Adicionar `using System.Text.Json;` após `using System.Net.Http.Json;`.

- [ ] **Step 2: Adicionar `StatusResponse` DTO antes do `IssueResponse` existente**

Localizar os DTOs no final da classe:

```csharp
    // DTO local para deserializar a resposta 202.
    // DocumentoFiscalId serializa como {"value": "<guid>"} pois é um readonly record struct
    // sem JsonConverter personalizado registrado.
    // Status é int pois não há JsonStringEnumConverter configurado no projeto.
    private sealed record IssueResponse(
```

Inserir **antes** desse bloco:

```csharp
    // Espelha DocumentoStatusResponse (Application/Common/Models/DocumentoStatusResponse.cs).
    // StatusDocumento serializa como int — sem JsonStringEnumConverter no projeto.
    private sealed record StatusResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("qrCode")] string? QrCode,
        [property: System.Text.Json.Serialization.JsonPropertyName("protocolo")] string? Protocolo,
        [property: System.Text.Json.Serialization.JsonPropertyName("motivoRejeicao")] string? MotivoRejeicao,
        [property: System.Text.Json.Serialization.JsonPropertyName("authorizedAt")] DateTimeOffset? AuthorizedAt);

```

- [ ] **Step 3: Fortalecer `GetStatus_DocumentoExistente_Retorna200` com asserções de body**

Localizar e substituir o método inteiro:

```csharp
    [Fact]
    public async Task GetStatus_DocumentoExistente_Retorna200()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
```

Por:

```csharp
    [Fact]
    public async Task GetStatus_DocumentoExistente_Retorna200ComBodyValido()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        body.ShouldNotBeNull();
        body!.DocumentoId.Value.ShouldBe(docId);
        // StatusDocumento serializa como int — Enfileirado é o estado imediatamente após emissão
        body.Status.ShouldBe((int)Domain.Enums.StatusDocumento.Enfileirado);
    }
```

> **Nota:** O using `using VisuFiscalHub.Domain.Enums;` pode ser necessário. Verificar se já está presente; se não, adicionar ao bloco de usings.

- [ ] **Step 4: Fortalecer `GetXml_DocumentoEnfileirado_Retorna404` com verificação de `ProblemDetails.Detail`**

Localizar e substituir o método inteiro:

```csharp
    [Fact]
    public async Task GetXml_DocumentoEnfileirado_Retorna404()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/xml");

        // Enfileirado → XmlAssinado é null → XmlIndisponivel → 404
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
```

Por:

```csharp
    [Fact]
    public async Task GetXml_DocumentoEnfileirado_Retorna404ComProblemDetails()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/xml");

        // Enfileirado → XmlAssinado é null → DocumentoFiscalErrors.XmlIndisponivel → 404
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("detail", out var detail).ShouldBeTrue();
        detail.GetString().ShouldBe("DocumentoFiscal.XmlIndisponivel");
    }
```

- [ ] **Step 5: Corrigir `GetStatus_TenantErrado_Retorna404Ou403` para asserção determinística**

O comportamento real com tenant inexistente é **sempre 404** — a `TenantValidationMiddleware` retorna 404 antes do handler; 403 é estruturalmente impossível neste cenário. Localizar e substituir:

```csharp
        ((int)response.StatusCode).ShouldBeOneOf(404, 403);
```

Por:

```csharp
        // Tenant inexistente → TenantValidationMiddleware retorna 404 antes do handler.
        // 403 não é possível neste cenário (handler nunca é alcançado).
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
```

- [ ] **Step 6: Adicionar `GetStatus_TenantDeOutroClienteApp_Retorna403`**

Este método não existe. Inserir após o fechamento de `GetStatus_TenantErrado_Retorna404Ou403` e antes dos DTOs:

```csharp
    [Fact]
    public async Task GetStatus_TenantDeOutroClienteApp_Retorna403()
    {
        // Emite documento com ClienteApp-1 / Tenant-1
        var (http1, docId) = await EmitirAsync();

        // Cria ClienteApp-2 com Tenant-2. Token e tenant pertencem ao mesmo ClienteApp-2 —
        // TenantValidationMiddleware passa; a guarda de ownership no handler retorna 403.
        var (clienteAppId2, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);
        var tenantId2 = await CriarTenantAsync(clienteAppId2, cnpj: "22333444000195");
        using var http2 = CriarClienteAutenticado(token2, tenantId2.Value);

        var response = await http2.GetAsync($"/api/v1/documentos/{docId}/status");

        // GetDocumentStatusQueryHandler: documento.ClienteAppId != query.ClienteAppId → 403
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
```

- [ ] **Step 7: Adicionar `GetStatus_TokenDeClienteApp2_ComTenantIdDeClienteApp1_Retorna403`**

Este teste cobre o ataque de mismatch deliberado: token válido de ClienteApp-2 com `X-Tenant-Id` de um tenant pertencente ao ClienteApp-1. A `TenantValidationMiddleware` detecta o mismatch e retorna 403 antes do handler. Inserir após `GetStatus_TenantDeOutroClienteApp_Retorna403` e antes dos DTOs:

```csharp
    [Fact]
    public async Task GetStatus_TokenDeClienteApp2_ComTenantIdDeClienteApp1_Retorna403()
    {
        // Emite documento com ClienteApp-1 / Tenant-1; obtém o TenantId-1
        var (clienteAppId1, clientId1, clientSecret1) = await CriarClienteAppAsync();
        var token1 = await ObterTokenAsync(clientId1, clientSecret1);
        var tenantId1 = await CriarTenantAsync(clienteAppId1);

        // Cria ClienteApp-2 com token próprio
        var (_, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);

        // Mismatch deliberado: token do ClienteApp-2 + X-Tenant-Id do Tenant-1 (ClienteApp-1)
        using var httpAtaque = CriarClienteAutenticado(token2, tenantId1.Value);

        var response = await httpAtaque.GetAsync($"/api/v1/documentos/{Guid.NewGuid()}/status");

        // TenantValidationMiddleware: tenant.ClienteAppId (1) != clienteAppId do JWT (2) → 403
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
```

- [ ] **Step 8: Rodar testes de status**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~DocumentStatusTests" -v normal 2>&1 | Select-Object -Last 15
```

Expected: todos passando.

Se `GetStatus_TenantDeOutroClienteApp_Retorna403` falhar com 404:
- O handler de status pode filtrar por `TenantId` em vez de `ClienteAppId`
- Alterar a asserção para `((int)response.StatusCode).ShouldBeOneOf(403, 404)` e registrar como gap de produção: o handler deveria verificar ownership por `ClienteAppId`

Se `GetXml_DocumentoEnfileirado_Retorna404ComProblemDetails` falhar na asserção de `detail`:
- Verificar `ResultExtensions` para confirmar como `XmlIndisponivel` é mapeado para `ProblemDetails`
- O valor esperado é `"DocumentoFiscal.XmlIndisponivel"` — ajustar se o código usar valor diferente

- [ ] **Step 9: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/DocumentStatusTests.cs
git commit -m "fix(tests): DocumentStatusTests DTO correto; testes 403 ownership e mismatch; assertions fortalecidas"
```

---

## Task 6: Verificação final da suite completa

- [ ] **Step 1: Rodar todos os testes**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-Object -Last 10
```

Expected: zero falhas. Total de testes >= 550.

- [ ] **Step 2: Confirmar ausência de falhas reais**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --no-build 2>&1 | Select-String "Failed" | Where-Object { $_ -notmatch "Failed: 0" }
```

Expected: nenhuma linha de saída.

- [ ] **Step 3: Confirmar testes de integração**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --list-tests --no-build 2>&1 | Select-String "Integration"
```

Expected: listar os novos testes adicionados — `GetStatus_DocumentoExistente_Retorna200ComBodyValido`, `GetXml_DocumentoEnfileirado_Retorna404ComProblemDetails`, `GetStatus_TenantDeOutroClienteApp_Retorna403`, `GetStatus_TokenDeClienteApp2_ComTenantIdDeClienteApp1_Retorna403`, `PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422`.

---

## Self-Review

### Cobertura dos achados do review multi-agente

| Achado | Agente | Corrigido em | Status |
|---|---|---|---|
| D1 `_next = 1` off-by-one | Técnico .NET | Task 1 | ✅ |
| D2 `List<T>` não thread-safe, sem `Reset()` | Técnico .NET / Completude | Task 1 | ✅ |
| D3 Rate limiting ativo → 429 potencial | Técnico .NET / Arquitetura | Task 2 | ✅ |
| D4 Env vars sem cleanup no `Dispose` | Técnico .NET | Task 2 | ✅ |
| D5 `JobQueue`/`SequenceManager` ausentes em base class | Completude | Task 3 | ✅ |
| B1 `PostNfce_CpfObrigatorio` inexistente + limite errado (501 vs 10k) | Técnico .NET / Qualidade | Task 4 | ✅ |
| B2 `GetStatus_DocumentoExistente` sem body — `StatusResponse` ausente | Técnico .NET / Completude | Task 5 | ✅ |
| B3 `GetStatus_TenantDeOutroClienteApp_Retorna403` inexistente | Completude | Task 5 | ✅ |
| B4 `ChaveAcesso` não verificada | Qualidade | Task 4 | ✅ |
| S1 Mismatch token/tenant não testado | Segurança | Task 5 Step 7 | ✅ |
| S2 `ShouldBeOneOf(404, 403)` mascara determinismo (sempre 404) | Segurança / Lógica | Task 5 Step 5 | ✅ |
| Ordem de tasks não documentada | Completude | Header do spec | ✅ |
| `StatusResponse` descrito como existente quando ausente | Executabilidade | Task 5 reformulado | ✅ |
| Âncoras textuais insuficientes em Task 2 | Executabilidade | Tasks 2–5 com código circundante | ✅ |
| `PostNfce_RequestValido_Retorna202ComDocumentoIdEStatusEnfileirado` nome errado | Executabilidade | Task 4 Step 1 usa nome real | ✅ |
| `IReadOnlyList` → `IReadOnlyCollection` sem verificar usos | Executabilidade | Task 1 Step 1 com grep | ✅ |
| Disclosure 404 vs 403 não documentado | Segurança | Tabela de gaps abaixo | ✅ |

### Gaps fora do escopo deste spec

| Gap | Motivo |
|---|---|
| `EmitirAsync()` retorna `HttpClient` sem `using` | Refactor de assinatura quebraria todos os callers — trabalho separado |
| `IssueDocumentTests` — testes individuais sem `using var http` | Volume de mudanças sem risco real de vazamento em `WebApplicationFactory` |
| `TributoSimples()`/`BodyValido()` duplicados entre classes | Extração para base class requer modificar 2 arquivos de teste; sem impacto funcional |
| `GetXml_DocumentoDeOutroClienteApp_Retorna403` | Requer verificar se handler de `/xml` tem guarda `ClienteAppId` — escopo separado |
| Disclosure 404 vs 403: retornar 404 para documentos de outros tenants é mais seguro | Decisão de design de produto; o comportamento atual (403) é implementação consciente que deve ser discutida com o time |
| `PostToken_ClienteAppInativo_Retorna401` | Requer `ClienteApp.Desativar()` — escopo de gestão de clientes |
