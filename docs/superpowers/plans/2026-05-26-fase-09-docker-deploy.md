# Fase 9 — Docker e Deploy: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corrigir bugs no setup Docker existente e validar que `docker compose up` sobe sem erros com todos os critérios da spec atendidos.

**Architecture:** A infraestrutura Docker já existe quase completa (Dockerfile, docker-compose.yml, .dockerignore, .env.example, appsettings.Docker.json). O trabalho é corrigir dois bugs conhecidos, commitar arquivos pendentes, e validar o stack end-to-end.

**Tech Stack:** Docker 29+, Docker Compose v2, .NET 10, PostgreSQL 17-alpine, Seq (dev overlay)

---

## Estado Atual — O Que Já Existe

Estes arquivos já existem e estão corretos (não modificar sem motivo):

| Arquivo | Status |
|---------|--------|
| `Dockerfile` | ✅ Correto — 2 stages (sdk:10.0 + aspnet:10.0), appuser, ca-certificates, curl, EXPOSE 8080 |
| `infra/docker-compose.yml` | ✅ Correto — hub + postgres, healthcheck, depends_on, hub-network bridge |
| `infra/.env.example` | ✅ Correto — todas as variáveis documentadas |
| `infra/postgres/init.sql` | ✅ Correto — uuid-ossp extension |
| `src/VisuFiscalHub.Api/appsettings.Docker.json` | ✅ Correto — Serilog + Seq em http://seq:5341 |
| `src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs` | ⚠️ Modificado, não commitado |
| `src/VisuFiscalHub.Infrastructure/Persistence/CorrelationIdAmbient.cs` | ⚠️ Novo, não commitado |

**Bugs conhecidos:**

1. **`.dockerignore` tem UTF-8 BOM** — encoding incorreto. Docker CLI no Linux lê o BOM como conteúdo literal `\xEF\xBB\xBF` e pode falhar a parse do arquivo. Deve ser UTF-8 sem BOM.

2. **`infra/docker-compose.override.yml` declara `hub-network: external: true`** — conflita com `docker-compose.yml` que cria `hub-network` como rede bridge local. Quando o override é carregado (padrão do `docker compose up`), o Compose tenta usar uma rede externa que não existe, causando erro `network hub-network declared as external, but could not be found`. A rede deve ser removida do override (já definida no compose principal).

---

## Arquivos a Criar/Modificar

| Arquivo | Ação |
|---------|------|
| `.dockerignore` | Reescrever sem BOM (UTF-8 puro) |
| `infra/docker-compose.override.yml` | Remover `networks: hub-network: external: true` |
| `infra/.env` | Criar para dev local (não versionado — já no .gitignore) |

---

## Task 1: Commitar arquivos pendentes de Fase 13

Os arquivos `CorrelationIdMiddleware.cs` e `CorrelationIdAmbient.cs` foram modificados mas não commitados. Precisam estar no repo antes de prosseguir.

**Files:**
- Verify: `src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs`
- Verify: `src/VisuFiscalHub.Infrastructure/Persistence/CorrelationIdAmbient.cs`

- [ ] **Step 1: Verificar o que está pendente**

```bash
git status --short
git diff HEAD -- src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs
git diff HEAD -- src/VisuFiscalHub.Infrastructure/Persistence/CorrelationIdAmbient.cs
```

Se `CorrelationIdMiddleware.cs` aparecer como `M` (modified) e `CorrelationIdAmbient.cs` como `??` (untracked), precisam ser commitados.

- [ ] **Step 2: Rodar testes para garantir que não há regressão**

```bash
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --no-build
```

Expected: todos os testes passando (756+).

- [ ] **Step 3: Commitar os arquivos pendentes**

```bash
git add src/VisuFiscalHub.Api/Middleware/CorrelationIdMiddleware.cs
git add src/VisuFiscalHub.Infrastructure/Persistence/CorrelationIdAmbient.cs
git commit -m "feat: CorrelationIdMiddleware sanitiza header e propaga via CorrelationIdAmbient"
```

Se `git status` mostrar que esses arquivos já estão commitados (sem modificações pendentes), pular este task e ir para o Task 2.

---

## Task 2: Corrigir `.dockerignore` (remover BOM)

