# Spec Fase 9 — Docker e Deploy

**Versão:** 1.0
**Data:** 2026-05-12
**Dependências:** Fase 8 concluída
**Critério de conclusão:** `docker compose up` sobe sem erros; `GET /health` retorna Healthy; Hangfire Dashboard acessível com autenticação

---

## 1. Visão Geral

### Objetivo

Containerizar o VisuFiscalHub para execução completa com `docker compose up`. Imagem multi-stage com build seguro, runtime mínimo, suporte a certificados ICP-Brasil para mTLS com o SEFAZ, e configuração completa via variáveis de ambiente.

### Dependências diretas

| Dependência | Fase |
|---|---|
| `Program.cs` compilando e com migrations | Fase 8 |
| `VisuFiscalHub.Api.csproj` publicável | Fase 8 |
| Solução `.slnx` completa | Todas as fases anteriores |

### Critério de conclusão

- [ ] `docker compose up` sobe sem erros
- [ ] `GET /health` retorna `{ "status": "Healthy" }` com PostgreSQL up
- [ ] Hangfire Dashboard acessível em `/hangfire` com usuário e senha configurados
- [ ] `POST /auth/token` responde com JWT válido
- [ ] Aplicação EXPÕE porta 8080
- [ ] Imagem usa stage SDK para build e stage aspnet para runtime
- [ ] Nenhuma chave privada ou secret no Dockerfile ou docker-compose.yml

---

## 2. Árvore de Arquivos

```
/  (raiz do repositório)
├── Dockerfile
infra/
├── docker-compose.yml              ← orquestração completa
├── docker-compose.override.yml     ← sobreposições de desenvolvimento
├── .env.example                    ← template de variáveis obrigatórias
└── postgres/
    └── init.sql                    ← scripts opcionais de inicialização (ex: criar extensions)
```

---

## 3. Arquivos — Especificações Detalhadas

### 3.1 `Dockerfile`

**Localização:** raiz do repositório (`/Dockerfile`)

```dockerfile
# ============================================================
# Stage 1 — build
# mcr.microsoft.com/dotnet/sdk:10.0 inclui compilador e CLI
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar solution e csproj separados para cache de layers
COPY VisuFiscalHub.slnx ./
COPY src/VisuFiscalHub.Domain/VisuFiscalHub.Domain.csproj ./src/VisuFiscalHub.Domain/
COPY src/VisuFiscalHub.Application/VisuFiscalHub.Application.csproj ./src/VisuFiscalHub.Application/
COPY src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj ./src/VisuFiscalHub.Infrastructure/
COPY src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj ./src/VisuFiscalHub.Api/

# Restore isolado para cache eficiente
RUN dotnet restore ./src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj

# Copiar código fonte completo
COPY src/ ./src/

# Publish em modo Release
RUN dotnet publish ./src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ============================================================
# Stage 2 — runtime
# mcr.microsoft.com/dotnet/aspnet:10.0 — apenas runtime ASP.NET
# Menor superfície de ataque que SDK
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# ca-certificates: necessário para validação da cadeia ICP-Brasil
# O SEFAZ emite certificados assinados por autoridades ICP-Brasil.
# Sem esses certificados, o handshake TLS com os webservices SEFAZ falhará.
# update-ca-certificates importa para o trust store do sistema.
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates && \
    rm -rf /var/lib/apt/lists/*

# NOTA sobre ICP-Brasil:
# A imagem base debian/ubuntu pode não incluir as ACs raízes e intermediárias
# da ICP-Brasil por padrão. Se a validação mTLS falhar com erro de cadeia,
# adicionar os certificados ICP-Brasil manualmente:
#
# COPY infra/certs/icp-brasil/ /usr/local/share/ca-certificates/icp-brasil/
# RUN update-ca-certificates
#
# Obter certificados em: https://www.gov.br/iti/pt-br/assuntos/repositorio/repositorio-de-certificados
# Necessários: AC Raiz Brasileira v2, AC Raiz Brasileira v5, ACs intermediárias dos emissores de A1

# Usuário não-root para segurança
RUN adduser --disabled-password --gecos "" appuser
USER appuser

# Copiar artefatos publicados do stage build
COPY --from=build /app/publish .

# Porta padrão do ASP.NET Core em containers
EXPOSE 8080

# Variável de ambiente para que o Kestrel escute na porta 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "VisuFiscalHub.Api.dll"]
```

**Notas de implementação:**
- Stage 1 usa `mcr.microsoft.com/dotnet/sdk:10.0` — inclui `dotnet publish`
- Stage 2 usa `mcr.microsoft.com/dotnet/aspnet:10.0` — runtime apenas (~100MB menor que SDK)
- `UseAppHost=false` evita gerar executável nativo (desnecessário em container Linux)
- `COPY csproj` antes do `COPY src/` maximiza cache de layers do Docker
- `--no-restore` no publish porque restore já foi feito em layer separada
- `apt-get install ca-certificates` é obrigatório para validação TLS dos webservices SEFAZ
- Usuário não-root (`appuser`) por boas práticas de segurança
- Certificados ICP-Brasil: adicionar manualmente se a validação do SEFAZ falhar (ver comentário no Dockerfile)

