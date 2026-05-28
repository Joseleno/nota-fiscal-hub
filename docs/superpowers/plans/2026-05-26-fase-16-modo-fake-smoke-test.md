# Fase 16 — Modo Fake para Smoke Test Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar um modo fake ativável por configuração (`Sefaz__UseFakeClient: true`) que substitui `ISefazClient` e `IPrefeituraClient` por stubs locais, permitindo validar o fluxo completo NFC-e → Enfileirado → Autorizado sem certificado digital nem acesso à SEFAZ. Entregar também uma coleção de requests `.http` cobrindo o caminho feliz completo.

**Architecture:** Mover as implementações fake de `tests/` para `src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/`, torná-las `internal`. O `DependencyInjection.AddInfrastructure()` já recebe `IConfiguration` e `IHostEnvironment` — basta ler `configuration["Sefaz:UseFakeClient"]` e registrar condicionalmente. O arquivo `.http` (VS Code REST Client / JetBrains HTTP Client) documenta o fluxo e serve de roteiro manual.

**Tech Stack:** .NET 10, `IConfiguration`, `appsettings.Development.json`, REST Client `.http` format (VS Code / JetBrains).

---

## File Map

| Ação | Arquivo | Responsabilidade |
|------|---------|-----------------|
| Create | `src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakeSefazClient.cs` | Stub ISefazClient — retorna Autorizado por padrão |
| Create | `src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakePrefeituraClient.cs` | Stub IPrefeituraClient — retorna Autorizado por padrão |
| Modify | `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs:128-137` | Registro condicional via `Sefaz:UseFakeClient` |
| Modify | `src/VisuFiscalHub.Api/appsettings.Development.json` | Adicionar `"Sefaz": { "UseFakeClient": true }` |
| Create | `docs/smoke-test.http` | Coleção REST Client com fluxo completo NFC-e |
| Modify | `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeSefazClient.cs` | Adicionar `using` para o tipo movido (reexport simples) OR manter como estava — ver nota abaixo |

> **Nota sobre os fakes nos testes:** Os `FakeSefazClient` / `FakePrefeituraClient` em `tests/` continuam existindo para os testes de integração — eles precisam de `SimularRejeitado()`, `SimularDuplicidade()` etc. Os fakes em `src/Infrastructure` são versões simplificadas (sempre Autorizado) para uso manual. Não há necessidade de reutilização — mantemos os dois separados para evitar acoplamento entre test-helpers e código de produção.

---

## Task 1: FakeSefazClient em Infrastructure

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakeSefazClient.cs`

- [ ] **Step 1: Criar o arquivo**

```csharp
// src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakeSefazClient.cs
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Fiscal.Stubs;

internal sealed class FakeSefazClient : ISefazClient
{
    public Task<Result<SefazRetorno>> SubmeterAutorizacaoAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct) =>
        Task.FromResult(Result.Success(new SefazRetorno(
            Autorizado: true,
            CStat: "100",
            XMotivo: "Autorizado o uso da NF-e [FAKE]",
            NProt: "135260000000001",
            XmlAutorizado: "<protNFe/>",
            QrCodeUrl: null,
            ElapsedMs: 0L)));

    public Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
        string chaveAcesso, TenantId tenantId, TipoDocumento tipo, CancellationToken ct) =>
        Task.FromResult(Result.Success(new SefazConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            CStat: "100",
            NProt: "135260000000001",
            XmlProtocolo: null,
            ElapsedMs: 0L)));
}
```

- [ ] **Step 2: Build para verificar compilação**

```
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakeSefazClient.cs
git commit -m "feat: FakeSefazClient em Infrastructure/Fiscal/Stubs"
```

---

## Task 2: FakePrefeituraClient em Infrastructure

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakePrefeituraClient.cs`

- [ ] **Step 1: Criar o arquivo**

```csharp
// src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakePrefeituraClient.cs
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Fiscal.Stubs;

internal sealed class FakePrefeituraClient : IPrefeituraClient
{
    public Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct) =>
        Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: true,
            NumeroNfse: "1",
            Protocolo: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));

    public Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps, string serieRps, TenantId tenantId, int codigoMunicipio, CancellationToken ct) =>
        Task.FromResult(Result.Success(new PrefeituraConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            NumeroNfse: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));
}
```

- [ ] **Step 2: Build**

```
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Fiscal/Stubs/FakePrefeituraClient.cs
git commit -m "feat: FakePrefeituraClient em Infrastructure/Fiscal/Stubs"
```

---