O `.dockerignore` atual tem encoding UTF-8 with BOM (`\xEF\xBB\xBF` no início do arquivo). O Docker CLI interpreta o BOM como conteúdo literal, podendo causar falha na parse. Reescrever com UTF-8 sem BOM.

**Files:**
- Modify: `.dockerignore`

- [ ] **Step 1: Verificar encoding atual**

```bash
file .dockerignore
# Expected: ".dockerignore: Unicode text, UTF-8 (with BOM) text"
# Confirma o bug
```

- [ ] **Step 2: Reescrever o arquivo sem BOM**

Reescrever `.dockerignore` com exatamente este conteúdo (UTF-8 sem BOM):

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
infra/
scripts/
```

Usar o Write tool para criar o arquivo. O Write tool grava UTF-8 sem BOM por padrão.

- [ ] **Step 3: Verificar encoding após reescrita**

```bash
file .dockerignore
# Expected: ".dockerignore: ASCII text"
# ou: ".dockerignore: UTF-8 Unicode text" (sem "with BOM")
```

- [ ] **Step 4: Commitar**

```bash
git add .dockerignore
git commit -m "fix: remover UTF-8 BOM do .dockerignore"
```

---

## Task 3: Corrigir `docker-compose.override.yml` (remover rede externa conflitante)

O override atual declara `hub-network: external: true`, mas `docker-compose.yml` já cria `hub-network` como rede bridge local. Quando o Docker Compose mescla os dois arquivos, tenta usar uma rede externa que não existe no host, causando:

```
network hub-network declared as external, but could not be found
```

A solução é remover a seção `networks` do override — a rede bridge já definida no compose principal será usada automaticamente para todos os serviços.

**Files:**
- Modify: `infra/docker-compose.override.yml`

- [ ] **Step 1: Reescrever o override sem a seção de rede externa**

Reescrever `infra/docker-compose.override.yml` com este conteúdo:

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

  postgres:
    ports:
      - "${POSTGRES_PORT:-5432}:5432"

volumes:
  seq-data:
```

Nota: `seq` não precisa de `networks` explícita — por padrão, todos os serviços definidos no override herdam a rede `hub-network` do compose principal.

- [ ] **Step 2: Validar sintaxe do compose**

No diretório `infra/`:

```bash
cd infra
docker compose config --quiet
```

Expected: sem erros. Se houver erros de YAML, corrigi-los antes de continuar.

- [ ] **Step 3: Commitar**

```bash
git add infra/docker-compose.override.yml
git commit -m "fix: remover hub-network external do override (conflitava com rede bridge do compose principal)"
```

---

## Task 4: Criar `infra/.env` para validação local

O arquivo `infra/.env` não está no repositório (está no `.gitignore` corretamente). Para executar `docker compose up`, ele precisa existir com valores funcionais. Este task cria o `.env` com chaves RSA geradas localmente e senhas de desenvolvimento.

**Files:**
- Create: `infra/.env` (NÃO versionado — já no .gitignore)

- [ ] **Step 1: Gerar chave RSA para JWT**

```bash
# Gerar chave privada RSA 2048
openssl genrsa -out /tmp/jwt-private.pem 2048

# Exportar chave pública
openssl rsa -in /tmp/jwt-private.pem -pubout -out /tmp/jwt-public.pem

# Formatar para linha única (substituir newlines por \n literal)
# PowerShell:
$priv = (Get-Content /tmp/jwt-private.pem) -join '\n'
$pub  = (Get-Content /tmp/jwt-public.pem)  -join '\n'
```

- [ ] **Step 2: Gerar chave AES para certificados**

```bash
# PowerShell:
$certKey = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

Ou via openssl:

```bash
openssl rand -base64 32
```

- [ ] **Step 3: Criar `infra/.env`**

Criar `infra/.env` com o seguinte template preenchido com os valores gerados acima:

```dotenv
# VisuFiscalHub — .env para desenvolvimento local
# NÃO versionar este arquivo

POSTGRES_DB=visufiscalhub
POSTGRES_USER=visufiscal
POSTGRES_PASSWORD=dev_password_local_only

