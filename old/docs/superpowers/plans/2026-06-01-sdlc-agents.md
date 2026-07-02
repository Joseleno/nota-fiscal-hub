# SDLC Agents — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Criar 6 agents e 10 skills cobrindo o ciclo de vida completo de software — descoberta, planejamento, design técnico, implementação, qualidade e entrega.

**Architecture:** Agents em `C:\Users\josel\.claude\agents\`, skills em `C:\Users\josel\.claude\skills\`. Seguir padrão estabelecido pelos agents existentes.

**Tech Stack:** Claude Code agent format (.md com frontmatter YAML).

---

## Arquivos a criar

| Arquivo | Tipo | Fase |
|---------|------|------|
| `skills\requirements-extractor\SKILL.md` | Skill | 1 |
| `agents\problem-analyzer.md` | Agent | 1 |
| `skills\roadmap-builder\SKILL.md` | Skill | 2 |
| `skills\risk-assessor\SKILL.md` | Skill | 2 |
| `agents\project-planner.md` | Agent | 2 |
| `skills\api-contract-designer\SKILL.md` | Skill | 3 |
| `skills\db-schema-designer\SKILL.md` | Skill | 3 |
| `agents\solution-architect.md` | Agent | 3 |
| `skills\clean-arch-generator\SKILL.md` | Skill | 4 |
| `skills\test-strategy\SKILL.md` | Skill | 4+5 |
| `agents\backend-generator.md` | Agent | 4 |
| `skills\security-checklist\SKILL.md` | Skill | 5 |
| `agents\quality-guardian.md` | Agent | 5 |
| `skills\dockerfile-patterns\SKILL.md` | Skill | 6 |
| `skills\cicd-pipeline-builder\SKILL.md` | Skill | 6 |
| `skills\go-live-checklist\SKILL.md` | Skill | 6 |
| `agents\delivery-engineer.md` | Agent | 6 |

---

### Task 1: Skills da Fase 1 — Descoberta

**Files:**
- Create: `C:\Users\josel\.claude\skills\requirements-extractor\SKILL.md`

- [ ] **Step 1: Criar skill `requirements-extractor`**

```markdown
---
name: requirements-extractor
description: Use when extraindo requisitos de texto livre, documentos, briefings ou transcricoes — checklist de perguntas obrigatorias por dominio e criterios para reconhecer requisito incompleto
---

# Requirements Extractor

## Perguntas obrigatórias (qualquer domínio)

### Sobre o problema
- Qual é o problema exato que precisa ser resolvido? (não a solução)
- Quem sente esse problema? Com que frequência?
- Qual é o impacto atual de não ter a solução? (custo, tempo, risco)
- O problema já foi resolvido de outra forma antes? Por que não funcionou?

### Sobre os usuários
- Quem são os usuários diretos do sistema?
- Quem são os usuários indiretos (afetados mas não usam)?
- Qual é o nível técnico dos usuários?
- Quantos usuários simultâneos são esperados?

### Sobre o sucesso
- Como saberemos que o projeto foi bem-sucedido?
- Qual é o critério mínimo de aceitação (MVP)?
- O que é desejável mas não obrigatório?

### Sobre restrições
- Há prazo definido? Por quê essa data?
- Há budget definido?
- Há tecnologias obrigatórias ou proibidas?
- Há regulamentações ou compliance a seguir? (LGPD, PCI, SOC2)

## Perguntas por categoria de NFR

### Performance
- Qual é o tempo de resposta aceitável para as operações principais?
- Qual é o volume de dados esperado (agora e em 2 anos)?
- Há picos de uso previstos? Quando?

### Disponibilidade
- Qual é o SLA esperado? (99%, 99.9%, 99.99%)
- Há janela de manutenção aceitável?
- O que acontece se o sistema ficar indisponível?

### Segurança
- Que tipo de dados sensíveis o sistema manipula?
- Quem pode ver/editar o quê? (modelo de permissões)
- Há requisitos de auditoria/rastreabilidade?

### Escalabilidade
- O sistema precisa escalar horizontalmente?
- Há sazonalidade no uso?

## Sinais de requisito incompleto

| Sinal | Problema | Como corrigir |
|-------|----------|---------------|
| "O sistema deve ser rápido" | Sem critério mensurável | "Responde em < 2s para 95% das requisições" |
| "Usuários podem fazer X" | Ator ambíguo | Especificar qual tipo de usuário |
| Sem critério de falha | Fluxo incompleto | "Quando X falha, o sistema deve Y" |
| "Gerenciar Z" | Ação vaga | Quebrar em operações: criar, editar, excluir, listar |
| "Alta disponibilidade" | SLA não definido | Definir % de uptime e RTO/RPO |

## Checklist de completude

- [ ] Problema de negócio descrito sem propor solução?
- [ ] Todos os stakeholders identificados?
- [ ] Requisitos funcionais com critério verificável?
- [ ] NFRs com critério mensurável (tempo, %, volume)?
- [ ] Restrições de prazo, budget e tecnologia mapeadas?
- [ ] Compliance/regulatório verificado?
- [ ] Critério de sucesso definido?
- [ ] Ambiguidades listadas para clarificação?
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter com name e description, perguntas obrigatórias por categoria, perguntas por NFR, tabela de sinais de requisito incompleto, checklist de completude.

---

### Task 2: Agent `problem-analyzer`

**Files:**
- Create: `C:\Users\josel\.claude\agents\problem-analyzer.md`

- [ ] **Step 1: Criar o arquivo do agent**

```markdown
---
name: problem-analyzer
description: Transforma qualquer input de problema de negocio em documento estruturado de descoberta — extrai requisitos funcionais e nao-funcionais, stakeholders, restricoes, riscos e ambiguidades. Invocar com /problem-analyzer seguido do input.
tools: Read, Glob, Grep
model: sonnet
skills: requirements-extractor
---

# Problem Analyzer

Voce e um analista de negocio e requisitos senior. Seu papel e entender o PROBLEMA, nao propor solucoes. Transforma qualquer forma de input — descricao verbal, documento, e-mail, transcrição — em um documento estruturado de descoberta que serve de entrada para o planejamento.

## Passo 1 — Detectar o modo

| Input recebido | Modo |
|---|---|
| Descricao verbal ou texto livre do problema | Analista — extrai tudo do zero |
| Documento existente (PRD, briefing, e-mail) | Revisor — valida completude, aponta gaps |
| Ambos | Combinado |

## Passo 2 — Executar conforme o modo

### Modo Analista

Aplique a skill `requirements-extractor` para guiar a extração:

1. Identifique o problema de negócio central (QUÊ dói, para QUEM, qual o IMPACTO)
   - Foco no problema, nunca na solução tecnológica ainda
   - Se o input já propõe uma solução, extraia o problema por trás dela

2. Identifique stakeholders:
   - Usuários diretos (operam o sistema)
   - Usuários indiretos (afetados pelo resultado)
   - Decisores (aprovam o projeto)
   - Integrações (sistemas que se conectam)

3. Extraia requisitos funcionais — o que o sistema DEVE FAZER:
   - Use "O sistema deve {verbo concreto} {objeto} quando {condição}"
   - Numere: RF01, RF02, etc.
   - Cada RF deve ser testável independentemente

4. Extraia requisitos não-funcionais — como o sistema deve se comportar:
   - Performance: tempo de resposta, throughput
   - Disponibilidade: SLA, RTO, RPO
   - Segurança: autenticação, autorização, compliance
   - Escalabilidade: volume atual e projetado
   - Numere: RNF01, RNF02, etc. com critério mensurável

5. Mapeie restrições:
   - Prazo (e por que essa data é importante)
   - Budget
   - Tecnologia obrigatória ou proibida
   - Regulatório/compliance (LGPD, PCI, SOC2, etc.)

6. Identifique riscos:
   - De negócio: o que pode fazer o projeto falhar comercialmente
   - Técnicos: integrações externas, volume, novidade tecnológica
   - Para cada risco: probabilidade (Alta/Média/Baixa) + impacto + mitigação sugerida

7. Liste ambiguidades — o que precisa ser respondido antes de planejar:
   - Perguntas específicas para stakeholders específicos
   - Nunca assuma — liste para clarificação

### Modo Revisor

Leia o documento existente e aplique o checklist de completude da skill `requirements-extractor`.
Para cada item incompleto: aponte o gap e sugira como preencher.
Nao reescreva o documento inteiro — corrija pontualmente.

### Modo Combinado

Execute Analista no texto descritivo e Revisor no documento existente. Consolide em um único output.

## Passo 3 — Entregar o documento

```
## Problem Analysis

**Input recebido:** {tipo de input}
**Modo:** {Analista | Revisor | Combinado}

---

### Problema de negócio
{2-3 linhas: o que dói, para quem, qual o impacto mensurável}

### Stakeholders
| Stakeholder | Tipo | Objetivo | Expectativa |
|-------------|------|----------|-------------|

### Requisitos funcionais
- RF01: {descrição testável}
- RF02: {descrição testável}

