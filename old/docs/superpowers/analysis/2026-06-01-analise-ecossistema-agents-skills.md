# Análise do Ecossistema de Agents/Skills — Lacunas e Roadmap

**Data:** 2026-06-01/02
**Método:** análise multi-agente (workflow com geração paralela + verificação adversarial), cruzando utilização real (commits/revisões do receba-api, transcripts, auditoria do ecossistema) e visão macro de todos os 15 repos do usuário (Receba + CodeProcess SaaS + AGP).

> **Nota metodológica:** em ambas as análises, agentes geradores produziram evidência inflada ou fabricada (hashes/tickets/contagens). A camada de verificação adversarial filtrou isso. **Toda sugestão aqui sobreviveu à verificação contra os arquivos reais.** Itens com evidência inventada foram rejeitados.

---

## Parte 1 — Análise focada (receba-api + transcripts)

### Resumo: 11 lacunas propostas → 8 aprovadas, 3 rejeitadas

**Padrões de uso reais observados:**
- Isolamento multi-tenant é a área de maior atividade e maior risco (override>claim "proteção IDOR", Named Query Filters EF Core 10, bypass EF+Dapper sob RLS, TenantMismatchException forense).
- Commit/branch/PR de altíssima frequência e zero suporte (Conventional Commits, rastreabilidade RD2-XXXX, Bitbucket).
- Retomada de sessão manual e recorrente (~71% dos transcripts abrem com "retomar").
- Refatoração grande sem rede de regressão já custou caro (migração Mapperly + commit de correção de regressões).
- Código "forense por design" em domínios críticos (pagamento multi-banco, jobs Hangfire, telemetria estruturada).

### Os 8 aprovados (Ondas 1-4)

| Onda | Prio | Tipo | Nome | O que faz |
|------|------|------|------|-----------|
| 1 ✅ | Alta | skill | `commit-pr-author` | Convenção real de commit/branch/PR do Receba (Conventional Commits, RD2-XXXX em 3 formatos, Bitbucket, template de corpo de PR) |
| 1 ✅ | Alta | agent | `session-resume` | Reconstrói "onde paramos" (git + planos/specs + MEMORY.md) antes de agir; só-leitura. Command `/resume` |
| 2 | Alta | skill | `multi-tenant-isolation-reviewer` | Checklist de vazamento cross-tenant: precedência override>claim, Named Query Filters cobrindo todas entidades, bypass EF+Dapper sob RLS, exceção forense, thread-safety do estado scoped |
| 2 | Alta | agent | `refactoring-orchestrator` | Refatoração behavior-preserving: caracteriza testes ANTES, fatias pequenas, `dotnet test` como gate entre cada uma |
| 3 | Média | skill | `payment-reconciliation-reviewer` | Review multi-banco: idempotência/dedup de webhook, reconciliação PIX COB/COBV e boleto, registrar inconsistência em vez de auto-fix |
| 3 | Média | skill | `background-job-reviewer` | Review Hangfire: idempotência, retry/dead-letter, scope DI+tenant por lote, paralelismo |
| 4 | Média | skill | `observability-instrumentor` | Instrumentação: Serilog estruturado, CorrelationId sem PII, LoggingBehavior com duração, exclusão de /health dos traces |
| 4 | Média | agent | `incident-responder` | Triagem de incidente: correlaciona por trace/CompanyId, classifica dado-inconsistente vs bug (delega bug-hunter) vs infra |

### Rejeitados (evidência fabricada)
- `archtest-enforcer` — commits/refs inexistentes; já coberto por architecture-review + clean-architecture-validator. *Futuro: merge se adotar NetArchTest.*
- `async-concurrency-auditor` — evidência inventada; zero `.Result/.Wait()` no codebase. Já coberto por code-reviewer + performance-optimizer.
- `mutation-test-hardener` — config Stryker inexistente. *Futuro: merge em test-strategy se adotar Stryker.NET.*

> ⚠️ **Atenção para a Onda 2:** a skill `multi-tenant-isolation-reviewer` DEVE ser escrita a partir do código real (campos scoped mutáveis em `RecebaContext.OverrideCompanyId` + contrato de thread-safety documentado, resolução via `CompanyAliasResolver` Dapper isolado), **NÃO** a moldura `AsyncLocal`/`ITenantScope` que a sugestão original assumiu (não existe no código).

---

## Parte 2 — Análise macro (todos os 15 repos)

### Resumo: 11 lacunas propostas → 4 aprovadas, 7 merges, 0 rejeitados

