# Fase 9 — Docker e Deploy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Containerizar VisuFiscalHub com Dockerfile multi-stage + docker-compose completo, Hangfire Dashboard autenticado em produção, e `docker compose up` funcional com health checks passando.

**Architecture:** Dockerfile com dois stages (sdk:10.0 build → aspnet:10.0 runtime), imagem corre como usuário não-root `appuser`, todas as variáveis sensíveis via `.env`. O `docker-compose.yml` existente em `infra/` é expandido para incluir o serviço `hub` com `depends_on: postgres: condition: service_healthy`. Hangfire Dashboard usa `IDashboardAuthorizationFilter` customizado que lê credenciais de `IConfiguration` — Basic Auth em produção, acesso livre em `Development`.

**Tech Stack:** Docker multi-stage build (.NET SDK 10.0 → ASP.NET 10.0 runtime), docker-compose v3, Hangfire.AspNetCore `IDashboardAuthorizationFilter`, Basic Auth via `Authorization` header, `appsettings.json` + environment variables.

---

## File Map

| Arquivo | Ação | Responsabilidade |
|---|---|---|
| `Dockerfile` | Criar | Multi-stage build: SDK → runtime com ca-certificates e appuser |
| `infra/docker-compose.yml` | Modificar | Adicionar serviço `hub` com depends_on, healthcheck e variáveis |
| `infra/docker-compose.override.yml` | Criar | Override de desenvolvimento (ASPNETCORE_ENVIRONMENT=Development, Serilog Debug) |
| `infra/.env.example` | Criar | Template documentado de todas as variáveis obrigatórias |
| `infra/postgres/init.sql` | Criar | Script de init opcional (uuid-ossp extension) |
| `src/VisuFiscalHub.Api/Authentication/HangfireDashboardAuthFilter.cs` | Criar | `IDashboardAuthorizationFilter` com Basic Auth via IConfiguration |
| `src/VisuFiscalHub.Api/Program.cs` | Modificar | Registrar `HangfireDashboardSettings`, expor Dashboard com auth em produção |
| `src/VisuFiscalHub.Application/Common/Models/HangfireDashboardSettings.cs` | Criar | Record de configuração com SectionName |

---

## Task 1: HangfireDashboardSettings + filter de autenticação

O Hangfire Dashboard em produção precisa de autenticação. A spec exige `HangfireDashboard__User` e `HangfireDashboard__Password` via env. Vamos criar um `IDashboardAuthorizationFilter` que valida HTTP Basic Auth lendo as credenciais do `IConfiguration`.

**Files:**
- Create: `src/VisuFiscalHub.Application/Common/Models/HangfireDashboardSettings.cs`
- Create: `src/VisuFiscalHub.Api/Authentication/HangfireDashboardAuthFilter.cs`

- [ ] **Step 1: Criar HangfireDashboardSettings**

```csharp
// src/VisuFiscalHub.Application/Common/Models/HangfireDashboardSettings.cs
namespace VisuFiscalHub.Application.Common.Models;

public sealed class HangfireDashboardSettings
{
    public const string SectionName = "HangfireDashboard";

    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Criar HangfireDashboardAuthFilter**

O filtro deve:
1. Não bloquear nada em `Development` (permite acesso livre para depuração local).
2. Em qualquer outro ambiente, exigir Basic Auth com credenciais de `HangfireDashboardSettings`.
3. Responder `401 + WWW-Authenticate` se as credenciais estiverem ausentes ou erradas.
4. Usar `CryptographicOperations.FixedTimeEquals` para comparação timing-safe (mesmo padrão do `AdminKeyHelper`).

```csharp
// src/VisuFiscalHub.Api/Authentication/HangfireDashboardAuthFilter.cs
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.Extensions.Options;
using VisuFiscalHub.Application.Common.Models;

namespace VisuFiscalHub.Api.Authentication;

