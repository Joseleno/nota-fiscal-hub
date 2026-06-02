# SDLC Agents — Design Spec (Ciclo de Vida Completo)

**Data:** 2026-06-01
**Status:** Aprovado

## Visão geral

Ecossistema de agents e skills cobrindo o ciclo de vida completo de software — do problema de negócio ao deploy. Genérico e reutilizável em qualquer projeto, não acoplado ao time Receba.

```
Fase 1: Descoberta      → problem-analyzer + requirements-extractor
Fase 2: Planejamento    → project-planner + roadmap-builder + risk-assessor
Fase 3: Design Técnico  → solution-architect + api-contract-designer + db-schema-designer
Fase 4: Implementação   → backend-generator + clean-arch-generator + test-strategy
Fase 5: Qualidade       → quality-guardian + security-checklist + test-strategy
Fase 6: Entrega         → delivery-engineer + dockerfile-patterns + cicd-pipeline-builder + go-live-checklist
```

---

## Fase 1 — Descoberta

### Agent `problem-analyzer`

- **Invocação:** `/problem-analyzer` seguido do input (texto livre, documento, e-mail, transcrição)
- **Tools:** `Read`, `Glob`, `Grep`
- **Skills:** `requirements-extractor`

**Modos:**
| Input | Modo |
|---|---|
| Descrição verbal/texto livre | Analista — extrai requisitos, stakeholders, restrições |
| Documento existente (PRD, briefing) | Revisor — valida completude, aponta gaps |
| Ambos | Combinado |

**Fluxo:**
1. Entende o problema de negócio (QUÊ e PORQUÊ, não COMO)
2. Identifica stakeholders e seus objetivos
3. Extrai requisitos funcionais e não-funcionais
4. Mapeia restrições (prazo, budget, tecnologia, regulatório)
5. Identifica riscos de negócio e técnicos
6. Aponta ambiguidades que precisam ser clarificadas

**Entregável:**
```
## Problem Analysis

### Problema de negócio
{descrição em 2-3 linhas — o que dói, para quem, qual o impacto}

### Stakeholders
| Stakeholder | Objetivo | Expectativa |

### Requisitos funcionais
- RF01: {descrição}

### Requisitos não-funcionais
- RNF01: {categoria} — {descrição + critério mensurável}

### Restrições
- {restrição}: {impacto no projeto}

### Riscos identificados
| Risco | Probabilidade | Impacto | Mitigação |

### Ambiguidades — precisam de resposta antes de prosseguir
- [ ] {pergunta específica para stakeholder}
```

### Skill `requirements-extractor`

Checklist de perguntas por domínio e critérios para reconhecer requisito incompleto:
- Perguntas obrigatórias para qualquer domínio (quem usa, com que frequência, qual o critério de sucesso)
- Perguntas por categoria de NFR (performance, segurança, disponibilidade, escalabilidade)
- Sinais de requisito incompleto (sem critério mensurável, ator ambíguo, sem contexto de falha)

---

## Fase 2 — Planejamento

### Agent `project-planner`

- **Invocação:** `/project-planner` seguido do documento de descoberta ou input direto
- **Tools:** `Read`, `Glob`, `Grep`
- **Skills:** `story-template`, `roadmap-builder`, `risk-assessor`

**Fluxo:**
1. Lê documento de descoberta (ou recebe input direto)
2. Quebra requisitos funcionais em histórias no template padrão
3. Agrupa histórias em épicos por área de negócio
4. Prioriza por valor de negócio × risco técnico (matriz 2x2)
5. Propõe roadmap em sprints com sequência lógica de dependências
6. Aponta histórias que precisam de spike técnico

**Matriz de priorização:**
| Quadrante | Valor | Risco | Ordem |
|-----------|-------|-------|-------|
| Quick wins | Alto | Baixo | 1 — implementar primeiro |
| Core risks | Alto | Alto | 2 — spike + implementar cedo |
| Fill | Baixo | Baixo | 3 — preencher sprints |
| Evitar | Baixo | Alto | 4 — questionar necessidade |