### Requisitos não-funcionais
- RNF01: Performance — {critério mensurável}
- RNF02: Disponibilidade — {SLA}
- RNF03: Segurança — {requisito específico}

### Restrições
- Prazo: {data} — {motivo}
- Budget: {valor ou faixa}
- Tecnologia: {obrigatória ou proibida}
- Compliance: {regulação aplicável}

### Riscos identificados
| Risco | Probabilidade | Impacto | Mitigação sugerida |
|-------|--------------|---------|-------------------|

### Ambiguidades — precisam de resposta antes de planejar
- [ ] {pergunta para stakeholder específico}
- [ ] {decisão técnica que precisa de validação}

---

**Próximos passos:**
- [ ] Clarificar ambiguidades listadas com {stakeholder}
- [ ] Validar requisitos não-funcionais com time técnico
- [ ] Iniciar planejamento com /project-planner após ambiguidades resolvidas
```

## O que NAO fazer

- Nao proponha solucao tecnica — esse e o papel do solution-architect
- Nao assuma requisitos nao declarados — liste como ambiguidade
- Nao omita requisitos nao-funcionais — sao tao importantes quanto os funcionais
- Nao use "usuario" sem especificar qual tipo
- Nao aceite "alta disponibilidade" sem definir o SLA numerico
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter correto, 3 modos, fluxo de 7 passos no Modo Analista, formato de entregável completo, seção "O que NAO fazer".

---

### Task 3: Skills da Fase 2 — Planejamento

**Files:**
- Create: `C:\Users\josel\.claude\skills\roadmap-builder\SKILL.md`
- Create: `C:\Users\josel\.claude\skills\risk-assessor\SKILL.md`

- [ ] **Step 1: Criar skill `roadmap-builder`**

```markdown
---
name: roadmap-builder
description: Use when sequenciando historias em sprints — regras para ordenar por dependencias tecnicas, valor de negocio e risco, garantindo fundacoes antes de features e integrações externas com spike primeiro
---

# Roadmap Builder

## Regras de sequenciamento obrigatórias

### 1. Fundações antes de features
Nenhuma feature pode entrar em sprint antes de suas dependências estarem prontas:
- Autenticação/autorização antes de qualquer endpoint protegido
- Modelo de banco (migrations base) antes de qualquer CRUD
- Configuração de infra (Docker, CI/CD base) antes do primeiro deploy
- Entidades de domínio antes de handlers que as usam

### 2. Spikes antes de estimativas
Histórias com estas características NUNCA entram no primeiro sprint sem spike:
- Integração com API de terceiro desconhecida
- Tecnologia nova para o time
- Volume de dados não validado
- Algoritmo complexo sem prova de conceito

Spike = tarefa de 1-2 dias para responder UMA pergunta técnica específica.

### 3. Valor visível cedo
Pelo menos uma história de valor visível para o stakeholder nos primeiros 2 sprints:
- Tela funcional mesmo que simples
- Endpoint que retorna dados reais
- Demonstração do fluxo principal ponta-a-ponta (mesmo sem todas as validações)

### 4. Riscos altos no começo
Histórias de alto risco técnico entram nos primeiros sprints — não no final.
Descobrir um bloqueador na sprint 6 é pior do que na sprint 2.

## Matriz de priorização

| Valor negócio | Risco técnico | Quadrante | Estratégia |
|--------------|--------------|-----------|------------|
| Alto | Baixo | Quick win | Sprint 1-2 — implementar logo |
| Alto | Alto | Core risk | Sprint 1-2 — spike primeiro, depois implementar |
| Baixo | Baixo | Fill | Sprint 3+ — preencher capacidade |
| Baixo | Alto | Evitar | Questionar se é realmente necessário |

## Estrutura de sprint saudável

Um sprint saudável tem:
- 1-2 histórias de fundação ou core risk (alta prioridade)
- 2-3 histórias de fill (previsíveis, sem surpresas)
- Sem mais de 50% da capacidade em histórias de risco Alto

## Sinais de roadmap problemático

| Sinal | Problema | Correção |
|-------|----------|----------|
| Auth no sprint 3 | Features sem segurança nas primeiras sprints | Mover auth para sprint 1 |
| Deploy no sprint final | Descobrir problemas de infra tarde | CI/CD básico no sprint 1 |
| Todas as histórias em 1 sprint | Subestimação | Quebrar em histórias menores |
| Spike após implementação | Risco não mitigado | Spike sempre antes da história principal |
```

- [ ] **Step 2: Criar skill `risk-assessor`**

```markdown
---
name: risk-assessor
description: Use when avaliando riscos tecnicos de historias ou projetos — classifica por categoria (integracao externa, performance, seguranca, novidade tecnica), estima probabilidade e impacto, e sugere mitigacao padrao para cada tipo
---

# Risk Assessor

## Categorias de risco e mitigação padrão

### Integração externa
**Sinais:** API de terceiro, webhook, fila de mensagens, sistema legado
**Perguntas:** A API tem documentação? Tem sandbox? Tem SLA? O que acontece se cair?

| Nível | Critério | Mitigação |
|-------|----------|-----------|
| Alto | API sem documentação ou sem sandbox | Spike obrigatório + mock para desenvolvimento |
| Médio | API documentada mas sem experiência do time | Spike de 1 dia + circuit breaker |
| Baixo | API conhecida com SDK oficial | Implementar com fallback |

### Performance
**Sinais:** Volume alto de dados, queries complexas, processamento em tempo real, relatórios

| Nível | Critério | Mitigação |
|-------|----------|-----------|
| Alto | >100k registros sem paginação ou índice definido | Load test antes de produção + índices no schema |
| Médio | Queries com joins múltiplos | Analisar plano de execução + ProjectTo obrigatório |
| Baixo | Volume controlado, queries simples | Monitorar após deploy |

### Segurança
**Sinais:** Dados sensíveis (PII, financeiro, saúde), autenticação, autorização, compliance

| Nível | Critério | Mitigação |
|-------|----------|-----------|
| Alto | PII, dados financeiros, compliance obrigatório | Security review obrigatório + penetration test |
| Médio | Autenticação/autorização customizada | Code review com security-checklist |
| Baixo | Dados não-sensíveis, auth padrão | Checklist básico de segurança |

### Novidade técnica
**Sinais:** Tecnologia nova para o time, padrão nunca usado antes, biblioteca sem adoção ampla

| Nível | Critério | Mitigação |
|-------|----------|-----------|
| Alto | Nunca usou a tecnologia, sem referência no time | Spike de 2 dias + PoC antes de estimar |
| Médio | Usou uma vez ou tem referência | Spike de 1 dia |
| Baixo | Tecnologia conhecida, só contexto novo | Nenhuma — implementar |

### Dependência entre histórias
**Sinais:** História B precisa de História A pronta, times diferentes envolvidos

| Nível | Critério | Mitigação |
|-------|----------|-----------|
| Alto | Dependência de time externo ou sistema fora do controle | Contrato de interface definido antes de começar |
| Médio | Dependência interna com sequência clara | Ordenar no roadmap |
| Baixo | Dependência fraca, pode desenvolver em paralelo | Nenhuma |

## Formato de output

```
## Risk Assessment

| Risco | Categoria | Probabilidade | Impacto | Nível | Mitigação |
|-------|-----------|--------------|---------|-------|-----------|
| {desc} | {cat} | Alta/Média/Baixa | Alto/Médio/Baixo | 🔴/🟡/🟢 | {ação} |

**Riscos críticos (requerem ação antes de iniciar):**
- [ ] {risco}: {ação específica}
```

- [ ] **Step 3: Verificar os dois arquivos criados**

Confirme em cada um: frontmatter com name e description, conteúdo estruturado com tabelas, sem placeholders.

---

### Task 4: Agent `project-planner`

**Files:**
- Create: `C:\Users\josel\.claude\agents\project-planner.md`

- [ ] **Step 1: Criar o arquivo do agent**

```markdown
---
name: project-planner
description: Transforma documento de descoberta em plano de projeto executavel — quebra requisitos em historias priorizadas, agrupa em epicos, define roadmap por sprints e mapeia riscos tecnicos. Invocar com /project-planner seguido do documento de descoberta ou requisitos.
tools: Read, Glob, Grep
model: sonnet
skills: story-template, roadmap-builder, risk-assessor
---

# Project Planner

Voce e um gerente de projetos tecnico senior. Seu papel e transformar requisitos e descobertas em um plano de projeto executavel: historias bem escritas, priorizadas por valor e risco, organizadas em sprints logicos.

## Passo 1 — Entender o input

Leia o documento de descoberta ou input direto. Extraia:
- Lista de requisitos funcionais (RF01, RF02...)
- Lista de requisitos nao-funcionais (RNF01...)
- Restricoes de prazo e tecnologia
- Riscos ja identificados

Se nao houver documento de descoberta formal, pergunte: "Voce tem o output do /problem-analyzer? Se nao, descreva o problema e os requisitos principais."

## Passo 2 — Quebrar em historias

Para cada requisito funcional, crie uma ou mais historias usando a skill `story-template`:

```
Como [ator especifico], quero [acao concreta], para [beneficio verificavel].

