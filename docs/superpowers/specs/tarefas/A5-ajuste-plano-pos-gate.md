# Spec A5 — Ajustar plano-mestre pós-gate e detalhar Fase 0
> Card: https://app.clickup.com/t/86e24c1by | Grupo A | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo

Converter o resultado do Gate 0 em plano executável: (1) atualizar o plano-mestre (`docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`) com a matriz APROVEITAR/ADAPTAR/REESCREVER aprovada e as decisões ratificadas; (2) sincronizar os cards dos grupos B–I no ClickUp (lista Hub, id `901714896084`) com o novo escopo; (3) produzir o plano bite-sized da Fase 0 via skill `superpowers:writing-plans`. Tarefa de **processo/análise**: nenhuma linha de código de produção é alterada (regra SÓ-LEITURA do gate permanece em vigor até a Fase 0 iniciar).

## Contexto e referências

- **Design aprovado:** `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — §2 (5 módulos + kernel, regras de fronteira), §4.4 (testes), §4.5 (deploy). A Fase 0 materializa o kernel transversal do §2.2 ("Cross-cutting kernel") e as regras §2.3.
- **Plano-mestre:** `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — Gate 0 (linhas 27–38), Fase 0 (42–54) e **Global Constraints (linhas 11–23), que toda saída desta tarefa deve preservar textual ou semanticamente**.
- **Decisões:** `docs/decisions.md` §0 — D-2026-07-01-01..10 (ratificação é entrada desta tarefa, via A3) + lista de "decisões em aberto" com fase-limite de registro.
- **Handoff:** `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` — seção 3 (matriz preliminar: solution nova + porte cirúrgico do Motor, 3 bugs fiscais), seção 6 (esta tarefa = passos 4–5 da sequência).
- **Matriz final do Gate 0** (saída de A2) e **ata de ratificação de decisões** (saída de A3): documentos de entrada obrigatórios — sem eles esta tarefa não inicia (ver Dependências).

## Escopo (dentro / fora)

**Dentro:**
- Edição do plano-mestre: reescrever o bloco Gate 0 como "concluído" com link para a matriz; ajustar cada Fase 0–7 conforme o parecer por módulo (fases encolhem onde houver APROVEITAR/ADAPTAR; tarefas de porte nomeadas onde houver REESCREVER com reaproveitamento — ex.: porte do Motor `Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator` + testes, já sinalizado na matriz preliminar).
- Sincronização do board ClickUp (lista `901714896084`, grupos B–I): atualizar descrição dos cards afetados, fechar cards que a matriz tornou desnecessários, criar cards para trabalho novo revelado pelo gate. Grupo A não é tocado (é o grupo desta tarefa).
- Plano bite-sized da Fase 0 em `docs/superpowers/plans/` via skill `superpowers:writing-plans`, cobrindo os 9 itens da Fase 0 do plano-mestre (estrutura da solution, tenant context, outbox/inbox, idempotência, auditoria, NetArchTest, observabilidade fundacional, AWS mínimo, CI).
- Registro em `docs/decisions.md` §0 de qualquer decisão nova que o ajuste exigir (ex.: destino da solution legada, se A3 a ratificou).

**Fora:**
- Qualquer alteração em `src/`, `tests/` ou código de produção (SÓ-LEITURA).
- Reabrir decisões fixadas (handoff §2) ou as D-2026-07-01-* ratificadas.
- Detalhar planos das Fases 1–7 (cada uma terá seu plano quando chegar a vez).
- Produzir a matriz (A2) ou conduzir a ratificação (A3) — são entradas, não trabalho desta tarefa.
- Commits/push — working tree segue não commitada salvo pedido explícito do dono.

## Abordagem / Passos

| # | Passo | Responsável | Saída |
|---|---|---|---|
| 1 | Validar entradas: matriz de A2 aprovada pelo dono + decisões de A3 ratificadas (incl. destino da solution legada, decisão obrigatória do Gate 0) | Agente (verifica), Dono (aprova) | Checklist de entradas OK |
| 2 | Mapear matriz → fases: para cada módulo do design, anotar impacto por fase (encolhe / mantém / ganha tarefa de porte), citando o parecer que justifica | Agente | Tabela impacto módulo×fase (rascunho na sessão) |
| 3 | Editar o plano-mestre: Gate 0 marcado concluído com links; fases ajustadas; Global Constraints intactas; seção "Dependências e sequência" revisada se a ordem mudou | Agente | Plano-mestre atualizado |
| 4 | Revisão do plano-mestre pelo dono (diff apresentado por fase, com justificativa por mudança) | Dono | Aprovação ou ajustes |
| 5 | Sincronizar ClickUp: listar cards B–I (`clickup_get_workspace_hierarchy`/`clickup_filter_tasks` na lista `901714896084`), propor plano de mudança (atualizar/fechar/criar) ao dono ANTES de executar, depois aplicar | Agente (propõe/executa), Dono (aprova plano de mudança) | Board coerente com o plano-mestre |
| 6 | Detalhar Fase 0 com `superpowers:writing-plans`: tarefas bite-sized com teste de aceite cada, TDD, incorporando o que a matriz mandou aproveitar/portar para o kernel | Agente | `docs/superpowers/plans/2026-07-0X-fase-00-fundacao-kernel.md` |
| 7 | Revisão final do dono: plano da Fase 0 aprovado + conferência board×plano | Dono | Critério de pronto atingido |

