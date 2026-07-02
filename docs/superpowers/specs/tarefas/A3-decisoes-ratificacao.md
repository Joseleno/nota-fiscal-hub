# Spec A3 — Ratificar decisões e registrar estratégia em docs/decisions.md
> Card: https://app.clickup.com/t/86e24c1bc | Grupo A | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Formalizar a governança de decisões na saída do Gate 0: (a) ratificar com o dono do produto as decisões D-2026-07-01-01..10 já aplicadas nos documentos; (b) resolver ou atribuir dono+prazo às 5 decisões em aberto; (c) registrar a decisão de estratégia (evoluir repo in-place vs solution nova), derivada da matriz APROVEITAR/ADAPTAR/REESCREVER da tarefa A2 — tudo em `docs/decisions.md` §0, com formato de ratificação rastreável (data, decisor, status).

## Contexto e referências
- `docs/decisions.md` §0 (linhas 9–28) — tabela D-2026-07-01-01..10 + parágrafo de decisões em aberto. Seções 1–8 são legado histórico do VisuFiscalHub; onde conflitarem, o design prevalece (nota do §0).
- `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — Gate 0 exige: "Decidir estratégia: evoluir o repo atual in-place vs. re-estruturar solution — decisão registrada em docs/decisions.md" e "Registrar as decisões pendentes... e ratificar as D-2026-07-01-01..10". Critério de saída do Gate 0: matriz aprovada + decisões registradas. Global Constraints valem para toda spec (nenhuma é violada aqui: tarefa 100% documental).
- `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` §1.1/§2.4 — decisões de produto fixadas (não reabrir).
- `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` — seções 2 (decisões fixadas), 3 (matriz preliminar: solution nova + porte cirúrgico do Motor), 6 (sequência: g0-qualidade → matriz final + ratificação → registro em decisions.md).

### Decisões a ratificar (títulos — texto integral em `docs/decisions.md` §0)
1. **D-2026-07-01-01** — QR Code NT 2025.001: v3 obrigatório em contingência, v2 no online; `GerarQrCode` na custódia.
2. **D-2026-07-01-02** — Contingência regera o documento (`tpEmis=9`, nova chave, reassinatura); consulta da chave original; transmissão ≤ 24h.
3. **D-2026-07-01-03** — Estado `RejeitadaAposContingencia` + webhook próprio; `Cancelada` só a partir de `Autorizada`.
4. **D-2026-07-01-04** — Numeração: `ContadorNumeracao` transacional mantido, transação curta + pool de rejeitadas + inutilização de buracos.
5. **D-2026-07-01-05** — 202 de contingência com dados estruturados de impressão; links do 201 como endpoints do hub.
6. **D-2026-07-01-06** — Cota excedida no MVP sempre emite com `avisos`; `ContaSuspensa` = `403 conta_suspensa`.
7. **D-2026-07-01-07** — Topologia: um ambiente produtivo; "homologação" = empresas `tpAmb=2`.
8. **D-2026-07-01-08** — Encerramento de conta: somente-leitura 5 anos, destruição auditada de A1/CSC, exportação em massa fora do MVP.
9. **D-2026-07-01-09** — `tpAmb` é o ambiente corrente (escalar) da empresa; CSC/séries/contadores por ambiente.
10. **D-2026-07-01-10** — Schema `leitura` pertence à Emissão; entrega de webhooks é de Contas & Planos, via inbox no Worker.

### Decisões em aberto (resolver OU atribuir dono + prazo/fase-limite)
| # | Decisão em aberto | Fase-limite (do §0) |
|---|---|---|
| P1 | Destino da solution legada (arquivar/porte cirúrgico/manter) | Saída do Gate 0 (acoplada à estratégia) |
| P2 | Stack do Portal/Backoffice | Antes de detalhar a Fase 6 |
| P3 | Catálogo de planos — dimensões de limite e ciclo | Antes da Fase 1 |
| P4 | E-mail transacional no MVP (persona "farmácia sem TI") | Fase 6/7 |
| P5 | Adoção do QR v3 também no online | Fase 3 |

### Decisão de estratégia (nova — sai da matriz A2)
**D-ESTRATÉGIA:** evoluir o repo atual in-place **vs** re-estruturar em solution nova. Insumo: matriz final APROVEITAR/ADAPTAR/REESCREVER (A2) + parecer g0-qualidade. A matriz preliminar (handoff §3) aponta solution nova com porte cirúrgico do Motor — apresentar como recomendação, não como fato consumado.

## Escopo (dentro / fora)
**Dentro:**
- Definir e aplicar o formato de ratificação em `docs/decisions.md` §0 (campos: Status, Data, Decisor).
- Conduzir a sessão de ratificação com o dono do produto (D-01..10 + D-ESTRATÉGIA + P1..P5).
- Registrar o resultado: cada decisão com status final; abertas não resolvidas ganham dono + prazo/fase-limite explícitos.
- Registrar a decisão de estratégia como nova entrada numerada (ex.: `D-2026-07-02-11`) com racional citando a matriz A2.

**Fora:**
- Reabrir decisões fixadas (handoff §2 e design §1.1) — se o dono quiser reverter uma D-*, isso é REVOGAÇÃO registrada, não reabertura silenciosa nesta tarefa.
- Qualquer alteração em código de produção ou testes (regra SÓ-LEITURA do gate).
- Editar design/plano-mestre (a maior parte já foi aplicada; ajuste residual é o passo 4 da sequência do handoff §6, fora desta spec).
- Produzir a matriz do Gate 0 (é a A2, dependência desta).

## Abordagem / Passos
| Passo | Ação | Responsável | Entrada → Saída |
|---|---|---|---|
| 1 | Adicionar ao §0 as colunas/bloco de ratificação: `Status` (Proposta / Ratificada / Ratificada-com-ajuste / Revogada), `Data`, `Decisor` — todas as D-01..10 iniciam como `Proposta` | Agente | decisions.md §0 → §0 com campos de governança |
| 2 | Preparar o pacote de ratificação: 1 parágrafo por decisão (título + racional + impacto se revogada) + recomendação de estratégia com base na matriz A2 e no g0-qualidade | Agente | matriz A2 + decisions.md → resumo executivo apresentado ao dono |
| 3 | Sessão de ratificação: dono decide item a item (D-01..10: ratifica/ajusta/revoga; D-ESTRATÉGIA: in-place ou solution nova; P1..P5: decide agora OU nomeia dono+prazo) | Dono do produto (decisor); agente registra | resumo → veredictos |
| 4 | Registrar veredictos no §0: atualizar Status/Data/Decisor das D-01..10; criar entrada numerada para D-ESTRATÉGIA (com P1 embutida ou separada); converter o parágrafo de "decisões em aberto" em tabela com colunas `ID | Decisão | Dono | Prazo/Fase-limite | Status` | Agente | veredictos → decisions.md atualizado |
| 5 | Se alguma D-* for `Ratificada-com-ajuste` ou `Revogada`: registrar no §0 a lista de documentos impactados (design/plano-mestre) como pendência nomeada com dono — sem editá-los nesta tarefa | Agente | veredicto divergente → pendência rastreada |
| 6 | Verificação final contra os critérios de aceite abaixo + atualizar o handoff/ClickUp com o resultado | Agente | decisions.md → checklist de aceite |

**Regra de conflito:** se a sessão de ratificação não ocorrer (dono indisponível), a tarefa NÃO é concluída por decisão unilateral do agente — permanece bloqueada com o pacote do passo 2 pronto. Nenhum agente ratifica em nome do dono.

## Critérios de aceite (verificáveis)
1. `docs/decisions.md` §0 contém as 10 entradas D-2026-07-01-01..10, cada uma com `Status ∈ {Ratificada, Ratificada-com-ajuste, Revogada}`, `Data` e `Decisor` preenchidos — zero entradas restantes em `Proposta`.
2. Existe entrada numerada da decisão de estratégia (in-place vs solution nova) com racional referenciando explicitamente a matriz A2 (path do artefato) e status `Ratificada`.
3. As 5 decisões em aberto (P1..P5) aparecem em tabela própria no §0; cada linha tem `Status ∈ {Decidida, Pendente}`; toda linha `Pendente` tem `Dono` (pessoa nomeada) e `Prazo/Fase-limite` preenchidos — **nenhuma decisão pendente sem dono e prazo** (critério de pronto da tarefa).
4. Nenhuma decisão fixada (design §1.1 / handoff §2) foi alterada; revogações, se houver, estão marcadas como tal com racional e pendência de propagação (passo 5).
5. `git status`: somente `docs/decisions.md` (e artefatos de tracking do gate, se o dono pedir) modificados — nenhum arquivo em `src/` ou `tests/` tocado.
6. O critério de saída do Gate 0 referente a decisões (plano-mestre, checklist do Gate 0, itens 3 e 6) pode ser marcado como concluído citando esta spec.

## Plano de testes / Evidências
- **E1 (estrutura):** grep em `docs/decisions.md` por `D-2026-07-01-` retorna 10 entradas com os 3 campos de ratificação; grep por `Proposta` no §0 retorna 0 após o passo 4.
- **E2 (estratégia):** a entrada de estratégia cita o path da matriz A2 e o veredicto (in-place | solution nova).
- **E3 (abertas):** tabela P1..P5 sem célula vazia em Dono/Prazo nas linhas `Pendente`.
- **E4 (só-leitura):** saída de `git status` anexada como evidência no card mostrando ausência de mudanças fora de `docs/`.
- **E5 (decisor):** registro da sessão (data + nome do dono) no próprio §0 — evidência de que a ratificação não foi sintetizada por agente.

## Dependências
- **A2 (matriz Gate 0)** — bloqueante para a D-ESTRATÉGIA e para P1; a ratificação das D-01..10 e a preparação (passos 1–2) podem andar em paralelo.
- **g0-qualidade** (5º relatório do Gate 0) — insumo do parecer APROVEITAR/ADAPTAR/REESCREVER da suíte; se atrasar, a recomendação de estratégia declara a lacuna.
- **Disponibilidade do dono do produto** — passo 3 é síncrono e insubstituível.

## Riscos e pontos de atenção
- **Ratificação de fachada:** apresentar as 10 decisões em bloco induz "aprova tudo". Mitigação: passo 2 exige impacto-se-revogada por item; decisões com custo de reversão alto (D-02, D-04, D-07) destacadas.
- **Estratégia decidida antes da matriz final:** a preliminar (solution nova) pode ancorar o dono. Mitigação: rotular como preliminar até A2 concluir; a entrada só é registrada com a matriz final citada.
- **Decisão revogada sem propagação:** design/plano-mestre ficariam inconsistentes com decisions.md. Mitigação: passo 5 cria pendência nomeada; aceite 4 verifica.
- **Escopo furtivo:** tentação de "já corrigir" documentos ou código durante a sessão. Mitigação: regra SÓ-LEITURA (memória do projeto) + aceite 5.
- **decisions.md ainda untracked:** o arquivo não está commitado (handoff §5); perda local destruiria o registro. Mitigação: ao final, propor commit ao dono (commitar só se ele pedir, conforme handoff).
