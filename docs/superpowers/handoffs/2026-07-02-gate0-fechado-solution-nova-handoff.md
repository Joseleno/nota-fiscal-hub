# Handoff — Gate 0 FECHADO, solution reestruturada; próximo é A5 (ajustar plano-mestre + detalhar Fase 0)

**Data:** 2026-07-02
**Como retomar:** ler este arquivo primeiro (é o mais recente). Próximo passo é a **tarefa A5** — ver §6.

---

## 1. O que esta sessão fez (tudo concluído e commitado na branch `dev`)

1. **A1 — g0-qualidade** (5º parecer do Gate 0): análise só-leitura da suíte legada. Relatório `docs/superpowers/reviews/2026-07-02-g0-qualidade.md`. Suíte 761/761 verde MAS conviveu com os 3 bugs fiscais. Achados: **Q1** (5 eventos MSG0005 descartados sem handler — Rejeitado/Cancelado/Falhou não notificam webhook), **Q2** (golden files sem XSD = causa-raiz dos bugs #1/#2), **Q3** (integração roda em EF InMemory, não Testcontainers). Notas: Domain 8, Application 7, Infrastructure 6, Integration 5, Api 7. Parecer: APROVEITAR domínio+Motor / REESCREVER integração+golden files.
2. **A2 — matriz final do Gate 0**: `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`. 12 linhas APROVEITAR/ADAPTAR/REESCREVER, aprovada integralmente pelo dono. 6 linhas REESCREVER (Contas&Planos, custódia, Emissão-núcleo, Documentos, Kernel, Deployables); 4 ADAPTAR (estrutura Empresas, casca Emissão, Motor condicionado, Outbox); testes de domínio/Motor APROVEITAR.
3. **A3 — governança das decisões**: `docs/decisions.md` §0 formalizado. **D-2026-07-01-01..10 RATIFICADAS integralmente** (Status/Data/Decisor); P1–P5 em tabela com Dono+Prazo (P1 decidida); §0.2 é a ata da sessão de ratificação. Decisor: Joseleno Dias Moreira dos Santos (dono).
4. **Decisões novas** (`docs/decisions.md` §0.1): **D-2026-07-02-01 SOLUTION NOVA** (não evoluir in-place; porte cirúrgico do Motor ~2.654 linhas), D-2026-07-02-02 (Motor ADAPTAR condicionado a golden files/XSD na Fase 3), D-2026-07-02-03 (registrar handlers dos 3 eventos — achado Q1).
5. **Reestruturação física da solution** (materializa D-2026-07-02-01):
   - `old/` = VisuFiscalHub legado inteiro (`src`, `tests`, `.slnx`, `Dockerfile`, `infra`, `scripts`, `.github`, `.dockerignore`) + specs/planos históricos em `old/docs/`. **A solution legada continua compilando dentro de `old/`** (restore verificado).
   - `docs/` na raiz = só documentação do produto novo (design, plano-mestre, decisions, reviews/handoffs do Gate 0, 14 specs A/B em `docs/superpowers/specs/tarefas/`).
   - `EmitaNotaAqui/` = nova solution, por ora só com README apontando design/plano/estrutura-alvo. Scaffold real é a tarefa B1.

## 2. Estado do Gate 0

**FECHADO em 2026-07-02.** Critério de saída atingido: matriz aprovada + decisões registradas com governança rastreável (`docs/decisions.md` §0.1/§0.2). Checkboxes do Gate 0 marcados no plano-mestre. Grupo A do board: **A1✅ A2✅ A3✅**; falta **A4** (pré-req SEFAZ-SE — ação externa do dono) e **A5** (esta é a próxima; §6).

## 3. Commits (branch `dev`)

```
ab3e81c docs: formalizar governanca das decisoes na saida do Gate 0 (A3)
6e0d033 docs: criar pasta da solution nova EmitaNotaAqui com README
7b96d51 chore: mover solution legada VisuFiscalHub para old/
d99f08d docs: fechar Gate 0 - matriz final aprovada e decisoes ratificadas
0a3e23b docs: adicionar 5o parecer do Gate 0 (g0-qualidade da suite legada)
```

Working tree **limpa**. **Estamos na branch `dev`** (não master) — os commits desta sessão foram todos para `dev`; decisão do dono foi manter em `dev`. Os commits de documentação anteriores (specs, estratégia) ficaram em `master` (`630baa4`).

## 4. Decisões de processo desta sessão (valem para as próximas)

- **Modelo:** projeto configurado para **Sonnet 5** por padrão (`.claude/settings.local.json`); Opus 4.8 só nos gates de decisão. A1/A2/A3 rodaram em Opus (eram gates). **O A5 é documental — rodar em Sonnet.** Estratégia completa: `docs/estrategia-modelos.md` e memória `feedback-estrategia-modelos`.
- **Commits:** **nenhuma referência a Claude/IA** (sem Co-Authored-By). Configurado em `attribution` do settings.local.json; memória `feedback-no-claude-in-commits`.
- **ClickUp:** **NÃO sincronizar via MCP.** O dono acompanha o board manualmente (lista Hub `901714896084`). Trabalhar só com os documentos locais. → **Isto altera a spec A5**: o passo 5 (sync ClickUp) fica FORA; fazer só os passos 3 (plano-mestre) e 6 (plano da Fase 0).
- **Só-leitura de código:** ainda vale até a Fase 0 iniciar. Nada em `src/`/`tests/` (legado em `old/`) deve ser tocado nas tarefas A. O scaffold do código novo começa na tarefa B1 (Fase 0).
- **Contexto:** parar a sessão ao chegar ~70% de contexto (memória `feedback-context-management`).

## 5. Decisões fixadas — não reabrir

- Todas do design §1.1 e as D-2026-07-01-* (agora ratificadas) e D-2026-07-02-* (solution nova, Motor condicionado, handlers de evento).
- Global Constraints do plano-mestre (linhas 11–23) — **preservar textual/semanticamente** em qualquer edição do plano.

## 6. PRÓXIMO PASSO — Tarefa A5 (sessão nova, Sonnet)

Spec: `docs/superpowers/specs/tarefas/A5-ajuste-plano-pos-gate.md`. **Escopo ajustado pelo dono: sem sync ClickUp.** Fazer:

1. **Editar o plano-mestre** (`docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`, passo 3 da spec): refletir a matriz A2 em cada fase — fases encolhem onde há APROVEITAR/ADAPTAR, ganham tarefas de porte nomeadas onde há REESCREVER com reaproveitamento (ex.: porte do Motor na Fase 3). **Global Constraints (linhas 11–23) intactas — diff só aditivo.** Rastreabilidade: cada fase alterada cita o parecer da matriz que a motivou. Refletir as D-* ratificadas no texto das fases (D-01 QR v3, D-04 numeração, D-07 topologia, solution nova). O bloco Gate 0 já está marcado concluído.
2. **Detalhar a Fase 0** (passo 6, skill `superpowers:writing-plans`): plano bite-sized em `docs/superpowers/plans/2026-07-0X-fase-00-fundacao-kernel.md` cobrindo os 9 itens (estrutura da solution, tenant context, outbox/inbox, idempotência, auditoria, NetArchTest, observabilidade fundacional, AWS mínimo, CI). As 9 specs B1–B9 já escritas (`docs/superpowers/specs/tarefas/B*.md`) são o insumo — o plano da Fase 0 as sequencia. O plano da Fase 0 **não porta código do Motor** (isso é Fase 3); só kernel.
3. **Aprovações do dono** nos pontos que a spec pede (diff do plano-mestre por fase; plano da Fase 0). Commitar só com ok do dono (padrão: sem ref a Claude).

**Dependências já satisfeitas:** A2 (matriz aprovada) ✅, A3 (decisões ratificadas + solution decidida) ✅. Falta só executar.

**Depois do A5:** A4 (pré-req SEFAZ-SE — depende de ação externa do dono: certificado A1 teste, CSC homolog, credenciamento, XSDs) pode correr em paralelo; e então iniciar a **execução da Fase 0** (tarefa B1: scaffold da solution EmitaNotaAqui) via `superpowers:subagent-driven-development` ou `executing-plans`.

## 7. Handoffs anteriores (contexto histórico)

- `2026-07-01-revisao-docs-e-gate0-handoff.md` — revisão dos documentos + estado do Gate 0 antes desta sessão.
- `2026-07-02-gate0-handoff.md` — os 4 primeiros pareceres do Gate 0 (§3).
- `2026-07-02-board-clickup-specs-handoff.md` — board ClickUp + mapa card↔spec (referência; **mas não sincronizar mais**).
- **Este arquivo supera todos para retomada** — é o mais recente e reflete o Gate 0 fechado.