**Entregável:**
```
## Project Plan

### Épicos
| # | Épico | Histórias | Prioridade |

### Histórias por épico
[story-template para cada história]

### Matriz de priorização
| História | Valor negócio | Risco técnico | Ordem sugerida |

### Roadmap sugerido
Sprint 1: {histórias} — {objetivo}
Sprint 2: {histórias} — {objetivo}

### Spikes necessários
- [ ] {tema}: {pergunta técnica a responder}

### Dependências entre histórias
- H03 depende de H01 — {motivo}
```

### Skill `roadmap-builder`

Regras para sequenciar histórias:
- Fundações antes de features (auth antes de qualquer endpoint protegido)
- Integrações externas com spike primeiro
- Migrations antes do código que as usa
- Features de valor visível cedo (demonstração para stakeholder)

### Skill `risk-assessor`

Classifica riscos técnicos por categoria e sugere mitigação padrão:
| Categoria | Sinais | Mitigação padrão |
|-----------|--------|-----------------|
| Integração externa | API de terceiro, webhook, fila | Spike + mock + circuit breaker |
| Performance | Volume alto, query complexa | Load test cedo, índices no schema |
| Segurança | Auth, dados sensíveis, PII | Security review na Fase 5 |
| Novidade técnica | Stack nova para o time | Spike de 1-2 dias antes de estimar |

---

## Fase 3 — Design Técnico

### Agent `solution-architect`

- **Invocação:** `/solution-architect` seguido do plano de projeto ou requisitos
- **Tools:** `Read`, `Glob`, `Grep`
- **Skills:** `api-contract-designer`, `db-schema-designer`, `ddd-domain-modeler`, `architecture-review`

**Fluxo:**
1. Analisa requisitos funcionais e não-funcionais
2. Propõe stack tecnológica justificada
3. Define estrutura de projetos e camadas
4. Modela domínio — entidades, agregados, value objects, eventos
5. Desenha contratos de API — endpoints, request/response, autenticação
6. Define schema de banco — tabelas, índices, relacionamentos, migrations
7. Identifica integrações externas e seus contratos
8. Registra decisões como ADRs

**Entregável:**
```
## Solution Architecture

### Stack
| Camada | Tecnologia | Justificativa |

### Estrutura de projetos
{nome}/
  src/
    Domain/
    Application/
    Infrastructure/
    Api/

### Modelo de domínio
Agregados: {lista com responsabilidade}
Value Objects: {lista com regra encapsulada}
Eventos de domínio: {lista com condição de disparo}

### Contratos de API
| Método | Endpoint | Auth | Request | Response |

### Schema de banco
Tabelas: {lista com campos e índices}
Relacionamentos: {lista com tipo e motivo}

### Integrações externas
| Sistema | Contrato | Auth | Fallback |

### ADRs
ADR-001: {título} — Decisão: {o que} — Motivo: {por quê}
```

### Skill `api-contract-designer`

Padrões REST e checklist de segurança:
- Nomenclatura de endpoints (substantivos no plural, hierarquia de recursos)
- Verbos HTTP corretos (POST cria, PUT substitui, PATCH atualiza parcialmente)
- Paginação obrigatória em coleções (cursor ou offset+limit)
- Versionamento (/v1/, header ou query param)
- Códigos de status corretos (201 em criação, 204 em deleção, 422 em validação)
- Checklist: auth em todo endpoint não-público, rate limit, validação de input, CORS

### Skill `db-schema-designer`

Padrões de schema e checklist de performance:
- Nomenclatura (snake_case, tabelas no plural, PKs como `id` UUID)
- Índices obrigatórios em FKs e colunas de busca frequente
- Soft delete (deleted_at) para entidades de negócio críticas
- Timestamps obrigatórios (created_at, updated_at)
- Checklist: sem SELECT *, N+1 identificados, migrations backward compatible, rollback possível

---

## Fase 4 — Implementação

### Agent `backend-generator`

- **Invocação:** `/backend-generator` seguido da arquitetura ou da história a implementar
- **Tools:** `Bash`, `Read`, `Glob`, `Grep`, `Edit`, `Write`
- **Skills:** `clean-arch-generator`, `code-reviewer`, `test-strategy`