JWT_PRIVATE_KEY_PEM=-----BEGIN RSA PRIVATE KEY-----\nMII...CONTEUDO_DA_CHAVE_PRIVADA...\n-----END RSA PRIVATE KEY-----
JWT_PUBLIC_KEY_PEM=-----BEGIN PUBLIC KEY-----\nMII...CONTEUDO_DA_CHAVE_PUBLICA...\n-----END PUBLIC KEY-----

CERT_ENCRYPTION_KEY=CONTEUDO_BASE64_32_BYTES_AQUI

ADMIN_KEY=dev_admin_key_minimo_32_caracteres_aqui_1234

HANGFIRE_DASHBOARD_USER=admin
HANGFIRE_DASHBOARD_PASSWORD=dev_hangfire_senha

POSTGRES_PORT=5432
```

**Importante:** os valores de `JWT_PRIVATE_KEY_PEM` e `JWT_PUBLIC_KEY_PEM` devem ter os newlines representados como `\n` literal (não quebras de linha reais). Usar os valores exportados no Step 1.

- [ ] **Step 4: Verificar que .env não foi adicionado ao git**

```bash
git status --short infra/.env
```

Expected: nenhuma saída (arquivo ignorado). Se aparecer, verificar `.gitignore`.

---

## Task 5: Validar `docker compose build`

Antes de subir o stack completo, verificar que o build da imagem funciona sem erros.

**Files:** nenhum

- [ ] **Step 1: Build da imagem Docker**

A partir da raiz do repositório:

```bash
cd infra
docker compose build --no-cache hub
```

Expected: build completo sem erros. O processo deve:
1. Copiar os `.csproj` e rodar `dotnet restore`
2. Copiar `src/` e rodar `dotnet publish -c Release`
3. Criar imagem runtime com `aspnet:10.0`

Se o build falhar com erro de restore/compile, investigar a mensagem de erro — pode ser dependência faltando ou versão de SDK incompatível.

- [ ] **Step 2: Verificar tamanho da imagem**

```bash
docker image ls visu-fiscal-hub:latest
```

Expected: imagem entre 200-400 MB (runtime ASP.NET sem SDK).

- [ ] **Step 3: Verificar que tests/ não estão na imagem**

```bash
docker run --rm visu-fiscal-hub:latest ls /app | grep -i test
```

Expected: nenhuma saída (diretório `tests/` excluído pelo `.dockerignore`).

---

## Task 6: Validar `docker compose up` end-to-end

Subir o stack completo e verificar todos os critérios de conclusão da spec.

**Files:** nenhum

- [ ] **Step 1: Subir o stack**

```bash
cd infra
docker compose up -d
```

Expected: sem erros. O Docker Compose deve:
1. Criar a rede `hub-network` (bridge)
2. Subir `postgres` e aguardar healthcheck
3. Subir `hub` após postgres healthy

- [ ] **Step 2: Aguardar startup e verificar logs**

```bash
docker compose logs hub --tail=30
```

Expected nos logs:
- `Serilog` inicializado
- `MigrateAsync` aplicando migrations
- `Kestrel` listening em `http://[::]:8080`
- Nenhuma exception durante startup

- [ ] **Step 3: Verificar health endpoint**

```bash
curl -s http://localhost:8080/health | python3 -m json.tool
# ou (PowerShell):
Invoke-RestMethod http://localhost:8080/health | ConvertTo-Json
```

Expected:

```json
{
  "status": "Healthy"
}
```

- [ ] **Step 4: Verificar health/live (usado pelo healthcheck do Compose)**

```bash
curl -f http://localhost:8080/health/live
```

Expected: HTTP 200, `{"status":"Healthy"}`.

- [ ] **Step 5: Testar autenticação**

Primeiro, criar um ClienteApp via admin endpoint:

```bash
curl -s -X POST http://localhost:8080/admin/cliente-apps \
  -H "Content-Type: application/json" \
  -H "X-Admin-Key: dev_admin_key_minimo_32_caracteres_aqui_1234" \
  -d '{"name": "Test App"}' | python3 -m json.tool
```

Expected: `201 Created` com `clientId` e `clientSecret`.

Salvar `clientId` e `clientSecret` da resposta e usar para obter token:

