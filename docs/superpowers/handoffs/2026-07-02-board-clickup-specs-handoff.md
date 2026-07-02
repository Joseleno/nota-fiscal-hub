# Handoff — Board ClickUp criado (50 tarefas) + 14 specs A/B escritas; anexos em finalização

**Data:** 2026-07-02 08:40 (America/Fortaleza)
**Como retomar:** `/resume` — ler este arquivo primeiro (é o handoff mais recente; o de `2026-07-01-revisao-docs-e-gate0-handoff.md` segue válido para o estado do Gate 0, e o de `2026-07-02-gate0-handoff.md` guarda os 4 relatórios do gate).

---

## 1. O que esta sessão fez

1. **Nada foi implementado em código** (regra do gate mantida). Um workflow de g0-qualidade foi lançado por engano de interpretação e **abortado a pedido do dono** sem efeitos.
2. **Board ClickUp criado**: 50 tarefas na lista **Hub** (id `901714896084`, espaço Sprints › pasta Projets), todas na coluna **iniciar** (demais colunas: `em andamento`, `terminado`). Grupos: A=Gate 0 restante (5), B=Fase 0 (9), C=Fase 1 (6), D=Fase 2 (5), E=Fase 3 (9), F=Fase 4 (5), G=Fase 5 (3), H=Fase 6 (4), I=Fase 7 (4). Cada card tem descrição com critérios de aceite e dependências. O card A5 prevê atualizar os grupos B–I se a matriz do Gate 0 mudar o escopo.
3. **14 specs dos grupos A e B escritas** por time de agentes (escritor especialista por tema) em `docs/superpowers/specs/tarefas/` — formato fixo de 8 seções, 68–114 linhas cada. Verificação estrutural ok; spot checks ok (A4 sem URLs inventadas; B3 define comportamento explícito para evento sem handler — lição do MSG0005; B6 com 7 regras do design §2.3/§2.5). **Revisão adversarial completa ainda pendente** (ver seção 2).
4. **14 comentários postados** nos cards apontando a spec correspondente no repo.
5. Memória do projeto (`project_hub_gate0.md`) atualizada com board + specs.

## 2. ITEM VOLÁTIL — workflow de revisão+anexo em execução

- No momento deste handoff, o workflow `wf_9ee92ac5-7d4` (revisar adversarialmente → corrigir se preciso → anexar `<CODE>-spec.md` em cada card + comentário-resumo) estava **em execução** (sessão `d837263c-...`).
- **Como verificar se terminou:** cada card da tabela abaixo deve ter (a) anexo `<CODE>-spec.md` e (b) comentário começando com "📎 **Spec revisada e anexada**".
- **Se incompleto:** script em `<scratchpad da sessão d837263c>/revisar-anexar-specs.js`; se o Temp tiver sido limpo, recriar o workflow com a tabela da seção 3 — estágios revisar→corrigir→anexar, **SEM estágio de escrita** (os arquivos já existem e não podem ser sobrescritos). **Cuidado:** re-rodar um card já anexado duplica o anexo — filtrar a lista para os cards faltantes.

## 3. Mapa card ↔ spec (fonte da verdade para retomada)

| Código | Card ID | Spec no repo |
|---|---|---|
| A1 | 86e24c1b5 | docs/superpowers/specs/tarefas/A1-g0-qualidade.md |
| A2 | 86e24c1b8 | docs/superpowers/specs/tarefas/A2-matriz-gate0.md |
| A3 | 86e24c1bc | docs/superpowers/specs/tarefas/A3-decisoes-ratificacao.md |
| A4 | 86e24c1bv | docs/superpowers/specs/tarefas/A4-prerequisitos-sefaz-se.md |
| A5 | 86e24c1by | docs/superpowers/specs/tarefas/A5-ajuste-plano-pos-gate.md |
| B1 | 86e24c1em | docs/superpowers/specs/tarefas/B1-estrutura-solution.md |
| B2 | 86e24c1fy | docs/superpowers/specs/tarefas/B2-tenant-context.md |
| B3 | 86e24c1gv | docs/superpowers/specs/tarefas/B3-outbox-inbox.md |
| B4 | 86e24c1hy | docs/superpowers/specs/tarefas/B4-idempotency-key.md |
| B5 | 86e24c1jh | docs/superpowers/specs/tarefas/B5-auditoria-append-only.md |
| B6 | 86e24c1jz | docs/superpowers/specs/tarefas/B6-netarchtest.md |
| B7 | 86e24c1k6 | docs/superpowers/specs/tarefas/B7-observabilidade-fundacional.md |
| B8 | 86e24c1kf | docs/superpowers/specs/tarefas/B8-provisionamento-aws.md |
| B9 | 86e24c1kp | docs/superpowers/specs/tarefas/B9-ci-pipeline.md |

## 4. Working tree

- Sessão anterior **commitada** até `a2d2743` (design + plano-mestre corrigidos, decisions.md, reviews, handoffs, fix do GlobalExceptionHandlerTests).
- Untracked NOVO desta sessão: `docs/superpowers/specs/tarefas/` (14 specs + este handoff quando salvo). Commitar só se o dono pedir.
- Untracked legado (já estava assim): `docs/implementation-plan.md`, `docs/specs/fase-*.md`, `docs/superpowers/plans/2026-05-*.md`.

## 5. Estado do Gate 0 (inalterado nesta sessão)

- 4/5 relatórios prontos; falta **g0-qualidade** (= card **A1**, spec pronta), depois **matriz final** (= A2) e **ratificação das D-2026-07-01-\*** (= A3). A4 (pré-requisitos SEFAZ-SE) corre em paralelo desde já. Sequência de execução = ordem dos prefixos no board.
- Decisões fixadas (seção 2 do handoff de 2026-07-01): **não reabrir** sem o dono.

## 6. Regras de processo vigentes

- Fase de gate: **só-leitura em código de produção**; bloqueios são apresentados ao dono com opções.
- Parar sessão ao atingir ~70% de contexto (regra do dono).
- Mover cards no ClickUp só com ok do dono.