---

### 3.2 `infra/docker-compose.yml`

```yaml
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
      # Conexão com banco
      ConnectionStrings__Default: >-
        Host=postgres;Port=5432;Database=visufiscalhub;
        Username=visufiscal;Password=${POSTGRES_PASSWORD};
        Include Error Detail=true
      # JWT RS256
      JWT__PrivateKeyPem: "${JWT_PRIVATE_KEY_PEM}"
      JWT__PublicKeyPems__0: "${JWT_PUBLIC_KEY_PEM}"
      # Criptografia de certificados
      CERT__EncryptionKey: "${CERT_ENCRYPTION_KEY}"
      # Chave administrativa
      AdminKey__Value: "${ADMIN_KEY}"
      # Tabela IBPT
      IBPT__TabelaVersao: "${IBPT_TABELA_VERSAO}"
      IBPT__DataReferencia: "${IBPT_DATA_REFERENCIA}"
      # Hangfire Dashboard
      HangfireDashboard__User: "${HANGFIRE_DASHBOARD_USER:-admin}"
      HangfireDashboard__Password: "${HANGFIRE_DASHBOARD_PASSWORD}"
      # Runtime
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
      - "5432:5432"
    environment:
      POSTGRES_DB: visufiscalhub
      POSTGRES_USER: visufiscal
      POSTGRES_PASSWORD: "${POSTGRES_PASSWORD}"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./postgres/init.sql:/docker-entrypoint-initdb.d/init.sql:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U visufiscal -d visufiscalhub"]
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
      - "6379:6379"
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

**Notas:**
- `depends_on: postgres: condition: service_healthy` garante que o hub só sobe após o PostgreSQL estar aceitando conexões
- Redis reservado para cache futuro (HybridCache L2) — presente na infraestrutura mas não usado nas Fases 1-10
- Todas as variáveis sensíveis via `.env` — NUNCA hardcoded no `docker-compose.yml`
- `restart: unless-stopped` para resiliência em produção

---

### 3.3 `infra/docker-compose.override.yml`

Sobreposições para desenvolvimento local:

```yaml
# docker-compose.override.yml — apenas para desenvolvimento
# Não usar em produção

services:
  hub:
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      # Em desenvolvimento, logar mais detalhes
      Serilog__MinimumLevel__Default: Debug
      # Hangfire Dashboard sem autenticação em dev (remover em produção!)
      HangfireDashboard__User: ""
      HangfireDashboard__Password: ""
    volumes:
      # Permite hot-reload de appsettings em desenvolvimento
      - ../src/VisuFiscalHub.Api/appsettings.Development.json:/app/appsettings.Development.json:ro
```

---

### 3.4 `infra/.env.example`

Template de todas as variáveis de ambiente necessárias:

```dotenv
# ============================================================
# VisuFiscalHub — Variáveis de Ambiente
# Copiar para .env e preencher os valores reais
# NUNCA versionar o arquivo .env no Git
# ============================================================

# PostgreSQL
POSTGRES_PASSWORD=change_me_strong_password_here

# JWT RS256 — chave privada em formato PEM
# Gerar com: openssl genrsa -out private.pem 2048
# Exportar como: cat private.pem | awk 'NR==1{printf "%s",$0;next}{printf "\\n%s",$0}END{print ""}'
JWT_PRIVATE_KEY_PEM=-----BEGIN RSA PRIVATE KEY-----\nMIIEpA...INSERT_REAL_KEY...\n-----END RSA PRIVATE KEY-----

# JWT RS256 — chave pública em formato PEM
# Derivar de: openssl rsa -in private.pem -pubout -out public.pem
JWT_PUBLIC_KEY_PEM=-----BEGIN PUBLIC KEY-----\nMIIBIj...INSERT_REAL_KEY...\n-----END PUBLIC KEY-----

# Chave AES-256-GCM para criptografia de certificados
# Gerar com: openssl rand -base64 32
CERT_ENCRYPTION_KEY=base64_encoded_32_byte_key_replace_this

# Chave administrativa (mínimo 32 caracteres)
# Gerar com: openssl rand -hex 32
ADMIN_KEY=generate_random_hex_64_chars_here

# Tabela IBPT
IBPT_TABELA_VERSAO=2026-04
IBPT_DATA_REFERENCIA=2026-04-01