```bash
curl -s -X POST http://localhost:8080/auth/token \
  -H "Content-Type: application/json" \
  -d '{"clientId": "CLI_ID_AQUI", "clientSecret": "CLIENT_SECRET_AQUI"}' | python3 -m json.tool
```

Expected: `200 OK` com `access_token`, `token_type: "Bearer"`, `expires_in: 3600`.

- [ ] **Step 6: Verificar Hangfire Dashboard**

Abrir no browser: `http://localhost:8080/hangfire`

O override define `HangfireDashboard__User: ""` e `HangfireDashboard__Password: ""` para desenvolvimento (sem autenticação). Expected: dashboard do Hangfire acessível mostrando jobs.

- [ ] **Step 7: Teardown**

```bash
cd infra
docker compose down
```

Expected: todos os containers parados e removidos. Volume `postgres_data` preservado.

---

## Task 7: Validar `docker compose up` sem override (modo produção simulado)

Testar explicitamente sem o override para garantir que o modo produção funciona com autenticação no Hangfire.

**Files:** nenhum

- [ ] **Step 1: Subir sem override**

```bash
cd infra
docker compose -f docker-compose.yml up -d
```

Expected: sobe apenas hub + postgres (sem seq). Hub usa `ASPNETCORE_ENVIRONMENT: Production`.

- [ ] **Step 2: Verificar health**

```bash
curl -f http://localhost:8080/health/live
```

Expected: HTTP 200.

- [ ] **Step 3: Verificar Hangfire com autenticação**

```bash
# Sem credenciais — deve retornar 401 ou redirecionar
curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/hangfire
```

Expected: `401` ou `302` (redirect para login).

```bash
# Com credenciais corretas
curl -s -u "admin:dev_hangfire_senha" http://localhost:8080/hangfire -L -o /dev/null -w "%{http_code}"
```

Expected: `200`.

- [ ] **Step 4: Teardown**

```bash
cd infra
docker compose -f docker-compose.yml down
```

---

## Task 8: Commit final e verificação do checklist

- [ ] **Step 1: Verificar git status**

```bash
git status --short
```

Todos os arquivos modificados devem estar commitados. Apenas `infra/.env` deve aparecer como ignorado (não rastreado).

- [ ] **Step 2: Verificar checklist completo da spec**

```
✅ Dockerfile tem exatamente 2 stages: sdk:10.0 (build) e aspnet:10.0 (runtime)
✅ Stage build usa dotnet publish -c Release
✅ Stage runtime instala ca-certificates via apt-get
✅ EXPOSE 8080 presente no Dockerfile
✅ ENTRYPOINT ["dotnet", "VisuFiscalHub.Api.dll"] presente
✅ Usuário não-root (appuser) no stage runtime
✅ docker-compose.yml tem serviços: hub, postgres
✅ hub depende de postgres com condition: service_healthy
✅ Variáveis sensíveis via ${VAR_NAME} — nunca hardcoded
✅ .env.example documenta todas as variáveis necessárias
✅ .dockerignore exclui tests/, docs/, bin/, obj/
✅ docker compose up sobe sem erros
✅ GET /health retorna 200 Healthy após startup
✅ Hangfire Dashboard acessível em /hangfire com credenciais do .env
✅ POST /auth/token com credenciais válidas retorna JWT
✅ Aplicação expõe porta 8080
```

- [ ] **Step 3: Rodar testes unitários/integração para garantir nenhuma regressão**

```bash
cd ..  # raiz do repositório
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --no-build
```

Expected: todos os testes passando.

- [ ] **Step 4: Commit final se houver mudanças residuais**

```bash
git add -A  # apenas arquivos rastreados — .env está no .gitignore
git status  # verificar que .env NÃO está incluído
git commit -m "chore: fase 9 docker e deploy — build validado end-to-end"
```

---

## Referência Rápida — Comandos Docker

```bash
# Build da imagem
cd infra && docker compose build hub

# Subir stack completo (dev — inclui seq)
cd infra && docker compose up -d

# Subir apenas produção (sem override)
cd infra && docker compose -f docker-compose.yml up -d

# Ver logs
docker compose logs hub -f

# Verificar healthcheck
curl http://localhost:8080/health
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready

# Derrubar stack
cd infra && docker compose down

# Derrubar e remover volumes
cd infra && docker compose down -v
```