**Modos:**
| Input | Modo |
|---|---|
| Arquitetura completa | Completo — implementa todas as camadas em sequência |
| "Implementa só Domain" | Parcial — camada específica |
| Código existente + nova história | Incremental — adiciona feature sem quebrar o existente |

**Fluxo (ordem obrigatória):**
1. Domain — entidades, value objects, eventos, interfaces de repositório
2. Infrastructure — EF Core DbContext, repositórios, migrations
3. Application — commands, queries, handlers, validators (FluentValidation)
4. Api — controllers, middlewares, registro de DI
5. Auto-revisa cada camada com `code-reviewer` antes de avançar
6. Gera testes unitários para handlers e validators

**Entregável:**
```
## Backend Generator Report

### Camadas implementadas
- [x] Domain — {N} entidades, {N} value objects, {N} eventos
- [x] Infrastructure — {N} repositórios, {N} migrations
- [x] Application — {N} commands, {N} queries, {N} handlers
- [x] Api — {N} controllers, {N} endpoints

### Auto-revisão por camada
Domain: Aprovado
Infrastructure: Aprovado — 1 warning: {descrição}
Application: Aprovado
Api: Aprovado

### Requer atenção manual
- [ ] {lógica de negócio complexa que requer revisão humana}
- [ ] {migration — verificar com migration-reviewer antes de rodar}

### Próximos passos
- [ ] dotnet ef database update
- [ ] dotnet test
- [ ] /pr-reviewer antes do merge
```

### Skill `clean-arch-generator`

Templates de código por camada e regras de fronteira:
- Entity: construtor privado, factory method, invariantes no domínio
- Command/Query: record imutável, sem lógica
- Handler: orquestra, não implementa regra de negócio
- Repositório: interface no Domain, implementação na Infrastructure
- Controller: recebe HTTP, envia para MediatR, retorna resultado — sem lógica

### Skill `test-strategy`

Define o que testar por tipo de componente:
| Componente | Tipo de teste | O que mockar | Cobertura mínima |
|------------|--------------|--------------|-----------------|
| Domain entities | Unit | Nada | 100% das invariantes |
| Handlers | Unit | Repositório, serviços externos | Fluxo feliz + erros |
| Controllers | Integration | Banco (usar TestServer) | Endpoints críticos |
| Frontend components | Unit | Serviços HTTP | Renderização + interações |

Nunca mockar: banco em teste de integração, regras de domínio, validações.

---

## Fase 5 — Qualidade

### Agent `quality-guardian`

- **Invocação:** `/quality-guardian` seguido do branch, PR ou arquivos
- **Tools:** `Bash`, `Read`, `Glob`, `Grep`
- **Skills:** `code-reviewer`, `review-angular`, `review-react`, `review-devops`, `security-checklist`, `test-strategy`, `migration-reviewer`, `dependency-audit`

**Modos:**
| Input | Modo |
|---|---|
| Nome de branch | PR — analisa diff completo contra main |
| Arquivos específicos | Spot — analisa apenas os arquivos indicados |
| "auditoria completa" | Full — analisa todo o repositório |

**Fluxo:**
1. Detecta stacks tocadas
2. Aplica review de código por stack
3. Verifica estratégia de testes (o que foi vs o que deveria ser testado)
4. Roda checklist de segurança
5. Verifica migrations se houver
6. Verifica dependências novas se houver
7. Consolida em relatório com decisão GO / NO-GO

**Entregável:**
```
## Quality Report

### Revisão de código
Backend .NET:     {score}/10 — {resumo}
Frontend Angular: {score}/10 — {resumo}
Frontend React:   {score}/10 — {resumo}
DevOps/Infra:    {score}/10 — {resumo}

### Cobertura de testes
| Componente | Testado | Deveria testar | Gap |

### Segurança
- [ ] {item}: OK | FALHOU — {descrição}

### Issues consolidados
CRÍTICO: {N} | WARNING: {N} | SUGESTÃO: {N}

### Decisão
GO ✅ | NO-GO ❌ | CONDICIONAL ⚠️ (aprovado se: {condições})

### Obrigatório antes do merge
- [ ] {ação específica com arquivo:linha}
```