## Task 3: Registro condicional em DependencyInjection + appsettings

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs:1-20` (usings) e `:128-137` (registros)
- Modify: `src/VisuFiscalHub.Api/appsettings.Development.json`

- [ ] **Step 1: Adicionar using no topo de DependencyInjection.cs**

Linha atual após os `using` existentes (após linha 19 `using VisuFiscalHub.Infrastructure.Services.Stubs;`), adicionar:

```csharp
using VisuFiscalHub.Infrastructure.Fiscal.Stubs;
```

- [ ] **Step 2: Substituir o bloco de registro SEFAZ (linhas 128-137) por registro condicional**

Localizar o bloco:
```csharp
        // Fase 7 — Integração SEFAZ
        // SefazHttpClient cria HttpClient por request para mTLS por-tenant — não usa factory.
        // User-Agent hardcoded em SefazHttpClient.UserAgent (constante sincronizada).
        services.AddScoped<SefazHttpClient>();
        services.AddScoped<ISefazClient, SefazClient>();

        // Fase 15 — NFS-e ABRASF
        services.AddScoped<INfseXmlBuilder, NfseXmlBuilder>();
        services.AddScoped<PrefeituraHttpClient>();
        services.AddScoped<IPrefeituraClient, PrefeituraClient>();
        services.AddScoped<PrefeituraProcessingJob>();
```

Substituir por:

```csharp
        // Fase 7 — Integração SEFAZ
        // Sefaz:UseFakeClient=true ativa stubs locais (sem certificado, sem acesso à SEFAZ).
        bool useFakeClient = configuration.GetValue<bool>("Sefaz:UseFakeClient");
        if (useFakeClient)
        {
            services.AddScoped<ISefazClient, FakeSefazClient>();
        }
        else
        {
            // SefazHttpClient cria HttpClient por request para mTLS por-tenant — não usa factory.
            services.AddScoped<SefazHttpClient>();
            services.AddScoped<ISefazClient, SefazClient>();
        }

        // Fase 15 — NFS-e ABRASF
        services.AddScoped<INfseXmlBuilder, NfseXmlBuilder>();
        if (useFakeClient)
        {
            services.AddScoped<IPrefeituraClient, FakePrefeituraClient>();
        }
        else
        {
            services.AddScoped<PrefeituraHttpClient>();
            services.AddScoped<IPrefeituraClient, PrefeituraClient>();
        }
        services.AddScoped<PrefeituraProcessingJob>();
```

- [ ] **Step 3: Adicionar flag em appsettings.Development.json**

Conteúdo atual:
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
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341"
        }
      }
    ]
  }
}
```

Substituir por:
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
        "Name": "Seq",
        "Args": {
          "serverUrl": "http://localhost:5341"
        }
      }
    ]
  },
  "Sefaz": {
    "UseFakeClient": true
  }
}
```

- [ ] **Step 4: Build + testes**

```
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --logger "console;verbosity=minimal"
```

Expected: `Build succeeded.` + `Passed! ... 761 ...`

> Os testes usam `EnvironmentName = "Test"` e substituem os clientes via `WebApplicationFactory.RemoveAll<ISefazClient>()` — não são afetados pelo flag `Sefaz:UseFakeClient`.

- [ ] **Step 5: Commit**

```
git add src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
git add src/VisuFiscalHub.Api/appsettings.Development.json
git commit -m "feat: registro condicional FakeSefazClient/FakePrefeituraClient via Sefaz:UseFakeClient"
```

---

## Task 4: Arquivo .http de smoke test

**Files:**
- Create: `docs/smoke-test.http`

O arquivo usa a sintaxe do [VS Code REST Client](https://marketplace.visualstudio.com/items?itemName=humao.rest-client) (também compatível com JetBrains HTTP Client). Cada bloco `###` é uma requisição separada. Variáveis `@nome = valor` são substituídas em `{{nome}}`.

- [ ] **Step 1: Criar docs/smoke-test.http**