**Padrões de re-trabalho e divergência na origem (cross-projeto):**
- **SaaS .NET clonado camada-a-camada, divergindo no detalhe.** DbContext diverge: `Ecocarro` usa `AppDbContext`/pasta `Data/` vs `AgendeAqui` `ApplicationDbContext`/`Persistence/`. Audit fields divergem (`DateTime` vs `DateTimeOffset`; `xmin` só em notification-hub).
- **Contrato API↔frontend fora da CI.** `AgendeAqui/frontend` `generate-api` aponta para `localhost`, fora do `ci.yml` → schema deriva em silêncio; `equilibraFin`/`receba-customer-react` digitam tipos à mão apesar de OpenAPI.
- **Dois mundos de CI sem reuso.** CodeProcess GitHub Actions com zero `workflow_call`/composite actions; nota-fiscal-hub/notification-hub com `workflows/` vazio. Receba usa Bitbucket Pipelines.
- **Cold-start e release são terra de ninguém.** Nenhum asset cria repo novo ponta-a-ponta. `receba-api` 45 tags ad-hoc, `AGP/visu` 157 tags com typo `vv2.40.7`, vários repos com 0 tags e sem CHANGELOG.
- **Onboarding inconsistente.** `nota-fiscal-hub/README.md` = 17 bytes vs `Receba/notification-hub` 7176 bytes; `.editorconfig` ausente em ~metade; CLAUDE.md só em 3 de 21 repos.

### Os 4 novos aprovados

| Prio | Tipo | Nome | O que faz | Esforço |
|------|------|------|-----------|---------|
| Média | agent | `saas-bootstrapper` | Entrevista + orquestra clean-arch-generator + db-schema + dockerfile + cicd + go-live → repo novo completo e consistente | G |
| Média | skill | `api-client-codegen` | Gera client TS tipado do OpenAPI (openapi-typescript p/ React, ng-openapi-gen p/ Angular) na CI, fail-on-drift | P |
| Média | skill | `release-manager` | SemVer de Conventional Commits + CHANGELOG + política de tag/breaking-change | M |
| Média | skill | `repo-onboarding-auditor` | Audita repo (README quick-start, `.editorconfig`, estrutura) e reporta gaps; delega CLAUDE.md ao claude-md-improver | P |

> Nenhum é "alta" porque o gatilho é infrequente (bootstrap = poucas vezes/ano; codegen/onboarding = 1× por projeto). São alavancagem one-time por repo, não dor diária.

### Os 7 merges (viram seção/modo de asset existente, não artefato novo)

| Proposta | Funde em |
|----------|----------|
| shared-library-extractor | `refactoring-orchestrator` (modo cross-repo) |
| github-actions-harmonizer | `cicd-pipeline-builder` (seção reusable workflows) |
| frontend-spa-bootstrapper | `frontend-react` (modo Greenfield) |
| ef-context-base-kit | `clean-arch-generator` (seção BaseDbContext) |
| spa-deploy-pipeline | `dockerfile-patterns` + `cicd-pipeline-builder` (premissa falsificada: receba-backoffice já faz env-injection em runtime) |

### Descartado
- `multi-agent-conductor` — duplica superpowers:subagent-driven-development + dispatching-parallel-agents + executing-plans + session-resume; primitivos já no harness. Evidência ~6x inflada.

---

## Roadmap consolidado (Ondas 1-7)

As 8 da Parte 1 cobrem o **loop diário**; as 4 da Parte 2 fecham as **bordas do SDLC** (entrada, contrato, saída).

- **✅ Onda 1** (implementada, commit 036c6ed): commit-pr-author, session-resume, /resume
- **⏸️ Onda 2** (segurança/corretude): multi-tenant-isolation-reviewer, refactoring-orchestrator
- **Onda 3** (review de domínio): payment-reconciliation-reviewer, background-job-reviewer
- **Onda 4** (operação pós-deploy): observability-instrumentor, incident-responder
- **Onda 5** (bordas — entrada+contrato): `repo-onboarding-auditor` (P) + `api-client-codegen` (P) — quick wins sem pré-requisitos
- **Onda 6** (saída + os 5 merges): `release-manager` (M, depende da Onda 1) + incrementos nos assets
- **Onda 7** (capstone): `saas-bootstrapper` (G) — por último, depois que os assets que ele orquestra amadurecerem

**Pré-requisito bloqueante da Onda 7:** decidir uma **convenção canônica** (nome do DbContext + `Persistence/` vs `Data/`) — os próprios repos discordam.

### Higiene de catálogo recomendada (auditoria)
- Remover `qa-homologacao`/`marketing-estrategia` (EcoCarro, não Receba) do catálogo.
- Aposentar `review-frontend` (órfã, superseded por review-react).
- Revisar overlap `dotnet-architect` × `solution-architect`+`backend-generator`.