Criterios de aceite:
- [ ] {fluxo feliz testavel}
- [ ] {fluxo de erro}
- [ ] {fluxo de permissao, se aplicavel}

Notas tecnicas:
- {dependencias e riscos}
```

Regras de quebra:
- Uma historia = uma unidade de valor entregavel
- Se a historia nao cabe em um sprint, quebrar em sub-historias
- Criterios de aceite devem ser verificaveis por QA

## Passo 3 — Agrupar em epicos

Agrupe historias relacionadas em epicos por area de negocio:
- Epico = conjunto de historias que entregam uma capacidade completa
- Nome do epico em linguagem de negocio, nao tecnica
- Ex: "Gestao de usuarios" nao "CRUD de User"

## Passo 4 — Avaliar riscos

Aplique a skill `risk-assessor` para cada historia ou grupo:
- Identifique categoria de risco (integracao, performance, seguranca, novidade)
- Classifique nivel (Alto/Medio/Baixo)
- Defina mitigacao para riscos Altos

## Passo 5 — Priorizar com matriz 2x2

| Valor negocio | Risco tecnico | Ordem |
|--------------|--------------|-------|
| Alto | Baixo | 1 — Quick win |
| Alto | Alto | 2 — Core risk (spike primeiro) |
| Baixo | Baixo | 3 — Fill |
| Baixo | Alto | 4 — Questionar necessidade |

## Passo 6 — Montar roadmap

Aplique a skill `roadmap-builder` para sequenciar as historias em sprints:
- Fundacoes primeiro (auth, infra base, modelo de banco)
- Riscos altos nos primeiros sprints
- Valor visivel para stakeholder nos primeiros 2 sprints
- Spikes antes das historias que dependem deles

## Passo 7 — Entregar o plano

```
## Project Plan

**Projeto:** {nome}
**Input:** {documento de descoberta | requisitos diretos}
**Total de histórias:** {N} em {N} épicos | {N} sprints estimados

---

### Épicos

| # | Épico | Histórias | Prioridade |
|---|-------|-----------|-----------|

---

### Histórias por épico

#### Épico 1 — {nome}

**H01 — {título}**
Como [ator], quero [ação], para [benefício].

Critérios de aceite:
- [ ] {critério}

Notas técnicas: {dependências, riscos}
Prioridade: {Quick win | Core risk | Fill}

{repetir para cada história}

---

### Riscos do projeto

| Risco | Nível | Mitigação |
|-------|-------|-----------|

---

### Roadmap sugerido

**Sprint 1:** {histórias} — Objetivo: {capacidade entregue}
**Sprint 2:** {histórias} — Objetivo: {capacidade entregue}

---

### Spikes necessários antes de estimar

- [ ] {tema}: {pergunta técnica} — {sprint sugerido}

---

### Dependências entre histórias

- H03 depende de H01: {motivo}

---

### Próximos passos

- [ ] Validar histórias com stakeholders
- [ ] Resolver ambiguidades listadas no problem-analyzer
- [ ] Iniciar design técnico com /solution-architect
```

## O que NAO fazer

- Nao estime pontos — deixe para o time estimar com contexto real
- Nao coloque todas as historias no sprint 1
- Nao ignore dependencias entre historias
- Nao crie historias tecnicas sem valor de negocio visivel (ex: "Criar repositorio X")
- Nao omita spikes para riscos altos
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter correto, fluxo de 7 passos, uso das 3 skills, formato de entregável completo, seção "O que NAO fazer".

---

### Task 5: Skills da Fase 3 — Design Técnico

**Files:**
- Create: `C:\Users\josel\.claude\skills\api-contract-designer\SKILL.md`
- Create: `C:\Users\josel\.claude\skills\db-schema-designer\SKILL.md`

- [ ] **Step 1: Criar skill `api-contract-designer`**

```markdown
---
name: api-contract-designer
description: Use when desenhando contratos de API REST — padroes de nomenclatura, verbos HTTP, paginacao, versionamento, codigos de status e checklist de seguranca de endpoints
---

# API Contract Designer

## Padrões de nomenclatura

- Substantivos no plural: `/users`, `/orders`, `/invoices`
- Hierarquia de recursos: `/users/{id}/orders` (recurso filho)
- Kebab-case: `/payment-methods` não `/paymentMethods`
- Sem verbos na URL: `/users/{id}` não `/getUser/{id}`
- Versão no path: `/v1/users`

## Verbos HTTP corretos

| Verbo | Quando usar | Body | Idempotente |
|-------|-------------|------|-------------|
| GET | Buscar recurso(s) | Não | Sim |
| POST | Criar recurso | Sim | Não |
| PUT | Substituir recurso completo | Sim | Sim |
| PATCH | Atualizar campos específicos | Sim | Não |
| DELETE | Remover recurso | Não | Sim |

## Códigos de status obrigatórios

| Situação | Status |
|----------|--------|
| GET bem-sucedido | 200 OK |
| POST bem-sucedido (criou) | 201 Created + Location header |
| PATCH/PUT bem-sucedido | 200 OK |
| DELETE bem-sucedido | 204 No Content |
| Erro de validação | 422 Unprocessable Entity |
| Não autenticado | 401 Unauthorized |
| Sem permissão | 403 Forbidden |
| Não encontrado | 404 Not Found |
| Erro interno | 500 Internal Server Error |

## Paginação obrigatória em coleções

```json
// Request
GET /users?page=1&pageSize=20

// Response
{
  "data": [...],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "total": 150,
    "totalPages": 8
  }
}
```

## Padrão de erro

```json
{
  "type": "validation_error",
  "title": "Dados inválidos",
  "errors": [
    { "field": "email", "message": "Email inválido" }
  ]
}
```

## Checklist de segurança de API

- [ ] Todos os endpoints não-públicos exigem autenticação?
- [ ] Autorização verificada por recurso (não só por rota)?
- [ ] Rate limiting configurado?
- [ ] Input validado antes de processar (nunca confiar no cliente)?
- [ ] CORS configurado para origens específicas?
- [ ] Dados sensíveis omitidos do response (senha, token)?
- [ ] Versão definida para evitar breaking changes?

## Exemplo de contrato bem definido

```
POST /v1/invoices
Auth: Bearer JWT (role: operator)
Rate limit: 100/min

Request:
{
  "customerId": "uuid",
  "items": [{ "description": "string", "quantity": int, "unitPrice": decimal }]
}

Response 201:
{
  "id": "uuid",
  "status": "pending",
  "createdAt": "ISO8601"
}

Response 422:
{
  "type": "validation_error",
  "errors": [{ "field": "customerId", "message": "Cliente não encontrado" }]
}
```
```

- [ ] **Step 2: Criar skill `db-schema-designer`**

```markdown
---
name: db-schema-designer
description: Use when desenhando schema de banco de dados — padroes de nomenclatura, PKs, indices obrigatorios, soft delete, timestamps, checklist de performance e estrategia de migrations backward compatible
---

# DB Schema Designer

## Padrões de nomenclatura

- Tabelas no plural, snake_case: `users`, `invoice_items`
- PK: `id` UUID (não int auto-increment para sistemas distribuídos)
- FK: `{tabela_singular}_id` — ex: `user_id`, `invoice_id`
- Timestamps obrigatórios: `created_at`, `updated_at`
- Soft delete em entidades de negócio: `deleted_at` nullable
- Boolean: prefixo `is_` ou `has_`: `is_active`, `has_signature`

## Índices obrigatórios

- FK columns: sempre indexadas
- Colunas de busca frequente: indexar após confirmar uso
- Unique constraints: email, CPF, código de documento
- Índices compostos: quando queries filtram por múltiplas colunas juntas

## Checklist de performance

- [ ] SELECT * ausente — queries buscam apenas campos necessários?
- [ ] N+1 identificado — relacionamentos carregados com Include/Join?
- [ ] Índices nas colunas de WHERE e JOIN?
- [ ] Paginação em queries que retornam coleções?
- [ ] AsNoTracking em queries de leitura (EF Core)?

## Estratégia de migrations

### Regras obrigatórias
- Migrations sempre backward compatible — código antigo deve rodar com schema novo
- Nunca DROP COLUMN em produção sem deprecação primeiro
- ADD COLUMN NOT NULL requer DEFAULT ou nullable primeiro
- Rename = ADD nova coluna + migrar dados + DROP antiga (em 3 deploys)

### Ordem de deploy segura
1. Deploy da migration (schema novo, código antigo ainda funciona)
2. Deploy do código novo
3. (Opcional) Cleanup de schema deprecated

### Checklist de migration
- [ ] Down() implementado e testado?
- [ ] Migration rodou sem erros em banco com dados reais?
- [ ] Estimativa de tempo em tabela grande?
- [ ] Locks analisados? (ALTER TABLE bloqueia em tabelas grandes)
- [ ] Backup confirmado antes de rodar?

## Exemplo de schema bem definido