# Hangfire Dashboard
HANGFIRE_DASHBOARD_USER=admin
HANGFIRE_DASHBOARD_PASSWORD=change_me_strong_password_here
```

---

### 3.5 `infra/postgres/init.sql`

Script de inicialização opcional do PostgreSQL:

```sql
-- Habilitar extensão uuid-ossp se necessário
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Schema para Hangfire (evitar poluir o schema public)
-- Criado automaticamente pelo Hangfire.PostgreSql ao iniciar
-- Documentado aqui para referência
```

---

### 3.6 `.dockerignore`

**Localização:** raiz do repositório (`/.dockerignore`)

```dockerignore
# Build artifacts
**/bin/
**/obj/

# Test projects (não incluir na imagem de produção)
tests/

# Documentação e configs de desenvolvimento
docs/
.claude/
*.md
*.sln

# Git
.git/
.gitignore

# Docker
Dockerfile
infra/

# IDE
.vs/
.vscode/
*.user
```

---

## 4. Fluxos e Diagramas

### 4.1 Build Multi-stage

```
Dockerfile — Stage 1 (SDK)
  ├── COPY csproj files           ← layer cacheável
  ├── dotnet restore              ← layer cacheável
  ├── COPY src/                   ← invalida cache apenas quando código muda
  └── dotnet publish -c Release → /app/publish

Dockerfile — Stage 2 (aspnet)
  ├── apt-get install ca-certificates  ← ICP-Brasil support
  ├── adduser appuser (não-root)
  ├── COPY --from=build /app/publish . ← apenas artefatos
  ├── EXPOSE 8080
  └── ENTRYPOINT dotnet VisuFiscalHub.Api.dll
```

### 4.2 Startup de Serviços

```
docker compose up
  │
  ├── postgres (healthcheck: pg_isready)
  │     └── HEALTHY após ~10s
  │
  ├── redis (healthcheck: redis-cli ping)
  │     └── HEALTHY após ~5s
  │
  └── hub (depends_on postgres healthy)
        ├── Startup .NET — Serilog, DI registration
        ├── MigrateAsync() — aplica migrations pendentes
        ├── RecurringJob setup — reconciliação 5min
        └── Kestrel listening :8080
              └── GET /health → Healthy
```

### 4.3 Rede e Portas

```
Host Machine
  │
  ├── :8080  → hub:8080   (API + Hangfire Dashboard)
  ├── :5432  → postgres:5432
  └── :6379  → redis:6379

hub-network (bridge)
  ├── hub       → ConnectionString: Host=postgres;...
  ├── postgres
  └── redis
```

---

## 5. Considerações sobre Certificados ICP-Brasil

O SEFAZ emite certificados SSL assinados por autoridades certificadoras da ICP-Brasil. A imagem base `mcr.microsoft.com/dotnet/aspnet:10.0` (baseada em Debian) pode não ter os certificados ICP-Brasil no trust store do sistema operacional.

**Diagnóstico:** Se a comunicação mTLS com o SEFAZ falhar com erro de certificado (`CERTIFICATE_VERIFY_FAILED` ou similar), adicionar as ACs ICP-Brasil.

**Procedimento para incluir certificados ICP-Brasil no Dockerfile:**

```dockerfile
# Após: apt-get install ca-certificates

# 1. Copiar arquivos de AC para o container no momento do build
COPY infra/certs/icp-brasil/*.crt /usr/local/share/ca-certificates/icp-brasil/

# 2. Atualizar trust store
RUN update-ca-certificates
```

**Fonte dos certificados:**
- Repositório ITI: `https://www.gov.br/iti/pt-br/assuntos/repositorio/repositorio-de-certificados`
- Certificados necessários: AC Raiz Brasileira v2, AC Raiz Brasileira v5, e as ACs intermediárias que assinam certificados A1 dos tenants

**Alternativa para desenvolvimento:** O sandbox AM aceita qualquer certificado — sem necessidade de ICP-Brasil para testes com `SandboxAM`.

---

## 6. Checklist de Conclusão

- [ ] `Dockerfile` tem exatamente 2 stages: `sdk:10.0` (build) e `aspnet:10.0` (runtime)
- [ ] Stage build usa `dotnet publish -c Release`
- [ ] Stage runtime instala `ca-certificates` via apt-get
- [ ] `EXPOSE 8080` presente no Dockerfile
- [ ] `ENTRYPOINT ["dotnet", "VisuFiscalHub.Api.dll"]` presente
- [ ] Usuário não-root (`appuser`) no stage runtime
- [ ] `docker-compose.yml` tem serviços: hub, postgres, redis
- [ ] `hub` depende de `postgres` com `condition: service_healthy`
- [ ] Variáveis sensíveis via `${VAR_NAME}` — nunca hardcoded
- [ ] `.env.example` documenta todas as variáveis necessárias
- [ ] `.dockerignore` exclui `tests/`, `docs/`, `bin/`, `obj/`
- [ ] `docker compose up` sobe sem erros em ambiente limpo
- [ ] `GET /health` retorna 200 Healthy após startup
- [ ] Hangfire Dashboard acessível em `/hangfire` com credenciais do `.env`
- [ ] `POST /auth/token` com credenciais válidas retorna JWT