```http
# VisuFiscalHub — Smoke Test NFC-e (modo fake)
# Pré-requisitos:
#   docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml up -d
#   ASPNETCORE_ENVIRONMENT=Development (ativado pelo override)
#   Sefaz:UseFakeClient=true (ativado pelo appsettings.Development.json)
#
# Sequência: 1 → 2 → 3 → 4 → 5 → 6
# Após cada request, copie o valor retornado para a variável correspondente.

@base = http://localhost:8080
@adminKey = dev_admin_key_minimo_32_caracteres_aqui_ok1

# ── Preencher após step 2 ──────────────────────────────────────────────────
@clientId = PREENCHER_APOS_STEP_2
@clientSecret = PREENCHER_APOS_STEP_2

# ── Preencher após step 3 ──────────────────────────────────────────────────
@accessToken = PREENCHER_APOS_STEP_3

# ── Preencher após step 4 ──────────────────────────────────────────────────
@tenantId = PREENCHER_APOS_STEP_4

# ── Preencher após step 6 ──────────────────────────────────────────────────
@documentoId = PREENCHER_APOS_STEP_6

###
# Step 1 — Health check
# Esperado: 200 OK  {"status":"Healthy"}
GET {{base}}/health/live

###
# Step 2 — Criar ClienteApp (requer X-Admin-Key)
# Copie clientId e clientSecret da resposta para as variáveis acima.
# Esperado: 201 Created
POST {{base}}/api/v1/clientes
Content-Type: application/json
X-Admin-Key: {{adminKey}}

{
  "clientName": "Loja Demo",
  "webhookUrl": null
}

###
# Step 3 — Autenticar (obter JWT)
# Use clientId e clientSecret do step 2.
# Copie access_token para a variável @accessToken acima.
# Esperado: 200 OK  {"access_token":"...","token_type":"Bearer","expires_in":3600}
POST {{base}}/auth/token
Content-Type: application/json

{
  "clientId": "{{clientId}}",
  "clientSecret": "{{clientSecret}}"
}

###
# Step 4 — Criar Tenant (empresa emissora)
# UfCodigo 43 = Rio Grande do Sul
# RegimeTributario: 1=SimplesNacional, 2=SimplesNacionalExcesso, 3=RegimeNormal
# Ambiente: 1=Producao, 2=Homologacao
# Copie o id retornado para @tenantId acima.
# Esperado: 201 Created
POST {{base}}/api/v1/tenants
Content-Type: application/json
Authorization: Bearer {{accessToken}}

{
  "cnpj": "11222333000181",
  "razaoSocial": "Loja Demo LTDA",
  "nomeFantasia": "Loja Demo",
  "regimeTributario": 1,
  "ambiente": 2,
  "ufCodigo": 43,
  "serie": "001",
  "serieNfe": "001",
  "inscricaoEstadual": "1234567890",
  "endereco": {
    "logradouro": "Rua das Flores",
    "numero": "100",
    "complemento": null,
    "bairro": "Centro",
    "municipio": "Porto Alegre",
    "codigoMunicipio": 4314902,
    "uf": "RS",
    "cep": "90010001"
  }
}

###
# Step 5 — Verificar CSC (necessário para NFC-e — QR Code)
# O tenant precisa de CSC configurado para NFC-e.
# Esperado: 200 OK  {"possuiCsc":false} inicialmente
GET {{base}}/api/v1/tenants/{{tenantId}}/certificado/status
Authorization: Bearer {{accessToken}}

###
# Step 5b — Configurar CSC do tenant (obrigatório para NFC-e)
# cscId e cscToken são valores fictícios para modo fake.
# Esperado: 200 OK
PUT {{base}}/api/v1/tenants/{{tenantId}}/csc
Content-Type: application/json
Authorization: Bearer {{accessToken}}

{
  "cscId": "000001",
  "cscToken": "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"
}

###
# Step 6 — Emitir NFC-e
# X-Idempotency-Key: use um UUID aleatório (mude a cada nova emissão).
# Copie o documentoId da resposta para @documentoId acima.
# Esperado: 202 Accepted  {"documentoId":"...","status":"Enfileirado"}
POST {{base}}/api/v1/documentos/nfce
Content-Type: application/json
Authorization: Bearer {{accessToken}}
X-Idempotency-Key: 550e8400-e29b-41d4-a716-446655440001

{
  "itens": [
    {
      "codigoProduto": "001",
      "descricao": "Produto Teste",
      "ncm": "62034200",
      "cest": null,
      "cfopSaida": "5102",
      "unidadeComercial": "UN",
      "quantidade": 1.0,
      "valorUnitario": 10.00,
      "valorDesconto": 0.00,
      "origemMercadoria": 0,
      "tributo": {
        "tipoIcms": 1,
        "csosnOuCst": 400,
        "aliquotaIcms": 0.00,
        "baseCalculoIcms": 0.00,
        "valorIcms": 0.00,
        "cstPis": 7,
        "baseCalculoPis": 0.00,
        "aliquotaPis": 0.00,
        "valorPis": 0.00,
        "cstCofins": 7,
        "baseCalculoCofins": 0.00,
        "aliquotaCofins": 0.00,
        "valorCofins": 0.00
      }
    }
  ],
  "pagamentos": [
    { "tipoPagamento": 1, "valor": 10.00 }
  ],
  "consumidor": null,
  "indPresenca": 1
}

###
# Step 7 — Consultar status do documento
# Aguarde ~2-5 segundos após o step 6 para o Hangfire processar.
# Esperado: 200 OK  {"status":"Autorizado","nProt":"135260000000001",...}
GET {{base}}/api/v1/documentos/{{documentoId}}/status
Authorization: Bearer {{accessToken}}
```