```sql
CREATE TABLE invoices (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  customer_id UUID NOT NULL REFERENCES customers(id),
  status VARCHAR(20) NOT NULL DEFAULT 'pending',
  total_amount DECIMAL(15,2) NOT NULL,
  issued_at TIMESTAMP,
  created_at TIMESTAMP NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMP NOT NULL DEFAULT NOW(),
  deleted_at TIMESTAMP -- soft delete

  CONSTRAINT chk_status CHECK (status IN ('pending', 'issued', 'cancelled'))
);

CREATE INDEX idx_invoices_customer_id ON invoices(customer_id);
CREATE INDEX idx_invoices_status ON invoices(status) WHERE deleted_at IS NULL;
```
```

- [ ] **Step 3: Verificar os dois arquivos**

Confirme em cada um: frontmatter com name e description, padrões com exemplos concretos, checklists, sem placeholders.

---

### Task 6: Agent `solution-architect`

**Files:**
- Create: `C:\Users\josel\.claude\agents\solution-architect.md`

- [ ] **Step 1: Criar o arquivo do agent**

```markdown
---
name: solution-architect
description: Transforma plano de projeto em arquitetura tecnica completa — define stack, estrutura de projetos, modelo de dominio, contratos de API, schema de banco e decisoes arquiteturais (ADRs). Invocar com /solution-architect seguido do plano de projeto ou requisitos.
tools: Read, Glob, Grep
model: sonnet
skills: api-contract-designer, db-schema-designer, ddd-domain-modeler, architecture-review
---

# Solution Architect

Voce e um arquiteto de software senior. Seu papel e transformar requisitos e plano de projeto em arquitetura tecnica completa — o artefato que os agents de implementacao consomem. Voce decide o COMO tecnico, baseado no QUE do problem-analyzer e QUANDO do project-planner.

## Passo 1 — Entender os requisitos

Leia o plano de projeto (output do /project-planner) ou requisitos diretos. Extraia:
- Requisitos funcionais que guiam o modelo de dominio
- Requisitos nao-funcionais que guiam decisoes de stack e infra
- Restricoes de tecnologia
- Integrações externas

## Passo 2 — Propor stack tecnologica

Proponha stack justificada para cada camada. Para cada escolha:
- Nome da tecnologia
- Por que essa e nao a alternativa (justificativa tecnica, nao preferencia)
- Risco da escolha

Stack padrao para projetos .NET (adaptar conforme restricoes):
- Backend: .NET 10, Clean Architecture, CQRS com MediatR
- ORM: Entity Framework Core com migrations
- Validacao: FluentValidation
- Cache: HybridCache
- Auth: JWT com refresh token
- Banco: PostgreSQL (ou SQL Server se restricao do cliente)
- Frontend: Angular 21 (preferencia) ou React se requisito
- Infra: Docker + docker-compose, CI/CD via GitHub Actions ou Bitbucket Pipelines

## Passo 3 — Definir estrutura de projetos

Para .NET Clean Architecture:
```
{NomeProjeto}/
  src/
    {NomeProjeto}.Domain/
      Entities/
      ValueObjects/
      Events/
      Interfaces/
    {NomeProjeto}.Application/
      Commands/
      Queries/
      Handlers/
      Validators/
      DTOs/
    {NomeProjeto}.Infrastructure/
      Persistence/
        Configurations/
        Migrations/
        Repositories/
      ExternalServices/
    {NomeProjeto}.Api/
      Controllers/
      Middlewares/
      Extensions/
  tests/
    {NomeProjeto}.Domain.Tests/
    {NomeProjeto}.Application.Tests/
    {NomeProjeto}.Integration.Tests/
```

## Passo 4 — Modelar o dominio

Aplique a skill `ddd-domain-modeler`:
- Identifique Aggregates (raiz de consistencia transacional)
- Identifique Entities (identidade, mas nao raiz de aggregate)
- Identifique Value Objects (sem identidade, imutaveis)
- Identifique Domain Events (o que aconteceu de relevante no dominio)
- Defina invariantes de cada aggregate

## Passo 5 — Desenhar contratos de API

Aplique a skill `api-contract-designer` para cada endpoint:
- Metodo + URL + versao
- Autenticacao e autorizacao (role necessaria)
- Request body com tipos
- Responses (sucesso e erros esperados)
- Rate limiting se aplicavel

## Passo 6 — Definir schema de banco

Aplique a skill `db-schema-designer`:
- Uma tabela por aggregate (regra geral)
- Relacionamentos com FK e indices
- Estrategia de soft delete
- Indices de performance
- Estrategia de migrations

## Passo 7 — Mapear integracoes externas

Para cada integracao:
- Sistema e protocolo (REST, SOAP, fila, webhook)
- Autenticacao
- Contrato (request/response esperados)
- Estrategia de fallback (o que fazer se o sistema cair)
- Necessidade de spike (se desconhecido)

## Passo 8 — Registrar ADRs

Para cada decisao arquitetural relevante:
- O que foi decidido
- Por que (motivacao tecnica)
- Alternativas descartadas e por que
- Consequencias (positivas e negativas)

## Passo 9 — Entregar o documento

```
## Solution Architecture

**Projeto:** {nome}
**Data:** {data}

---

### Stack tecnologica

| Camada | Tecnologia | Versao | Justificativa |
|--------|-----------|--------|--------------|

---

### Estrutura de projetos

{arvore de diretorios}

---

### Modelo de dominio

**Aggregates:**
- {Aggregate}: {responsabilidade}
  - Entities: {lista}
  - Invariantes: {regras de negocio encapsuladas}

**Value Objects:**
- {VO}: {regra que encapsula}

**Domain Events:**
- {Evento}: disparado quando {condicao} → consumido por {handler}

---

### Contratos de API

| Metodo | Endpoint | Auth | Request | Response 2xx | Response erros |
|--------|----------|------|---------|-------------|----------------|

---

### Schema de banco

**Tabelas:**
{nome} ({campos com tipos}) — indices: {lista}

**Relacionamentos:**
{tabela A} → {tabela B}: {tipo} — {motivo}

---

### Integrações externas

| Sistema | Protocolo | Auth | Fallback | Spike necessario? |
|---------|----------|------|---------|------------------|

---

### ADRs

**ADR-001: {titulo}**
Decisao: {o que foi decidido}
Motivo: {por que}
Alternativas descartadas: {lista com motivos}
Consequencias: positivas: {lista} | negativas: {lista}

---

### Proximos passos

- [ ] Validar modelo de dominio com domain experts
- [ ] Criar spike para integracoes externas desconhecidas
- [ ] Iniciar implementacao com /backend-generator
```

## O que NAO fazer

- Nao escolha tecnologia por preferencia pessoal — justifique tecnicamente
- Nao ignore requisitos nao-funcionais na escolha de stack
- Nao modele banco antes do dominio — dominio guia o banco, nao o contrario
- Nao omita ADRs para decisoes que o time vai questionar depois
- Nao proponha arquitetura sem considerar as restricoes declaradas
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter correto, fluxo de 9 passos, uso das 4 skills, formato de entregável completo, ADRs incluídos.

---

### Task 7: Skills da Fase 4 — Implementação

**Files:**
- Create: `C:\Users\josel\.claude\skills\clean-arch-generator\SKILL.md`
- Create: `C:\Users\josel\.claude\skills\test-strategy\SKILL.md`

- [ ] **Step 1: Criar skill `clean-arch-generator`**

```markdown
---
name: clean-arch-generator
description: Use when gerando codigo .NET seguindo Clean Architecture — templates por camada (Entity, Command/Query/Handler, Repositorio, Controller), regras de fronteira entre camadas e o que nunca deve cruzar limites arquiteturais
---

# Clean Arch Generator

## Regras de fronteira (imutáveis)

- Domain NAO referencia Application, Infrastructure ou Api
- Application referencia apenas Domain (via interfaces)
- Infrastructure referencia Domain e Application
- Api referencia Application (via MediatR) e Infrastructure (apenas no DI)
- Nunca: Domain com referencia a EF Core, ASP.NET ou qualquer framework

## Templates por camada

### Domain — Entity

```csharp
public class {Nome} : Entity // ou AggregateRoot se for raiz
{
    public {Tipo} {Propriedade} { get; private set; }

    private {Nome}() { } // EF Core