### Skill `security-checklist`

Checklist por categoria:
- **Auth:** JWT validado em todos endpoints protegidos, roles verificadas, refresh token com expiração
- **Injeção:** queries parametrizadas, sem concatenação de SQL, input sanitizado
- **Dados sensíveis:** sem secrets no código, logs sem PII, HTTPS obrigatório
- **Dependências:** CVEs verificados (usa `dependency-audit`)
- **Frontend:** XSS (sem innerHTML dinâmico), CSRF token em formulários

---

## Fase 6 — Entrega

### Agent `delivery-engineer`

- **Invocação:** `/delivery-engineer` seguido da arquitetura ou "revisa" para revisar infra existente
- **Tools:** `Bash`, `Read`, `Glob`, `Grep`, `Edit`, `Write`
- **Skills:** `dockerfile-patterns`, `cicd-pipeline-builder`, `go-live-checklist`, `review-devops`

**Modos:**
| Input | Modo |
|---|---|
| Arquitetura + "gera tudo" | Completo — Dockerfile + compose + pipeline + deploy |
| "Gera só o Dockerfile" | Parcial — artefato específico |
| Infra existente + "revisa" | Revisor — review-devops + aponta problemas |
| "Checklist de go-live" | Go-live — só o checklist |

**Fluxo:**
1. Lê arquitetura e detecta stack
2. Gera Dockerfile multi-stage otimizado
3. Gera docker-compose (dev e staging)
4. Detecta plataforma CI/CD e gera pipeline
5. Gera script de deploy com estratégia adequada
6. Auto-revisa com `review-devops`
7. Entrega checklist de go-live

**Entregável:**
```
## Delivery Engineer Report

### Artefatos gerados
- [x] Dockerfile — multi-stage, base: {imagem}
- [x] docker-compose.yml — serviços: {lista}
- [x] docker-compose.staging.yml
- [x] {plataforma}-pipeline.yml — stages: {lista}
- [x] deploy.sh — estratégia: {rolling | blue-green | simples}

### Auto-revisão (review-devops)
Issues corrigidos: {lista resumida}
Status: Aprovado

### Checklist de go-live
Infraestrutura:
- [ ] Variáveis de ambiente configuradas
- [ ] Health check respondendo
- [ ] Backup de banco configurado
- [ ] Migrations rodadas e validadas

Segurança:
- [ ] TLS configurado
- [ ] Portas desnecessárias fechadas
- [ ] Secrets em vault/env vars
- [ ] Imagem sem CVEs críticos

Monitoramento:
- [ ] Logs estruturados
- [ ] Alertas de erro configurados
- [ ] Métricas de saúde coletadas

Rollback:
- [ ] Procedimento documentado e testado
- [ ] Backup validado antes do deploy
- [ ] Imagem anterior disponível

### Decisão
PRONTO PARA DEPLOY ✅ | BLOQUEADO ❌ — {motivo}
```

### Skill `dockerfile-patterns`

Padrões por stack:
- Multi-stage build obrigatório (build stage + runtime stage)
- Imagem base slim/alpine no runtime
- Usuário não-root obrigatório
- .dockerignore com node_modules, bin, obj, .env
- HEALTHCHECK declarado
- Layers ordenadas do menos ao mais mutável (dependências antes do código)

### Skill `cicd-pipeline-builder`

Padrões de pipeline:
- Stages obrigatórios: lint → test → build → push → deploy
- Cache de dependências (NuGet, npm)
- Deploy condicional (só em main/master ou tag)
- Variáveis de ambiente por stage (dev, staging, prod)
- Rollback automático em falha de health check pós-deploy
- Notificação de resultado (Slack, e-mail)

### Skill `go-live-checklist`

Checklist completo pré-deploy em produção:
- Infraestrutura (env vars, health check, backup, migrations)
- Segurança (TLS, portas, secrets, CVEs)
- Monitoramento (logs, alertas, métricas)
- Rollback (procedimento, backup validado, imagem anterior)
- Comunicação (stakeholders notificados, janela de manutenção agendada)
- Pós-deploy (smoke test, monitoramento por 30min, confirmação com stakeholder)
