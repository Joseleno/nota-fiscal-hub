# Fase 9 — Docker e Deploy: Design

**Data:** 2026-05-24
**Status:** Aprovado
**Ambiente-alvo:** VPS simples (DigitalOcean, Hetzner, etc.)

---

## Contexto

As Fases 1–10 estão implementadas com 572 testes passando. O `Dockerfile`, `docker-compose.yml`, `docker-compose.override.yml` e `infra/.env.example` já existem mas nunca foram validados em execução real. Esta fase valida a stack, adiciona Redis, corrige exposições de segurança para produção e produz scripts de setup/deploy para VPS.

---

## O que já existe (não muda)

| Arquivo | Estado |
|---|---|
| `Dockerfile` | Multi-stage (sdk:10.0 → aspnet:10.0), non-root user, `ca-certificates`, `curl`, healthcheck |
| `infra/docker-compose.yml` | Serviços `hub` + `postgres`, `depends_on: service_healthy`, volumes, rede interna |
| `infra/docker-compose.override.yml` | Overrides de desenvolvimento (ASPNETCORE_ENVIRONMENT=Development, sem auth no dashboard) |
| `infra/.env.example` | Template com todas as variáveis necessárias |

---

## Mudanças

### 1. Redis no docker-compose

**`infra/docker-compose.yml`** — adicionar:

```yaml
redis:
  image: redis:7-alpine
  container_name: visu-fiscal-redis
  volumes:
    - redis_data:/data
  healthcheck:
    test: ["CMD", "redis-cli", "ping"]
    interval: 10s
    timeout: 5s
    retries: 5
  networks:
    - hub-network
  restart: unless-stopped
```

- Sem `ports:` no compose principal — redis acessível apenas pela rede interna em produção.
- `hub` passa a depender também do redis: `depends_on: redis: condition: service_healthy`.
- Adicionar ao ambiente do `hub`: `Redis__ConnectionString: "redis:6379"` — preparado para uso futuro; ignorado se não houver consumer registrado.
- Adicionar `redis_data` à seção `volumes:`.

**`infra/docker-compose.override.yml`** — adicionar serviço redis com porta exposta para dev:

```yaml
redis:
  ports:
    - "6379:6379"
```

---

### 2. Correções de segurança para produção

**Porta do postgres movida para override:**

O `docker-compose.yml` principal expõe `ports: ["${POSTGRES_PORT:-5432}:5432"]` no postgres. Em produção, o banco não deve ser acessível fora da rede interna. Mover essa linha para o `docker-compose.override.yml`.

**`Include Error Detail` configurável:**

A connection string atual tem `Include Error Detail=true` hardcoded — expõe detalhes internos do EF Core em produção. Substituir por variável:

```
ConnectionStrings__DefaultConnection: "...;Include Error Detail=${POSTGRES_INCLUDE_ERROR_DETAIL:-false}"
```

Adicionar `POSTGRES_INCLUDE_ERROR_DETAIL=true` ao `docker-compose.override.yml` para desenvolvimento.

---

### 3. Script de setup inicial da VPS

**`infra/scripts/setup-vps.sh`** — executar uma vez na instalação:

1. Instala Docker + Docker Compose plugin via script oficial (`get.docker.com`)
2. Configura firewall `ufw`: libera 22/tcp, 80/tcp, 443/tcp; habilita ufw
3. Gera par de chaves RSA 2048 para JWT:
   - `openssl genrsa -out /tmp/jwt_private.pem 2048`
   - `openssl rsa -in /tmp/jwt_private.pem -pubout -out /tmp/jwt_public.pem`
   - Converte para formato PEM de linha única (com `\n` literal) para variável de ambiente
4. Gera `CERT_ENCRYPTION_KEY`: `openssl rand -base64 32`
5. Gera `ADMIN_KEY`: `openssl rand -hex 32`
6. Cria `infra/.env` a partir de `.env.example` substituindo os valores gerados
7. Imprime checklist do que ainda precisa ser preenchido manualmente:
   - `POSTGRES_PASSWORD`
   - `HANGFIRE_DASHBOARD_PASSWORD`

Script com `set -euo pipefail`, saídas coloridas (verde = ok, amarelo = atenção, vermelho = erro).

---

### 4. Script de deploy/atualização

**`infra/scripts/deploy.sh`** — executar a cada atualização:

```bash
set -euo pipefail
cd "$(dirname "$0")/.."   # garante execução a partir do diretório infra/
git pull origin main
docker compose pull
docker compose up --build -d
docker compose ps
```

Imprime URL do health check ao final: `curl -s http://localhost:8080/health/ready`.

---

## Critério de Conclusão

- `docker compose -f docker-compose.yml -f docker-compose.override.yml up --build` sobe sem erros
- `GET http://localhost:8080/health` retorna `{"status":"Healthy"}`
- `GET http://localhost:8080/health/live` retorna 200
- `GET http://localhost:8080/health/ready` retorna 200 (postgres saudável)
- Redis acessível em `localhost:6379` no ambiente de desenvolvimento
- Em produção (sem override), porta do postgres não exposta para o host
- `setup-vps.sh` executa sem erros em Ubuntu 22.04/24.04
- `deploy.sh` executa sem erros e mostra status dos containers

---

## Itens fora de escopo

- Nginx / reverse proxy / TLS termination — responsabilidade do operador da VPS (Caddy, Nginx, Traefik)
- CI/CD pipeline — fora do escopo desta fase
- Kubernetes / Helm — ambiente-alvo é VPS simples
- Monitoramento externo (Grafana, Prometheus) — fora do escopo desta fase