public sealed class HangfireDashboardAuthFilter(
    IOptions<HangfireDashboardSettings> settings,
    IWebHostEnvironment env) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // Em Development não exige autenticação — facilita depuração local
        if (env.IsDevelopment())
            return true;

        var httpContext = context.GetHttpContext();

        if (!AuthenticationHeaderValue.TryParse(
                httpContext.Request.Headers.Authorization,
                out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            Challenge(httpContext);
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch
        {
            Challenge(httpContext);
            return false;
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
        {
            Challenge(httpContext);
            return false;
        }

        var user = decoded[..separatorIndex];
        var password = decoded[(separatorIndex + 1)..];

        var enc = Encoding.UTF8;
        var hmacKey = RandomNumberGenerator.GetBytes(32);
        var userMatch = CryptographicOperations.FixedTimeEquals(
            HMACSHA256.HashData(hmacKey, enc.GetBytes(user)),
            HMACSHA256.HashData(hmacKey, enc.GetBytes(settings.Value.User)));

        var passwordMatch = CryptographicOperations.FixedTimeEquals(
            HMACSHA256.HashData(hmacKey, enc.GetBytes(password)),
            HMACSHA256.HashData(hmacKey, enc.GetBytes(settings.Value.Password)));

        if (userMatch && passwordMatch)
            return true;

        Challenge(httpContext);
        return false;
    }

    private static void Challenge(HttpContext ctx)
    {
        ctx.Response.StatusCode = 401;
        ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Hangfire Dashboard\"";
    }
}
```

- [ ] **Step 3: Compilar para verificar**

```powershell
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj -c Release 2>&1 | Select-String -Pattern "Error|error" | Where-Object { $_ -notmatch "0 Error" }
```

Expected: nenhuma linha de erro (somente warnings pré-existentes são aceitáveis).

- [ ] **Step 4: Commit**

```powershell
git add src/VisuFiscalHub.Application/Common/Models/HangfireDashboardSettings.cs `
        src/VisuFiscalHub.Api/Authentication/HangfireDashboardAuthFilter.cs
git commit -m "feat: HangfireDashboardSettings e IDashboardAuthorizationFilter com Basic Auth timing-safe"
```

---

## Task 2: Registrar HangfireDashboardSettings e expor Dashboard com auth

Modificar `Program.cs` para:
1. Registrar `HangfireDashboardSettings` via `AddOptions` + `ValidateOnStart`.
2. Trocar `if (IsDevelopment())` por `UseHangfireDashboard` sempre ativo, com `HangfireDashboardAuthFilter` injetado — o filtro já cuida de liberar em `Development`.

**Files:**
- Modify: `src/VisuFiscalHub.Api/Program.cs`

- [ ] **Step 1: Adicionar registro de HangfireDashboardSettings**

No bloco de configurações de opções em `Program.cs`, após o bloco de `AdminKeySettings`, inserir:

```csharp
builder.Services
    .AddOptions<HangfireDashboardSettings>()
    .Bind(builder.Configuration.GetSection(HangfireDashboardSettings.SectionName))
    .ValidateOnStart();
```

Adicionar o using no topo do arquivo (junto com os outros usings de Models):

```csharp
using VisuFiscalHub.Application.Common.Models;
```

(Já existe — verificar antes de duplicar.)

- [ ] **Step 2: Registrar HangfireDashboardAuthFilter no DI**

Após `builder.Services.AddExceptionHandler<GlobalExceptionHandler>();`, adicionar:

```csharp
builder.Services.AddScoped<HangfireDashboardAuthFilter>();
```

- [ ] **Step 3: Substituir o bloco do Hangfire Dashboard**

Localizar e substituir:

```csharp
// ── Hangfire Dashboard (apenas em desenvolvimento) ────────────────────────
if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire");
```

Por:

```csharp
// ── Hangfire Dashboard ────────────────────────────────────────────────────
// HangfireDashboardAuthFilter libera em Development; exige Basic Auth em outros ambientes.
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [app.Services.GetRequiredService<HangfireDashboardAuthFilter>()]
});
```

Adicionar o using necessário no topo (se ainda não existir):

```csharp
using VisuFiscalHub.Api.Authentication;
```

- [ ] **Step 4: Compilar**

```powershell
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj -c Release 2>&1 | Select-String -Pattern "Error|error" | Where-Object { $_ -notmatch "0 Error" }
```

Expected: 0 erros.

- [ ] **Step 5: Rodar os testes**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --no-build 2>&1 | tail -5
```

Expected: todos os testes passando (457 ou mais).

- [ ] **Step 6: Commit**

```powershell
git add src/VisuFiscalHub.Api/Program.cs
git commit -m "feat: Hangfire Dashboard com autenticação Basic Auth em produção via HangfireDashboardAuthFilter"
```

---

## Task 3: Dockerfile multi-stage

Criar o `Dockerfile` na raiz do repositório exatamente como especificado na Fase 9. O stage `build` usa `mcr.microsoft.com/dotnet/sdk:10.0`, copia os `.csproj` primeiro (para cache de layers), faz restore, copia o código fonte e publica em Release. O stage `runtime` usa `mcr.microsoft.com/dotnet/aspnet:10.0`, instala `ca-certificates`, cria usuário não-root `appuser` e copia o artefato publicado.

**Files:**
- Create: `Dockerfile`

- [ ] **Step 1: Verificar que o `.dockerignore` existente cobre o necessário**

```powershell
cat .dockerignore
```

O `.dockerignore` existente exclui `**/bin`, `**/obj`, `tests/`, `docs/`, `.git/`. Isso está correto — nenhuma alteração necessária.

- [ ] **Step 2: Criar o Dockerfile**

```dockerfile
# Dockerfile
# ============================================================
# Stage 1 — build
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY VisuFiscalHub.slnx ./
COPY src/VisuFiscalHub.Domain/VisuFiscalHub.Domain.csproj ./src/VisuFiscalHub.Domain/
COPY src/VisuFiscalHub.Application/VisuFiscalHub.Application.csproj ./src/VisuFiscalHub.Application/
COPY src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj ./src/VisuFiscalHub.Infrastructure/
COPY src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj ./src/VisuFiscalHub.Api/

RUN dotnet restore ./src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj

COPY src/ ./src/

RUN dotnet publish ./src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ============================================================
# Stage 2 — runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# ca-certificates necessário para validação TLS dos webservices SEFAZ (cadeia ICP-Brasil)
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl && \
    rm -rf /var/lib/apt/lists/*

RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "VisuFiscalHub.Api.dll"]
```

**Nota:** `curl` é instalado junto com `ca-certificates` pois é usado no healthcheck do `docker-compose.yml` (`CMD curl -f http://localhost:8080/health/live`). Sem `curl` na imagem, o healthcheck falha.

- [ ] **Step 3: Commit**

```powershell
git add Dockerfile
git commit -m "feat: Dockerfile multi-stage SDK→aspnet, ca-certificates, appuser não-root"
```

---

## Task 4: docker-compose.yml — adicionar serviço hub

O `infra/docker-compose.yml` já tem `postgres` e `redis`. Adicionar o serviço `hub` com `depends_on: postgres: condition: service_healthy`, healthcheck, variáveis de ambiente e configuração de rede. Também adicionar a rede `hub-network` ao arquivo.

**Files:**
- Modify: `infra/docker-compose.yml`

- [ ] **Step 1: Substituir o docker-compose.yml completo**

```yaml
# infra/docker-compose.yml
name: visu-fiscal-hub

services:
  hub:
    build:
      context: ..
      dockerfile: Dockerfile
    image: visu-fiscal-hub:latest
    container_name: visu-fiscal-hub
    ports:
      - "8080:8080"
    environment:
      ConnectionStrings__DefaultConnection: >-
        Host=postgres;Port=5432;Database=${POSTGRES_DB:-visufiscalhub};
        Username=${POSTGRES_USER:-visufiscal};Password=${POSTGRES_PASSWORD};
        Include Error Detail=true
      Jwt__PrivateKeyPem: "${JWT_PRIVATE_KEY_PEM}"
      Jwt__PublicKeyPems__0: "${JWT_PUBLIC_KEY_PEM}"
      Jwt__Issuer: "visu-fiscal-hub"
      Jwt__Audience: "visu-fiscal-hub-clients"
      Jwt__ExpiresInSeconds: "3600"
      Cert__EncryptionKey: "${CERT_ENCRYPTION_KEY}"
      AdminKey__Value: "${ADMIN_KEY}"
      HangfireDashboard__User: "${HANGFIRE_DASHBOARD_USER:-admin}"
      HangfireDashboard__Password: "${HANGFIRE_DASHBOARD_PASSWORD}"
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health/live"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 60s
    networks:
      - hub-network

  postgres:
    image: postgres:17-alpine
    container_name: visu-fiscal-postgres
    ports:
      - "${POSTGRES_PORT:-5432}:5432"
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-visufiscalhub}
      POSTGRES_USER: ${POSTGRES_USER:-visufiscal}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Definir POSTGRES_PASSWORD no .env}
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./postgres/init.sql:/docker-entrypoint-initdb.d/init.sql:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-visufiscal} -d ${POSTGRES_DB:-visufiscalhub}"]
      interval: 10s
      timeout: 5s
      retries: 5
    networks:
      - hub-network
    restart: unless-stopped

  redis:
    image: redis:7-alpine
    container_name: visu-fiscal-redis
    ports:
      - "${REDIS_PORT:-6379}:6379"
    volumes:
      - redis_data:/data
    command: redis-server --appendonly yes
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3
    networks:
      - hub-network
    restart: unless-stopped

volumes:
  postgres_data:
    driver: local
  redis_data:
    driver: local

networks:
  hub-network:
    driver: bridge
```

- [ ] **Step 2: Commit**

```powershell
git add infra/docker-compose.yml
git commit -m "feat: adicionar serviço hub ao docker-compose com depends_on e healthcheck"
```

---

## Task 5: docker-compose.override.yml, .env.example e postgres/init.sql

Criar os arquivos complementares da infraestrutura.

**Files:**
- Create: `infra/docker-compose.override.yml`
- Create: `infra/.env.example`
- Create: `infra/postgres/init.sql`

- [ ] **Step 1: Criar docker-compose.override.yml**

```yaml
# infra/docker-compose.override.yml
# Sobreposições para desenvolvimento local — NÃO usar em produção
services:
  hub:
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      Serilog__MinimumLevel__Default: Debug
      HangfireDashboard__User: ""
      HangfireDashboard__Password: ""
    volumes:
      - ../src/VisuFiscalHub.Api/appsettings.Development.json:/app/appsettings.Development.json:ro
```

- [ ] **Step 2: Criar infra/.env.example**

```dotenv
# ============================================================
# VisuFiscalHub — Variáveis de Ambiente
# Copiar para .env e preencher os valores reais
# NUNCA versionar o arquivo .env
# ============================================================

# PostgreSQL
POSTGRES_DB=visufiscalhub
POSTGRES_USER=visufiscal
POSTGRES_PASSWORD=change_me_strong_password_here
POSTGRES_PORT=5432

# JWT RS256 — chave privada (formato PEM com \n literal)
# Gerar: openssl genrsa -out private.pem 2048
# Exportar: awk 'NR==1{printf "%s",$0;next}{printf "\\n%s",$0}' private.pem
JWT_PRIVATE_KEY_PEM=-----BEGIN RSA PRIVATE KEY-----\nMIIEpA...INSERT_REAL_KEY...\n-----END RSA PRIVATE KEY-----

# JWT RS256 — chave pública (formato PEM com \n literal)
# Derivar: openssl rsa -in private.pem -pubout | awk 'NR==1{printf "%s",$0;next}{printf "\\n%s",$0}'
JWT_PUBLIC_KEY_PEM=-----BEGIN PUBLIC KEY-----\nMIIBIj...INSERT_REAL_KEY...\n-----END PUBLIC KEY-----

# AES-256-GCM para criptografia de certificados (base64, 32 bytes)
# Gerar: openssl rand -base64 32
CERT_ENCRYPTION_KEY=base64_encoded_32_byte_key_replace_this

# Chave administrativa (mínimo 32 caracteres)
# Gerar: openssl rand -hex 32
ADMIN_KEY=generate_random_hex_64_chars_here

# Hangfire Dashboard
HANGFIRE_DASHBOARD_USER=admin
HANGFIRE_DASHBOARD_PASSWORD=change_me_strong_password_here

# Redis (porta exposta — opcional alterar)
REDIS_PORT=6379
```

- [ ] **Step 3: Criar infra/postgres/init.sql**

```sql
-- Extensão uuid-ossp disponível caso necessário
-- O EF Core usa gen_random_uuid() (pgcrypto/built-in) — este script é opcional
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
```

- [ ] **Step 4: Verificar que .gitignore já ignora .env**

```powershell
Select-String -Path .gitignore -Pattern "^\.env$|^infra/\.env$|\*\.env"
```

Se não encontrar, adicionar ao `.gitignore`:

```
infra/.env
```

- [ ] **Step 5: Commit**

```powershell
git add infra/docker-compose.override.yml infra/.env.example infra/postgres/init.sql
git commit -m "feat: docker-compose.override, .env.example e postgres/init.sql"
```

---

## Task 6: Atualizar .dockerignore

O `.dockerignore` existente cobre a maioria dos casos mas precisa de ajustes menores: deve excluir `infra/` (não faz parte do build) e scripts de desenvolvimento.

**Files:**
- Modify: `.dockerignore`

- [ ] **Step 1: Verificar conteúdo atual**

```powershell
cat .dockerignore
```

Conteúdo atual:
```
**/.vs
**/.vscode
**/.idea
**/bin
**/obj
**/*.user
**/*.suo
.git
.gitignore
.claude
docs
tests
*.md
```

- [ ] **Step 2: Atualizar .dockerignore**

Substituir por:

```dockerignore
# Build artifacts
**/bin/
**/obj/

# Test projects
tests/

# Documentação e configs de IDE
docs/
.claude/
.vs/
.vscode/
.idea/
*.md
*.user
*.suo

# Git
.git/
.gitignore

# Docker e infra (não fazem parte do build da aplicação)
Dockerfile
infra/
scripts/
```

- [ ] **Step 3: Commit**

```powershell
git add .dockerignore
git commit -m "chore: atualizar .dockerignore — excluir infra/ e scripts/"
```

---

## Task 7: Verificação final — build da imagem Docker

Verificar que `docker build` completa sem erros.

**Files:** nenhum (verificação apenas)

- [ ] **Step 1: Verificar que Docker está disponível**

```powershell
docker --version
```

Expected: `Docker version 24.x` ou superior.

- [ ] **Step 2: Build da imagem**

Na raiz do repositório:

```powershell
docker build -t visu-fiscal-hub:test .
```

Expected: `Successfully built <hash>` e `Successfully tagged visu-fiscal-hub:test`.

Pontos de atenção:
- Stage 1 `dotnet restore` deve completar sem erros de NuGet.
- Stage 1 `dotnet publish -c Release` deve completar sem erros de compilação.
- Stage 2 `apt-get install ca-certificates curl` deve completar.
- Imagem final deve existir: `docker images visu-fiscal-hub:test`.

- [ ] **Step 3: Verificar que a imagem roda e responde ao health check**

```powershell
# Subir apenas o postgres para o hub poder iniciar
cd infra
docker compose up -d postgres
Start-Sleep -Seconds 15

# Rodar o hub com variáveis mínimas de teste (sem SEFAZ real)
docker run --rm -d `
  --name hub-test `
  --network infra_hub-network `
  -p 8080:8080 `
  -e "ConnectionStrings__DefaultConnection=Host=visu-fiscal-postgres;Port=5432;Database=visufiscalhub;Username=visufiscal;Password=test_pw" `
  -e "Jwt__PrivateKeyPem=<<VER NOTA ABAIXO>>" `
  -e "Jwt__PublicKeyPems__0=<<VER NOTA ABAIXO>>" `
  -e "Jwt__Issuer=visu-fiscal-hub" `
  -e "Jwt__Audience=visu-fiscal-hub-clients" `
  -e "Jwt__ExpiresInSeconds=3600" `
  -e "Cert__EncryptionKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=" `
  -e "AdminKey__Value=00000000000000000000000000000000" `
  -e "HangfireDashboard__User=admin" `
  -e "HangfireDashboard__Password=admin" `
  -e "ASPNETCORE_ENVIRONMENT=Development" `
  visu-fiscal-hub:test
```

**NOTA JWT para teste:** Gerar um par RSA temporário:
```powershell
# No WSL/Git Bash:
openssl genrsa -out /tmp/test.pem 2048
openssl rsa -in /tmp/test.pem -pubout -out /tmp/test_pub.pem
# Converter para \n literals:
awk 'NR==1{printf "%s",$0;next}{printf "\\n%s",$0}' /tmp/test.pem
awk 'NR==1{printf "%s",$0;next}{printf "\\n%s",$0}' /tmp/test_pub.pem
```

- [ ] **Step 4: Verificar health check**

```powershell
Start-Sleep -Seconds 30  # aguardar startup e migrations
Invoke-RestMethod -Uri "http://localhost:8080/health/live"
```

Expected: `{ "status": "Healthy" }` (ou similar).

```powershell
Invoke-RestMethod -Uri "http://localhost:8080/health"
```

Expected: status 200 com `{ "status": "Healthy" }` (requer postgres up e acessível).

- [ ] **Step 5: Limpar containers de teste**

```powershell
docker stop hub-test
cd infra
docker compose down
```

- [ ] **Step 6: Rodar testes da suite**

```powershell
cd ..  # voltar para raiz do repo
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | tail -5
```

Expected: todos os testes passando.

- [ ] **Step 7: Commit final**

```powershell
git add .
git commit -m "feat: implementar Fase 9 — verificação docker build e compose completos"
```

---

## Self-Review

### 1. Spec Coverage

| Requisito da spec | Task |
|---|---|
| Dockerfile 2 stages: sdk:10.0 (build) e aspnet:10.0 (runtime) | Task 3 |
| `dotnet publish -c Release` no stage build | Task 3 |
| `ca-certificates` via apt-get no runtime | Task 3 |
| `EXPOSE 8080` no Dockerfile | Task 3 |
| `ENTRYPOINT ["dotnet", "VisuFiscalHub.Api.dll"]` | Task 3 |
| Usuário não-root `appuser` | Task 3 |
| docker-compose.yml com hub, postgres, redis | Task 4 |
| `hub` depends_on `postgres` com `condition: service_healthy` | Task 4 |
| Variáveis sensíveis via `${VAR_NAME}` | Task 4, 5 |
| `.env.example` documenta todas as variáveis | Task 5 |
| `.dockerignore` exclui tests/, docs/, bin/, obj/ | Task 6 |
| `GET /health` retorna Healthy | Task 7 |
| Hangfire Dashboard acessível com credenciais | Task 1, 2 |
| `POST /auth/token` responde com JWT | Task 7 (verificação) |

### 2. Placeholder Scan

Sem placeholders — todos os steps têm código ou comandos completos.

### 3. Type Consistency

- `HangfireDashboardSettings.SectionName` = `"HangfireDashboard"` → corresponde às variáveis `HangfireDashboard__User` e `HangfireDashboard__Password` no docker-compose.yml.
- `HangfireDashboardAuthFilter` recebe `IOptions<HangfireDashboardSettings>` e `IWebHostEnvironment` — ambos registráveis via DI padrão do ASP.NET Core.
- `builder.Services.AddScoped<HangfireDashboardAuthFilter>()` → resolvido em `app.Services.GetRequiredService<HangfireDashboardAuthFilter>()` após `app.Build()`.
- `ConnectionStrings__DefaultConnection` no docker-compose corresponde à chave `GetConnectionString("DefaultConnection")` usada em `DependencyInjection.cs`.