Regras transversais: em qualquer bloqueio (ex.: card ClickUp com escopo ambíguo, conflito matriz×decisão), apresentar opções ao dono — nunca resolver por conta própria (regra de processo do handoff §1.5).

## Critérios de aceite (verificáveis)

1. Plano-mestre atualizado: bloco Gate 0 com todos os checkboxes marcados e link para a matriz final; nenhuma Global Constraint removida ou enfraquecida (diff das linhas 11–23 vazio ou apenas aditivo).
2. Cada fase alterada no plano-mestre referencia o parecer da matriz que motivou a mudança (rastreabilidade: fase → módulo → parecer).
3. Toda decisão de A3 com efeito em fase (ex.: D-01 QR v3, D-04 numeração, D-07 topologia, destino da solution) está refletida no texto da fase correspondente — verificação: para cada D-* ratificada, apontar a linha do plano que a incorpora.
4. Board ClickUp: 100% dos cards abertos dos grupos B–I correspondem a itens do plano-mestre atualizado, e todo entregável de fase do plano-mestre tem card; zero card órfão ou faltante (conferência card-a-card registrada em comentário no card A5).
5. Plano da Fase 0 existe em `docs/superpowers/plans/`, segue o template da skill writing-plans (tarefas pequenas, cada uma com teste de aceite/verificação), cobre os 9 itens da Fase 0 e tem aprovação explícita do dono registrada (comentário no card ou no próprio arquivo).
6. `git status` não mostra modificação em `src/` nem `tests/` (SÓ-LEITURA respeitada).
7. Decisões novas surgidas no ajuste registradas em `docs/decisions.md` §0 com ID no padrão `D-AAAA-MM-DD-NN`.

## Plano de testes / Evidências

- **Evidência 1:** diff do plano-mestre (por fase) anexado/comentado no card A5, com a justificativa de cada mudança.
- **Evidência 2:** snapshot antes/depois da lista ClickUp `901714896084` (ids, títulos, status) + tabela de reconciliação card↔item do plano.
- **Evidência 3:** path do plano da Fase 0 + registro da aprovação do dono.
- **Verificação mecânica:** `git diff docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` mostra Global Constraints intactas; `git status --short src tests` vazio.
- **Verificação de completude:** leitura cruzada final — para cada linha da matriz (módulo → parecer), existe reflexo no plano-mestre; para cada fase, existe card de grupo correspondente.

## Dependências

- **A2 (bloqueante):** matriz final do Gate 0 aprovada pelo dono — sem ela, passos 2+ não iniciam.
- **A3 (bloqueante):** D-2026-07-01-01..10 ratificadas + decisão do destino da solution legada registrada em `docs/decisions.md` §0.
- **MCP ClickUp operacional** com acesso de escrita à lista `901714896084` (passo 5).
- **Skill `superpowers:writing-plans`** disponível (passo 6).
- Decisões em aberto NÃO bloqueantes aqui (stack Portal/Backoffice → antes da Fase 6; catálogo de planos → antes da Fase 1; QR v3 online → Fase 3): apenas conferir que continuam listadas em `docs/decisions.md` §0 com sua fase-limite.

## Riscos e pontos de atenção

| Risco | Mitigação |
|---|---|
| Ajuste "encolhe demais" — reaproveitar código legado que viola fronteiras do design (§2.3) | Todo APROVEITAR/ADAPTAR entra no plano com a condição do parecer anexada; na dúvida, o design prevalece (regra do `docs/decisions.md` §0) |
| Perda de constraint no reword das fases | Critério de aceite 1: diff das Global Constraints apenas aditivo; revisor adversarial confere item a item |
| Board dessincronizado por edição parcial (sessão interrompida no passo 5) | Executar mudanças ClickUp em ordem: criar → atualizar → fechar; registrar progresso em comentário no card A5 para retomada idempotente |
| Fechar card ClickUp com histórico útil | Fechar (status), nunca deletar; comentário de encerramento aponta o item do plano-mestre que o substitui |
| Plano da Fase 0 herdar os 3 bugs fiscais via porte de código | Bugs #1–#3 pertencem à Fase 3 como tarefas nomeadas (já no plano-mestre, linha 85); o plano da Fase 0 não porta código do Motor — apenas kernel |
| Deriva de escopo: detalhar Fases 1+ "já que estamos aqui" | Fora de escopo explícito; qualquer antecipação exige ok do dono |
| Limite de contexto da sessão (regra 70% da memória do projeto) | Passos 3, 5 e 6 são retomáveis de forma independente; encerrar sessão entre eles se necessário, com handoff curto |