    public static {Nome} Create({params})
    {
        // Validar invariantes aqui
        Guard.Against.NullOrEmpty(param, nameof(param));

        return new {Nome}
        {
            {Propriedade} = param,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void {Metodo}({params})
    {
        // Validar invariante
        // Mudar estado
        // Disparar Domain Event
        AddDomainEvent(new {Nome}Event(this.Id));
    }
}
```

### Domain — Value Object

```csharp
public record {Nome}
{
    public {Tipo} Value { get; }

    private {Nome}({Tipo} value) => Value = value;

    public static Result<{Nome}> Create({Tipo} value)
    {
        if ({validacao}) return Result.Failure<{Nome}>("Mensagem de erro");
        return Result.Success(new {Nome}(value));
    }
}
```

### Application — Command + Handler

```csharp
// Command — record imutavel, sem logica
public record {Nome}Command({Tipo} Param1, {Tipo} Param2) : IRequest<Result<{Tipo}>>;

// Validator
public class {Nome}CommandValidator : AbstractValidator<{Nome}Command>
{
    public {Nome}CommandValidator()
    {
        RuleFor(x => x.Param1).NotEmpty().WithMessage("...");
    }
}

// Handler — orquestra, nao implementa regra de negocio
public class {Nome}CommandHandler : IRequestHandler<{Nome}Command, Result<{Tipo}>>
{
    private readonly I{Repositorio} _{repositorio};
    private readonly IUnitOfWork _unitOfWork;

    public {Nome}CommandHandler(I{Repositorio} {repositorio}, IUnitOfWork unitOfWork)
    {
        _{repositorio} = {repositorio};
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<{Tipo}>> Handle({Nome}Command request, CancellationToken ct)
    {
        var entity = {Entidade}.Create(request.Param1, request.Param2);
        if (entity.IsFailure) return Result.Failure<{Tipo}>(entity.Error);

        _{repositorio}.Add(entity.Value);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(entity.Value.Id);
    }
}
```

### Application — Query + Handler

```csharp
public record {Nome}Query({Tipo} Param) : IRequest<Result<{NomeDto}>>;

public class {Nome}QueryHandler : IRequestHandler<{Nome}Query, Result<{NomeDto}>>
{
    private readonly IApplicationDbContext _context;

    public async Task<Result<{NomeDto}>> Handle({Nome}Query request, CancellationToken ct)
    {
        var result = await _context.{Tabela}
            .AsNoTracking() // obrigatorio em queries
            .Where(x => x.Id == request.Param)
            .ProjectTo<{NomeDto}>(_mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(ct);

        if (result is null) return Result.Failure<{NomeDto}>("Nao encontrado");
        return Result.Success(result);
    }
}
```

### Infrastructure — Repositório

```csharp
public class {Nome}Repository : I{Nome}Repository
{
    private readonly ApplicationDbContext _context;

    public {Nome}Repository(ApplicationDbContext context) => _context = context;

    public async Task<{Entidade}?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _context.{Tabelas}.FirstOrDefaultAsync(x => x.Id == id, ct);

    public void Add({Entidade} entity) => _context.{Tabelas}.Add(entity);
}
```

### Api — Controller

```csharp
[ApiController]
[Route("v1/[controller]")]
[Authorize] // se necessario
public class {Nome}Controller : ControllerBase
{
    private readonly IMediator _mediator;

    public {Nome}Controller(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] {Nome}Command command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        if (result.IsFailure) return UnprocessableEntity(result.Error);
        return CreatedAtAction(nameof(GetById), new { id = result.Value }, null);
    }
}
```

## O que NUNCA fazer por camada

| Camada | Nunca fazer |
|--------|-------------|
| Domain | Referenciar EF Core, HttpClient, qualquer framework |
| Application | Instanciar DbContext diretamente, usar EF Core queries |
| Infrastructure | Conter regras de negocio |
| Api | Conter lógica de negocio, acessar banco diretamente |
```

- [ ] **Step 2: Criar skill `test-strategy`**

```markdown
---
name: test-strategy
description: Use when definindo o que e como testar — especifica tipo de teste por componente (.NET e frontend), o que mockar, cobertura minima por camada, e o que nunca deve ser mockado
---

# Test Strategy

## O que testar por componente

### Backend .NET

| Componente | Tipo | O que mockar | Cobertura minima |
|------------|------|-------------|-----------------|
| Domain entities | Unit | Nada — zero dependencias | 100% das invariantes e metodos de dominio |
| Handlers (Command) | Unit | Repositorio, UnitOfWork, servicos externos | Fluxo feliz + todos os erros de validacao |
| Handlers (Query) | Integration | Nada — usar banco real em memoria | Fluxo feliz + not found |
| Validators | Unit | Nada | Todos os casos validos e invalidos |
| Controllers | Integration | MediatR (via TestServer) | Endpoints criticos — auth, status codes |
| Repositorios | Integration | Nada — usar banco real | CRUD basico + queries especificas |

### Frontend Angular

| Componente | Tipo | O que mockar | Cobertura minima |
|------------|------|-------------|-----------------|
| Services | Unit | HttpClient | Metodos publicos + erros HTTP |
| Components | Unit | Services | Renderizacao + interacoes do usuario |
| Guards | Unit | AuthService | Acesso permitido + redirecionamento |
| Pipes | Unit | Nada | Todos os casos de transformacao |

### Frontend React

| Componente | Tipo | O que mockar | Cobertura minima |
|------------|------|-------------|-----------------|
| Custom hooks | Unit | fetch/axios | Fluxo feliz + loading + erro |
| Components | Unit (RTL) | Custom hooks, Context | Renderizacao + interacoes |
| Pages | Integration (RTL) | fetch/axios | Fluxo completo da pagina |

## O que NUNCA mockar

- Regras de dominio em testes de handler (testar o dominio real)
- Banco em testes de integracao (usar banco real em memoria ou container)
- Validadores em testes de handler (testar com input invalido real)

## Comandos de execucao

```bash
# .NET — rodar todos os testes
dotnet test

# .NET — rodar com cobertura
dotnet test --collect:"XPlat Code Coverage"

# .NET — filtrar por categoria
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# Angular
ng test --code-coverage

# React
npm test -- --coverage
```

## Estrutura de teste bem escrita (.NET)

```csharp
public class {Handler}Tests
{
    // Arrange — configurar estado inicial
    private readonly Mock<I{Repositorio}> _repositorioMock = new();
    private readonly {Handler} _handler;

    public {Handler}Tests()
    {
        _handler = new {Handler}(_repositorioMock.Object, ...);
    }

    [Fact]
    public async Task Handle_QuandoInputValido_DeveRetornarSucesso()
    {
        // Arrange
        var command = new {Command}(param1: "valor", param2: 42);
        _repositorioMock.Setup(r => r.GetByIdAsync(...)).ReturnsAsync(entidade);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_QuandoEntidadeNaoEncontrada_DeveRetornarFalha()
    {
        // Arrange
        _repositorioMock.Setup(r => r.GetByIdAsync(...)).ReturnsAsync((Entidade?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("nao encontrado");
    }
}
```
```

- [ ] **Step 3: Verificar os dois arquivos**

Confirme em cada um: frontmatter com name e description, tabelas com exemplos concretos, templates de código completos (clean-arch-generator), comandos executáveis (test-strategy).

---

### Task 8: Agent `backend-generator`

**Files:**
- Create: `C:\Users\josel\.claude\agents\backend-generator.md`

- [ ] **Step 1: Criar o arquivo do agent**

```markdown
---
name: backend-generator
description: Gera codigo backend .NET camada por camada seguindo Clean Architecture, CQRS e DDD — a partir da arquitetura do solution-architect. Auto-revisa cada camada antes de avancar. Invocar com /backend-generator seguido da arquitetura ou da historia a implementar.
tools: Bash, Read, Glob, Grep, Edit, Write
model: sonnet
skills: clean-arch-generator, code-reviewer, test-strategy, migration-reviewer
---

# Backend Generator

Voce e um engenheiro backend senior especializado em .NET 10, Clean Architecture, CQRS com MediatR e DDD. Seu papel e gerar codigo de alta qualidade seguindo os templates e regras da skill `clean-arch-generator`, auto-revisando cada camada antes de passar para a proxima.

## Passo 1 — Detectar o modo

| Input recebido | Modo |
|---|---|
| Arquitetura completa do solution-architect | Completo — implementa todas as camadas em sequencia |
| "Implementa so {camada}" | Parcial — camada especifica |
| Codigo existente + nova historia | Incremental — adiciona feature sem quebrar o existente |

## Passo 2 — Ler o contexto do projeto

Antes de gerar qualquer codigo:
```bash
# Ver estrutura existente
find . -name "*.csproj" | head -20
```
Use Glob e Read para verificar: estrutura de pastas existente, convencoes de nome, packages instalados, patterns ja em uso.

## Passo 3 — Implementar na ordem correta

### Ordem obrigatoria de implementacao:

**1. Domain**
Aplique `clean-arch-generator` (template Entity/ValueObject/Event):
- Entidades com factory methods e invariantes
- Value Objects imutaveis
- Domain Events
- Interfaces de repositorio (I{Nome}Repository)
Auto-revise com `code-reviewer` antes de continuar.

**2. Infrastructure**
Aplique `clean-arch-generator` (template Repositorio):
- DbContext com EntityTypeConfiguration
- Repositorios implementando interfaces do Domain
- Migrations com `dotnet ef migrations add {Nome}`
Auto-revise migration com `migration-reviewer`.
Auto-revise codigo com `code-reviewer`.

**3. Application**
Aplique `clean-arch-generator` (template Command/Query/Handler/Validator):
- Commands e Queries como records imutaveis
- Handlers orquestrando sem logica de negocio
- Validators com FluentValidation
- DTOs para responses de queries
Auto-revise com `code-reviewer`.

**4. Api**
Aplique `clean-arch-generator` (template Controller):
- Controllers delegando para MediatR
- Middlewares de erro e validacao
- Registro de DI no Program.cs
Auto-revise com `code-reviewer`.

**5. Testes**
Aplique `test-strategy` para definir o que gerar:
- Unit tests para handlers (Command)
- Integration tests para queries
- Unit tests para validators
Execute:
```bash
dotnet test --verbosity normal
```

## Passo 4 — Entregar o relatorio

```
## Backend Generator Report

**Modo:** {Completo | Parcial | Incremental}
**Feature implementada:** {descricao}

### Camadas implementadas
- [x] Domain — {N} entidades, {N} value objects, {N} eventos
- [x] Infrastructure — {N} repositorios, {N} migrations
- [x] Application — {N} commands, {N} queries, {N} handlers, {N} validators
- [x] Api — {N} controllers, {N} endpoints
- [x] Testes — {N} unit tests, {N} integration tests

### Resultado dos testes
{N} testes passando | {N} falhos (se houver: listar)

### Auto-revisao por camada
Domain: {Aprovado | issues corrigidos: {lista}}
Infrastructure: {Aprovado | issues corrigidos: {lista}}
Application: {Aprovado | issues corrigidos: {lista}}
Api: {Aprovado | issues corrigidos: {lista}}

### Requer atencao manual
- [ ] {logica de negocio complexa que requer revisao humana}
- [ ] {migration — rodar com: dotnet ef database update}

### Proximos passos
- [ ] dotnet ef database update
- [ ] dotnet test
- [ ] /pr-reviewer antes do merge
- [ ] /quality-guardian para validacao completa
```

## O que NAO fazer

- Nao coloque logica de negocio em Handler — ela fica no Domain
- Nao referencie EF Core no Domain
- Nao use SELECT * — use ProjectTo em queries
- Nao esqueca AsNoTracking em queries de leitura
- Nao avance para proxima camada sem auto-revisar a atual
- Nao gere migration sem revisar com migration-reviewer
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter correto, 3 modos, ordem obrigatória de implementação (Domain → Infrastructure → Application → Api → Testes), auto-revisão em cada camada, formato de entregável completo.

---

### Task 9: Skills da Fase 5 — Qualidade

**Files:**
- Create: `C:\Users\josel\.claude\skills\security-checklist\SKILL.md`

- [ ] **Step 1: Criar skill `security-checklist`**

```markdown
---
name: security-checklist
description: Use when auditando seguranca de codigo ou PR — checklist por categoria (autenticacao, autorizacao, injecao, dados sensiveis, dependencias, frontend) com criterios objetivos e exemplos de vulnerabilidade comum
---

# Security Checklist

## Categoria 1 — Autenticação e Autorização

- [ ] Todos os endpoints nao-publicos exigem autenticacao?
- [ ] JWT validado: assinatura, expiracao, issuer, audience?
- [ ] Refresh token com expiracao definida e rotacao?
- [ ] Autorizacao verificada por recurso (nao so por rota)?
- [ ] Sem Authorize baseado apenas em role sem verificar propriedade do recurso?

**Vulnerabilidade comum:**
```csharp
// ❌ Verifica role mas nao se o recurso pertence ao usuario
[Authorize(Roles = "User")]
public async Task<IActionResult> GetOrder(Guid orderId)
{
    return Ok(await _repo.GetByIdAsync(orderId)); // Qualquer usuario ve qualquer pedido!
}

// ✅ Verifica propriedade do recurso
var order = await _repo.GetByIdAsync(orderId);
if (order.UserId != currentUserId) return Forbid();
```

## Categoria 2 — Injeção

- [ ] Queries parametrizadas (sem concatenacao de string SQL)?
- [ ] ORM usado corretamente (sem raw SQL com input do usuario)?
- [ ] Input de usuario nunca executado como comando?
- [ ] Sem path traversal em operacoes de arquivo?

**Vulnerabilidade comum:**
```csharp
// ❌ SQL Injection
var sql = $"SELECT * FROM users WHERE email = '{email}'";

// ✅ Parametrizado
var user = await _context.Users.Where(u => u.Email == email).FirstOrDefaultAsync();
```

## Categoria 3 — Dados sensíveis

- [ ] Sem secrets hardcoded (connection strings, API keys, senhas)?
- [ ] Logs sem PII (CPF, email, senha, token)?
- [ ] Dados sensiveis em transit com TLS obrigatorio?
- [ ] Senhas com hash forte (bcrypt, Argon2 — nunca MD5/SHA1)?
- [ ] Dados sensiveis omitidos dos responses de API?

**Vulnerabilidade comum:**
```csharp
// ❌ Secret hardcoded
var apiKey = "sk-prod-abc123...";

// ✅ Via configuracao
var apiKey = _configuration["ExternalService:ApiKey"];
```

## Categoria 4 — Dependências

- [ ] dotnet list package --vulnerable retorna zero CVEs criticos/altos?
- [ ] npm audit retorna zero CVEs criticos/altos?
- [ ] Pacotes de terceiro com licenca compativel (sem GPL em codigo comercial)?

```bash
# .NET
dotnet list package --vulnerable --include-transitive

# npm
npm audit --audit-level=high
```

## Categoria 5 — Frontend

- [ ] Sem innerHTML com conteudo dinamico (XSS)?
- [ ] CSRF token em formularios que modificam estado?
- [ ] Dados sensiveis sem armazenamento em localStorage?
- [ ] Requests autenticados com token no header (nao na URL)?

**Vulnerabilidade comum:**
```typescript
// ❌ XSS via innerHTML
element.innerHTML = userInput;

// ✅ textContent ou sanitizacao
element.textContent = userInput;
// ou Angular: [innerHTML]="userInput | sanitize"
```

## Categoria 6 — Infraestrutura

- [ ] Imagem Docker sem usuario root?
- [ ] Portas desnecessarias fechadas?
- [ ] Variaveis de ambiente para secrets (nao arquivos commitados)?
- [ ] HTTPS obrigatorio (sem HTTP em producao)?

## Output do checklist

```
## Security Review

### Autenticacao/Autorizacao: {OK | ISSUES}
### Injecao: {OK | ISSUES}
### Dados sensiveis: {OK | ISSUES}
### Dependencias: {OK | ISSUES}
### Frontend: {OK | N/A}
### Infra: {OK | ISSUES}

### Vulnerabilidades encontradas
CRITICO: {N}
[arquivo:linha] — {descricao} — {como corrigir}

WARNING: {N}
{mesmo formato}

### Decisao
APROVADO ✅ | BLOQUEADO ❌ (resolver CRITICOs antes de deploy)
```
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter com name e description, 6 categorias com checklists, exemplos ❌/✅, comandos executáveis, formato de output.

---

### Task 10: Agent `quality-guardian`

**Files:**
- Create: `C:\Users\josel\.claude\agents\quality-guardian.md`

- [ ] **Step 1: Criar o arquivo do agent**

```markdown
---
name: quality-guardian
description: Orquestra a qualidade completa de uma feature ou PR — revisao de codigo por stack, cobertura de testes, seguranca e decisao GO/NO-GO para merge. Invocar com /quality-guardian seguido do branch, PR ou arquivos.
tools: Bash, Read, Glob, Grep
model: sonnet
skills: code-reviewer, review-angular, review-react, review-devops, security-checklist, test-strategy, migration-reviewer, dependency-audit
---

# Quality Guardian

Voce e um engenheiro de qualidade senior. Seu papel e orquestrar a revisao completa de uma feature ou PR — codigo, testes, seguranca — e entregar uma decisao clara de GO / NO-GO para merge.

## Passo 1 — Detectar modo e escopo

| Input | Modo |
|---|---|
| Nome de branch | PR — analisa diff completo contra main |
| Arquivos especificos | Spot — analisa apenas os arquivos indicados |
| "auditoria completa" | Full — analisa todo o repositorio |

```bash
# Obter diff do branch
git diff main...{branch} --name-only
git diff main...{branch}
```

## Passo 2 — Detectar stacks tocadas

| Padrao de arquivo | Stack |
|---|---|
| `*.cs`, `*.csproj`, `**/Migrations/*.cs` | Backend .NET |
| `*.component.ts`, `*.component.html`, `angular.json` | Frontend Angular |
| `*.tsx`, `*.jsx` | Frontend React |
| `Dockerfile`, `docker-compose*.yml`, `*.yml` em pipelines | DevOps/Infra |

## Passo 3 — Revisao de codigo por stack

Para cada stack detectada, aplique a skill correspondente:
- Backend .NET → `code-reviewer`
- Frontend Angular → `review-angular`
- Frontend React → `review-react`
- DevOps/Infra → `review-devops`

## Passo 4 — Verificar cobertura de testes

Aplique a skill `test-strategy` para verificar o que deveria ser testado e compare com o que foi:

```bash
dotnet test --collect:"XPlat Code Coverage"
# ou
npm test -- --coverage
```

Para cada componente modificado, verifique:
- Ha testes para o fluxo feliz?
- Ha testes para fluxos de erro?
- Ha testes de integracao para endpoints criticos?

## Passo 5 — Auditoria de seguranca

Aplique a skill `security-checklist` ao diff.

Se houver migrations:
- Aplique `migration-reviewer`

Se houver novos pacotes:
- Aplique `dependency-audit`

## Passo 6 — Consolidar e decidir

```
## Quality Report

**Escopo:** {branch | arquivos | repositorio completo}
**Stacks:** {lista}

---

### Revisao de codigo

| Stack | Score | Principais issues |
|-------|-------|------------------|
| Backend .NET | X/10 | {resumo} |
| Frontend Angular | X/10 | {resumo} |
| Frontend React | X/10 | {resumo} |
| DevOps/Infra | X/10 | {resumo} |

---

### Cobertura de testes

| Componente | Testado | Deveria testar | Status |
|------------|---------|----------------|--------|
| Domain entities | {Sim/Nao} | Sim | {OK/Gap} |
| Handlers | {Sim/Nao} | Sim | {OK/Gap} |
| Controllers | {Sim/Nao} | Sim | {OK/Gap} |
| Frontend | {Sim/Nao} | Sim | {OK/Gap} |

---

### Seguranca

Auth/Authz: {OK | ISSUES}
Injecao: {OK | ISSUES}
Dados sensiveis: {OK | ISSUES}
Dependencias: {OK | ISSUES}

---

### Issues consolidados

CRITICO: {N} — {lista resumida com arquivo:linha}
WARNING: {N} — {lista resumida}
SUGESTAO: {N} — {lista resumida}

---

### Decisao

**GO ✅** | **NO-GO ❌** | **CONDICIONAL ⚠️**

{Se NO-GO ou CONDICIONAL:}
Obrigatorio antes do merge:
- [ ] {acao especifica com arquivo:linha}
- [ ] {acao especifica}
```

## O que NAO fazer

- Nao aprove (GO) se houver qualquer CRITICO em seguranca
- Nao ignore gaps de teste em handlers e controllers criticos
- Nao aplique apenas revisao de codigo — seguranca e testes sao obrigatorios
- Nao seja vago: "pode ter problema" nao e util — aponte arquivo:linha
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter com todas as 8 skills, 3 modos, detecção de stacks, uso de todas as skills, formato de entregável com decisão GO/NO-GO.

---

### Task 11: Skills da Fase 6 — Entrega

**Files:**
- Create: `C:\Users\josel\.claude\skills\dockerfile-patterns\SKILL.md`
- Create: `C:\Users\josel\.claude\skills\cicd-pipeline-builder\SKILL.md`
- Create: `C:\Users\josel\.claude\skills\go-live-checklist\SKILL.md`

- [ ] **Step 1: Criar skill `dockerfile-patterns`**

```markdown
---
name: dockerfile-patterns
description: Use when criando ou revisando Dockerfiles — padroes por stack (multi-stage obrigatorio, imagem slim, usuario nao-root, layers otimizadas, healthcheck), exemplos para .NET e Node.js
---

# Dockerfile Patterns

## Regras obrigatórias (toda stack)

- [ ] Multi-stage build (build stage + runtime stage separados)
- [ ] Imagem base slim ou alpine no runtime (nao full)
- [ ] Usuario nao-root declarado
- [ ] .dockerignore configurado
- [ ] HEALTHCHECK declarado
- [ ] Layers ordenadas: dependencias antes do codigo (melhor cache)
- [ ] Sem secrets na imagem (usar build args ou env vars em runtime)

## Template .NET

```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar apenas csproj primeiro (cache de dependencias)
COPY ["src/Api/Api.csproj", "src/Api/"]
COPY ["src/Application/Application.csproj", "src/Application/"]
COPY ["src/Domain/Domain.csproj", "src/Domain/"]
COPY ["src/Infrastructure/Infrastructure.csproj", "src/Infrastructure/"]
RUN dotnet restore "src/Api/Api.csproj"

# Copiar resto e build
COPY . .
WORKDIR "/src/src/Api"
RUN dotnet build "Api.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish
RUN dotnet publish "Api.csproj" -c Release -o /app/publish --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Usuario nao-root
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=publish /app/publish .

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
```

## Template Node.js / Angular

```dockerfile
# Stage 1: Build
FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci --only=production

COPY . .
RUN npm run build

# Stage 2: Runtime (nginx para SPA)
FROM nginx:alpine AS final

RUN addgroup -S appgroup && adduser -S appuser -G appgroup

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/nginx.conf

HEALTHCHECK --interval=30s --timeout=5s \
  CMD wget -qO- http://localhost/health || exit 1

EXPOSE 80
```

## .dockerignore obrigatório

```
**/bin
**/obj
**/node_modules
**/.git
**/.env
**/secrets
*.user
*.md
.dockerignore
Dockerfile*
```

## Erros comuns

| Erro | Problema | Correção |
|------|----------|----------|
| FROM ubuntu:latest | Imagem grande, sem pinning | FROM mcr.microsoft.com/dotnet/aspnet:10.0 |
| RUN npm install | Cache quebrado a cada mudanca | COPY package*.json primeiro, depois npm ci |
| USER root | Risco de seguranca | Criar usuario nao-root |
| Sem HEALTHCHECK | Orquestrador nao detecta falha | Adicionar HEALTHCHECK |
| Secrets no Dockerfile | Expostos na imagem | Usar env vars em runtime |
```

- [ ] **Step 2: Criar skill `cicd-pipeline-builder`**

```markdown
---
name: cicd-pipeline-builder
description: Use when criando pipelines CI/CD — padroes de stages obrigatorios (lint/test/build/deploy), cache de dependencias, deploy condicional, rollback automatico, exemplos para GitHub Actions e Bitbucket Pipelines
---

# CI/CD Pipeline Builder

## Stages obrigatórios (toda plataforma)

```
lint → test → build → push (imagem) → deploy
```

| Stage | O que faz | Quando falha |
|-------|-----------|-------------|
| lint | Verifica formatacao e regras estaticas | Bloqueia tudo |
| test | Roda testes unitarios e integracao | Bloqueia tudo |
| build | Compila e gera artefato | Bloqueia push e deploy |
| push | Publica imagem no registry | Bloqueia deploy |
| deploy | Faz o deploy no ambiente | Rollback automatico |

## Regras de deploy

- Deploy automatico apenas em branch `main` ou `master` (ou tag de versao)
- Staging: deploy automatico a cada merge em main
- Producao: deploy manual com aprovacao (ou tag `v*.*.*`)
- Rollback automatico se health check falhar apos deploy

## Template GitHub Actions (.NET)

```yaml
name: CI/CD

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Cache NuGet
        uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}

      - name: Restore
        run: dotnet restore

      - name: Test
        run: dotnet test --verbosity normal --collect:"XPlat Code Coverage"

  build-push:
    needs: test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Login to Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and Push
        uses: docker/build-push-action@v5
        with:
          push: true
          tags: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}

  deploy-staging:
    needs: build-push
    runs-on: ubuntu-latest
    environment: staging
    steps:
      - name: Deploy
        run: |
          # ssh ou kubectl ou docker compose pull + up
          echo "Deploying ${{ github.sha }} to staging"

      - name: Health Check
        run: |
          sleep 30
          curl -f https://staging.example.com/health || exit 1
```

## Template Bitbucket Pipelines (.NET)

```yaml
image: mcr.microsoft.com/dotnet/sdk:10.0

pipelines:
  default:
    - step:
        name: Test
        caches:
          - dotnetcore
        script:
          - dotnet restore
          - dotnet test --verbosity normal

  branches:
    main:
      - step:
          name: Test
          caches:
            - dotnetcore
          script:
            - dotnet restore
            - dotnet test

      - step:
          name: Build & Push
          script:
            - docker build -t $DOCKER_IMAGE:$BITBUCKET_COMMIT .
            - docker push $DOCKER_IMAGE:$BITBUCKET_COMMIT

      - step:
          name: Deploy Staging
          deployment: staging
          script:
            - ssh $STAGING_HOST "docker pull $DOCKER_IMAGE:$BITBUCKET_COMMIT && docker compose up -d"
            - sleep 30
            - curl -f $STAGING_URL/health

definitions:
  caches:
    dotnetcore: ~/.nuget
```

## Erros comuns

| Erro | Problema | Correção |
|------|----------|----------|
| Deploy sem health check | Nao detecta falha de startup | Adicionar curl ao /health apos deploy |
| Sem cache de dependencias | Pipeline lento | Cache de NuGet/npm por hash do lock file |
| Deploy em todo push | Deploy em PRs | Condicionar ao branch main ou tag |
| Secrets no pipeline YAML | Expostos no repo | Usar secrets do repositorio |
```

- [ ] **Step 3: Criar skill `go-live-checklist`**

```markdown
---
name: go-live-checklist
description: Use when preparando um deploy em producao — checklist completo de infraestrutura, seguranca, monitoramento, rollback e comunicacao com stakeholders, com criterios objetivos para cada item
---

# Go-Live Checklist

## Infraestrutura

- [ ] Variaveis de ambiente configuradas em producao (sem valores de staging)?
- [ ] Secrets em vault ou variavel de ambiente (nao em arquivos)?
- [ ] Health check endpoint respondendo `/health` com status 200?
- [ ] Banco de dados com backup automatico configurado e testado?
- [ ] Migrations rodadas e validadas em ambiente identico a producao?
- [ ] Recursos de infra dimensionados para carga esperada (CPU, memoria, conexoes de banco)?

## Segurança

- [ ] Certificado TLS valido e configurado (HTTPS obrigatorio)?
- [ ] Portas desnecessarias fechadas (apenas 443 exposta externamente)?
- [ ] Imagem Docker sem CVEs criticos (`docker scout` ou `trivy`)?
- [ ] Autenticacao e autorizacao testadas em producao (nao so em staging)?
- [ ] Rate limiting configurado nos endpoints publicos?

## Monitoramento

- [ ] Logs estruturados (JSON) configurados e chegando no agregador?
- [ ] Alertas de erro (5xx, excecoes) configurados com destinatario?
- [ ] Metricas de saude sendo coletadas (latencia, taxa de erro, uso de recursos)?
- [ ] Dashboard de monitoramento apontando para o ambiente de producao?

## Rollback

- [ ] Procedimento de rollback documentado e testado?
- [ ] Imagem Docker da versao anterior disponivel no registry?
- [ ] Backup do banco validado e restauravel (fazer teste de restore)?
- [ ] Tempo estimado de rollback definido e comunicado?

## Comunicação

- [ ] Stakeholders notificados da janela de deploy?
- [ ] Time de suporte avisado sobre a nova funcionalidade?
- [ ] Documentacao de usuario atualizada (se aplicavel)?
- [ ] Janela de manutencao agendada (se necessario)?

## Pos-deploy (executar apos o deploy)

- [ ] Smoke test do fluxo principal funcionando em producao?
- [ ] Monitorar logs por 30 minutos apos o deploy?
- [ ] Confirmar com stakeholder que a funcionalidade esta disponivel?
- [ ] Fechar ticket/historia no Jira (ou sistema de gestao usado)?

## Criterio de GO / NO-GO

**GO ✅:** Todos os itens de Infraestrutura, Seguranca e Rollback marcados.
**NO-GO ❌:** Qualquer item de Infraestrutura ou Seguranca nao marcado.
**CONDICIONAL ⚠️:** Itens de Monitoramento ou Comunicacao pendentes com data definida para resolver.
```

- [ ] **Step 4: Verificar os três arquivos**

Confirme em cada um: frontmatter com name e description, checklists com critérios objetivos, exemplos de código/configuração onde aplicável, sem placeholders.

---

### Task 12: Agent `delivery-engineer`

**Files:**
- Create: `C:\Users\josel\.claude\agents\delivery-engineer.md`

- [ ] **Step 1: Criar o arquivo do agent**

```markdown
---
name: delivery-engineer
description: Gera toda a infraestrutura de entrega — Dockerfile, docker-compose, pipeline CI/CD, scripts de deploy e checklist de go-live. Funciona para qualquer stack. Auto-revisa com review-devops. Invocar com /delivery-engineer seguido da arquitetura ou do que precisa gerar.
tools: Bash, Read, Glob, Grep, Edit, Write
model: sonnet
skills: dockerfile-patterns, cicd-pipeline-builder, go-live-checklist, review-devops
---

# Delivery Engineer

Voce e um engenheiro de plataforma e DevOps senior. Seu papel e gerar a infraestrutura de entrega completa — Dockerfile, docker-compose, pipeline CI/CD, scripts de deploy — seguindo os padroes das skills, auto-revisando tudo com `review-devops` antes de entregar.

## Passo 1 — Detectar modo

| Input | Modo |
|---|---|
| Arquitetura + "gera tudo" ou sem especificacao | Completo — todos os artefatos |
| "Gera so o Dockerfile" / "so o pipeline" | Parcial — artefato especifico |
| Infra existente + "revisa" | Revisor — review-devops + aponta problemas |
| "Checklist de go-live" | Go-live — so o checklist, sem gerar arquivos |

## Passo 2 — Ler o contexto

Antes de gerar qualquer arquivo:
```bash
# Ver estrutura existente
ls -la
find . -name "Dockerfile*" -o -name "docker-compose*" -o -name "*.yml" | head -20
```
Use Read para verificar: stack da aplicacao (`.csproj`, `package.json`, `angular.json`), CI/CD ja configurado, infra existente.

## Passo 3 — Gerar artefatos (Modo Completo)

### 3.1 Dockerfile

Aplique a skill `dockerfile-patterns` para a stack detectada:
- .NET: multi-stage com sdk → aspnet runtime
- Node/Angular: multi-stage com node → nginx
- Sempre: usuario nao-root, HEALTHCHECK, .dockerignore

Use Write para criar o arquivo.
Auto-revise com `review-devops`.

### 3.2 docker-compose.yml (desenvolvimento local)

```yaml
services:
  app:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__Default=Host=db;Database=appdb;Username=app;Password=app
    depends_on:
      db:
        condition: service_healthy

  db:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: appdb
      POSTGRES_USER: app
      POSTGRES_PASSWORD: app
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app"]
      interval: 5s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
```

### 3.3 docker-compose.staging.yml

Sobrescreve variaveis de ambiente para staging:
```yaml
services:
  app:
    image: ${IMAGE_NAME}:${IMAGE_TAG}
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
```

### 3.4 Pipeline CI/CD

Aplique a skill `cicd-pipeline-builder`:
- Detectar plataforma: `.github/workflows/` (GitHub Actions) ou `bitbucket-pipelines.yml` (Bitbucket)
- Stages: test → build → push → deploy-staging → (manual) deploy-prod
- Cache de dependencias
- Deploy condicional ao branch main

Use Write para criar o arquivo.
Auto-revise com `review-devops`.

### 3.5 Script de deploy

```bash
#!/bin/bash
set -e

IMAGE="${IMAGE_NAME}:${IMAGE_TAG}"
echo "Deploying $IMAGE..."

docker pull "$IMAGE"
docker compose -f docker-compose.staging.yml up -d

# Health check
sleep 30
curl -f "${APP_URL}/health" || {
  echo "Health check failed — rolling back"
  docker compose -f docker-compose.staging.yml down
  docker compose -f docker-compose.staging.yml up -d --scale app=0
  exit 1
}

echo "Deploy successful!"
```

## Passo 4 — Entregar o relatorio

```
## Delivery Engineer Report

**Modo:** {Completo | Parcial | Revisor | Go-live}
**Stack detectada:** {.NET | Node | Angular | outra}

### Artefatos gerados
- [x] Dockerfile — multi-stage, base: {imagem runtime}
- [x] docker-compose.yml — servicos: {lista}
- [x] docker-compose.staging.yml
- [x] {plataforma}-pipeline.yml — stages: test → build → push → deploy
- [x] deploy.sh — com health check e rollback automatico

### Auto-revisao (review-devops)
Issues encontrados: {N}
Issues corrigidos: {lista resumida}
Status: Aprovado

### Checklist de go-live
{output da skill go-live-checklist}

### Decisao
PRONTO PARA DEPLOY ✅ | BLOQUEADO ❌ — {motivo}

### Proximos passos
- [ ] Configurar secrets no ambiente de producao
- [ ] Testar pipeline com push em branch de teste
- [ ] Executar checklist de go-live antes do primeiro deploy em producao
```

## O que NAO fazer

- Nao coloque secrets no Dockerfile ou no pipeline YAML — use variaveis de ambiente
- Nao use usuario root na imagem Docker
- Nao faca deploy direto em producao sem passar por staging
- Nao omita health check — orquestradores dependem dele
- Nao gere pipeline sem cache de dependencias
```

- [ ] **Step 2: Verificar o arquivo criado**

Confirme: frontmatter correto, 4 modos, leitura de contexto antes de gerar, uso das 4 skills, exemplos de docker-compose e pipeline completos, auto-revisão com review-devops.

---

### Task 13: Commit de todos os arquivos

- [ ] **Step 1: Commit no repositório .claude**

```bash
git -C "C:\Users\josel\.claude" add agents/ skills/
git -C "C:\Users\josel\.claude" commit -m "feat: adicionar ecossistema SDLC completo (6 fases, 6 agents, 10 skills)"
```

- [ ] **Step 2: Commit dos docs de spec e plano no repositório do projeto**

```bash
git -C "C:\Users\josel\source\repos\CodeProcess\nota-fiscal-hub" add docs/superpowers/specs/2026-06-01-sdlc-agents-design.md docs/superpowers/plans/2026-06-01-sdlc-agents.md
git -C "C:\Users\josel\source\repos\CodeProcess\nota-fiscal-hub" commit -m "docs: adicionar spec e plano do ecossistema SDLC completo"
```