- [ ] **Step 2: Verificar se o arquivo tem sintaxe válida** (leitura visual — confirme que todos os `###` têm pelo menos um `\n` antes do próximo bloco e que variáveis `{{...}}` estão definidas acima como `@...`)

- [ ] **Step 3: Commit**

```
git add docs/smoke-test.http
git commit -m "docs: smoke-test.http — fluxo completo NFC-e para teste manual com FakeClient"
```

---

## Task 5: Validar smoke test manualmente

**Files:** nenhum (validação)

Este task requer que o Docker esteja rodando com o override de desenvolvimento.

- [ ] **Step 1: Confirmar que containers estão no ar**

```
docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml ps
```

Expected: `visu-fiscal-hub` com status `Up` e health `healthy`.

Se não estiver rodando:
```
docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml up -d --build
```

Aguardar ~15 segundos, então:
```
curl -s http://localhost:8080/health/live
```
Expected: `{"status":"Healthy"}`

- [ ] **Step 2: Confirmar que FakeClient está ativo nos logs**

```
docker compose -f infra/docker-compose.yml -f infra/docker-compose.override.yml logs hub | grep -i "fake\|UseFakeClient\|Sefaz"
```

Ou acesse `http://localhost:8081` (Seq) e filtre por `FakeSefazClient` — ao processar o primeiro documento, deve aparecer log do job.

> Se não aparecer log específico, prossiga mesmo assim — o comportamento correto é confirmado no step 5 (status Autorizado).

- [ ] **Step 3: Executar steps 1–7 do smoke-test.http**

Abrir `docs/smoke-test.http` no VS Code (com extensão REST Client) ou JetBrains e executar cada request em sequência, copiando os valores retornados para as variáveis no topo do arquivo.

Resultado esperado após cada step:

| Step | Endpoint | Status esperado |
|------|----------|----------------|
| 1 | GET /health/live | 200 `{"status":"Healthy"}` |
| 2 | POST /api/v1/clientes | 201 com `clientId` e `clientSecret` |
| 3 | POST /auth/token | 200 com `access_token` |
| 4 | POST /api/v1/tenants | 201 com `id` (tenantId) |
| 5b | PUT /tenants/{id}/csc | 200 |
| 6 | POST /documentos/nfce | 202 com `documentoId` e `status: "Enfileirado"` |
| 7 | GET /documentos/{id}/status | 200 com `status: "Autorizado"` |

- [ ] **Step 4: Verificar no Hangfire que o job foi processado**

Abrir `http://localhost:8080/hangfire` (modo Development — sem senha).

Em **Jobs → Succeeded**, deve aparecer o job `FiscalDocumentProcessingJob` com status `Succeeded`.

- [ ] **Step 5: Commit de log se necessário**

Se nenhuma mudança de código foi necessária, nenhum commit adicional. Caso contrário, commitar correções.

---

## Self-Review

### 1. Spec coverage

| Requisito | Task |
|-----------|------|
| FakeSefazClient em Infrastructure | Task 1 |
| FakePrefeituraClient em Infrastructure | Task 2 |
| Flag `Sefaz:UseFakeClient` em DI | Task 3 |
| Ativação em appsettings.Development.json | Task 3 |
| Arquivo .http com fluxo completo | Task 4 |
| Validação manual end-to-end | Task 5 |

### 2. Placeholder scan

- Todos os steps têm código completo. ✅
- Todos os comandos têm expected output. ✅
- Nenhum "TBD" ou "similar ao anterior". ✅

### 3. Type consistency

- `FakeSefazClient` implementa `ISefazClient` com `SubmeterAutorizacaoAsync(DocumentoFiscalId, TenantId, CancellationToken)` e `ConsultarNfeAsync(string, TenantId, TipoDocumento, CancellationToken)` — mesma assinatura do `FakeSefazClient` de testes. ✅
- `FakePrefeituraClient` implementa `IPrefeituraClient` com `EnviarRpsAsync(DocumentoFiscalId, TenantId, CancellationToken)` e `ConsultarNfseAsync(string, string, TenantId, int, CancellationToken)` — mesma assinatura do `FakePrefeituraClient` de testes. ✅
- `SefazRetorno` e `PrefeituraRetorno` usados com os mesmos campos das implementações existentes nos testes. ✅
