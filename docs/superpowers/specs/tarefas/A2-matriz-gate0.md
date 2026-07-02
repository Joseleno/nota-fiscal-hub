# Spec A2 — Matriz final do Gate 0 (APROVEITAR/ADAPTAR/REESCREVER)
> Card: https://app.clickup.com/t/86e24c1b8 | Grupo A | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Consolidar os 5 pareceres do Gate 0 (aderência, fiscal, escalabilidade, segurança, qualidade) na **matriz final por módulo do design** — APROVEITAR / ADAPTAR / REESCREVER, com justificativa rastreável e ações de porte — e conduzi-la à **aprovação formal do dono do produto**, que é o critério de saída do Gate 0 no plano-mestre. Tarefa de PROCESSO/ANÁLISE: nenhum código de produção é alterado (regra SÓ-LEITURA do gate).

## Contexto e referências
- Design aprovado: `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` (módulos §2.2, fronteiras §2.3, dependências §2.5).
- Plano-mestre: `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — seção "Gate 0" define os checkboxes e o critério de saída; a seção "Global Constraints" vincula TODA classificação (uma peça de código que viole constraint estrutural não pode ser APROVEITAR).
- Entradas (4 relatórios já consolidados): `docs/superpowers/handoffs/2026-07-02-gate0-handoff.md` §3 (3.1 aderência, 3.2 fiscal, 3.3 escalabilidade, 3.4 segurança), §4 (dados parciais de qualidade) e §5 (matriz preliminar).
- Entrada pendente: relatório **g0-qualidade** produzido pela tarefa A1 (spec `docs/superpowers/specs/tarefas/A1-*.md`) — nota por camada, silent failures, cobertura vs §4.4 do design.
- Decisões: `docs/decisions.md` §0 — D-2026-07-01-01..10 aplicadas nos documentos, **pendentes de ratificação** nesta saída de gate; decisões em aberto listadas lá (destino da solution legada, stack Portal/Backoffice, catálogo de planos, QR v3 online, e-mail transacional).
- Processo/estado: `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` (§3, §6).

## Escopo (dentro / fora)
**Dentro**
1. Matriz final cobrindo TODAS as linhas abaixo (unidade = componente do design, não "área" solta): Contas & Planos; Empresas & Certificados — **duas linhas**: estrutura/agregados e custódia A1/CSC; Emissão — **duas linhas**: núcleo (numeração, máquina de estados, síncrono/contingência) e casca (endpoints/handlers); Motor NFCe/NFe (XML, transporte SEFAZ, QR Code, classificação cStat); Documentos; Kernel (tenant context, idempotência, auditoria, NetArchTest); Outbox/Inbox; Deployables (API/Worker); Suíte de testes (parecer vem de A1); NFSe ABRASF (registro "NÃO PORTAR agora — fase 2").
2. Aplicação dos critérios objetivos (abaixo) a cada linha, revalidando a matriz preliminar (§5 do handoff) contra o relatório A1.
3. Documento de decisão para o dono + registro do resultado em `docs/decisions.md` §0 + ajuste residual do plano-mestre (após aprovação).
**Fora**
- Qualquer edição em código de produção ou testes (incl. os 3 bugs fiscais — viram itens da matriz, já nomeados na Fase 3 do plano-mestre).
- Reabrir decisões fixadas (handoff §2: motor próprio, roadmap, 5 módulos + kernel, 2 deployables etc.).
- Detalhamento da Fase 0 (só começa após a aprovação desta matriz).
- Novas análises de código além do necessário para desempatar pareceres divergentes (ver Passo 3).

## Abordagem / Passos
**Responsáveis:** agente = consolida, propõe e evidencia; **dono do produto** = ratifica, decide desempates de produto e aprova a saída do gate.

1. **Formato da matriz (fixo).** Tabela com colunas: `Componente do design` | `Parecer` (APROVEITAR/ADAPTAR/REESCREVER) | `Justificativa` (1–3 frases citando relatório-fonte e, quando houver, `arquivo:linha`) | `Ações de porte` (o que mover/corrigir, com caminho de origem no repo legado e módulo de destino) | `Esforço` (P/M/G) | `Fase destino` (0–7). Linhas com parecer dividido (ex.: Empresas & Certificados) são desmembradas — proibido parecer híbrido numa linha só.
2. **Critérios objetivos de classificação** (aplicar nesta ordem; o primeiro que falhar rebaixa o parecer):
   - **APROVEITAR** exige as 4 condições: (a) nenhum achado CRÍTICO/ALTO dos 5 relatórios incide no componente; (b) não viola nenhuma Global Constraint nem regra de fronteira §2.3 (tenancy, custódia, outbox por módulo, contratos, tpAmb); (c) o porte é mecânico — mover para o módulo/projeto de destino sem mudar contrato nem comportamento; (d) há testes existentes que continuam válidos após o porte.
   - **ADAPTAR** exige as 3 condições: (a) o núcleo (algoritmo/regra fiscal/mecânica) está correto e validado por pelo menos um relatório; (b) os ajustes são enumeráveis com correção conhecida (ex.: bugs #1–#3 do g0-fiscal, `cIdToken` no QR, inbox no outbox); (c) custo estimado do ajuste < custo de reescrever do zero, registrado como comparação explícita na justificativa.
   - **REESCREVER** quando qualquer uma: (a) o componente não existe (Documentos/S3, contingência, inutilização); (b) viola constraint estrutural de forma não-localizada (sem filtro de tenant, chave privada trafegando entre camadas, fluxo 100% assíncrono vs síncrono do design, agregado god entity); (c) ajuste ≥ reescrita; (d) dois ou mais relatórios o classificam REESCREVER e nenhum evidencia núcleo aproveitável.
   - **Desempate:** divergência entre relatórios → prevalece o parecer mais conservador (mais próximo de REESCREVER), salvo contra-evidência concreta (código+teste apontados) registrada na justificativa; empate que envolva decisão de produto (ex.: numeração sequence × `ContadorNumeracao` — já resolvido por D-2026-07-01-04) é referenciado à decisão, não re-analisado.
3. **Revalidar a matriz preliminar** (handoff §5) linha a linha contra o relatório A1: qualidade pode rebaixar parecer (ex.: componente "APROVEITAR" cujos testes têm silent failures cai para ADAPTAR) mas não promove sem evidência. Registrar em coluna extra `Δ vs preliminar` toda mudança e o porquê. Pontos já convergidos que a matriz deve refletir sem reabrir: **estratégia = solution nova (Fase 0) + porte cirúrgico do Motor** (`Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator` + testes correlatos); os **3 bugs fiscais críticos** (cStat do `retEnviNFe` em vez do `protNFe`; `<Signature>` dentro de `infNFe`; idem no evento de cancelamento) entram como ações de porte obrigatórias do Motor — o parecer ADAPTAR do Motor está condicionado a essas correções na Fase 3.
4. **Produzir o pacote de decisão** em `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`: (a) matriz final; (b) resumo de 1 página por decisão pedida ao dono; (c) lista de ratificações: D-2026-07-01-01..10 (sim/não/ajustar cada uma) + decisões em aberto exigidas na saída do gate (mínimo: destino da solution legada); (d) impacto no plano-mestre (fases que encolhem/crescem).
5. **Sessão de aprovação com o dono:** apresentar o pacote; coletar aprovação, vetos ou pedidos de reanálise por linha. Sem aprovação integral, o gate NÃO fecha — reanálises voltam ao Passo 3.
6. **Registrar o resultado:** decisão de estratégia + ratificações + destino da solution em `docs/decisions.md` §0 (novas entradas D-YYYY-MM-DD-*); ajustar o plano-mestre (checkboxes do Gate 0 marcados; ajustes de fase decorrentes); atualizar o handoff ativo apontando o gate como FECHADO.

## Critérios de aceite (verificáveis)
1. Existe `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md` com a matriz cobrindo as 10+ linhas do Escopo, todas com as 6 colunas preenchidas e coluna `Δ vs preliminar`.
2. Cada parecer cita ao menos um relatório-fonte (aderência/fiscal/escalabilidade/segurança/qualidade-A1); nenhum parecer híbrido em linha única; toda linha ADAPTAR tem a comparação de custo ajuste × reescrita; os 3 bugs fiscais + `cIdToken` aparecem como ações de porte do Motor.
3. Toda divergência com a matriz preliminar do handoff §5 está justificada; ausências de divergência declaradas ("confirma preliminar").
4. O pacote lista as 10 decisões D-2026-07-01-* com status de ratificação individual preenchido pelo dono, e o destino da solution legada decidido.
5. `docs/decisions.md` §0 contém as novas entradas (estratégia, ratificações, destino da solution) com data e racional; plano-mestre com checkboxes do Gate 0 concluídos.
6. Aprovação explícita do dono registrada (frase de aprovação + data no documento da matriz) — sem ela, tarefa não está pronta.
7. `git status` não mostra modificação em `src/` ou `tests/` decorrente desta tarefa (SÓ-LEITURA respeitada).

## Plano de testes / Evidências
- **Checklist de completude:** conferir as linhas da matriz contra §2.2 + cross-cutting kernel do design e contra as 10 áreas da matriz preliminar — nenhuma área do design sem linha.
- **Auditoria de rastreabilidade (amostral):** para 3 linhas sorteadas (1 de cada parecer), verificar que a justificativa confere com o texto do relatório-fonte no handoff §3/§4 ou no relatório A1.
- **Teste do critério:** aplicar os critérios do Passo 2 de trás para frente em 2 linhas (dado o parecer, as condições listadas realmente se sustentam? ex.: Motor ADAPTAR passa em (a)(b)(c)?).
- **Evidências a anexar no card:** link do documento da matriz, diff de `docs/decisions.md`, diff do plano-mestre, transcrição/resumo da aprovação do dono.

## Dependências
- **Bloqueante:** relatório g0-qualidade (tarefa A1) entregue — a matriz não é "final" com 4/5 pareceres. Dados já adiantados: suíte compila, 761/761 verdes, NU1903 ×2, 5 eventos sem handler (MSG0005).
- Handoffs e relatórios citados em Contexto (já no repo); `docs/decisions.md` §0 existente.
- Disponibilidade do dono do produto para a sessão de aprovação (Passo 5).
- Nada desta spec depende de acesso à SEFAZ ou a ambientes AWS.

## Riscos e pontos de atenção
- **A1 rebaixar pareceres do Motor:** se a cobertura real dos testes do Motor for fraca (sem golden files/XSD), o "porte cirúrgico + testes" perde a rede de segurança — mitigar exigindo, na ação de porte, a criação dos golden files na Fase 3 antes de considerar o porte concluído (já previsto no plano-mestre).
- **Viés de ancoragem na matriz preliminar:** o Passo 3 obriga revalidação linha a linha e o desempate conservador existe exatamente para impedir "carimbar" o preliminar.
- **Escopo deslizando para correção de código:** os 3 bugs são tentadores de "já corrigir"; a regra SÓ-LEITURA (memória do projeto + handoff §1.5) proíbe — bloqueios são apresentados ao dono, não corrigidos.
- **Ratificação parcial das D-2026-07-01-*:** se o dono vetar alguma (ex.: D-04 numeração), design/plano-mestre precisam de emenda antes de fechar o gate — prever iteração extra no cronograma.
- **Datas conflitantes dos handoffs:** o arquivo `2026-07-02-gate0-handoff.md` é da sessão ANTERIOR ao `2026-07-01-revisao-docs-e-gate0-handoff.md` (nota de "SUPERADO" no topo); usar o de 07-02 apenas como fonte dos relatórios §3–§5.
- **Working tree com correções não commitadas** (design, plano-mestre, decisions.md, fix de teste): decidir com o dono, na mesma sessão de aprovação, o commit desses artefatos — a matriz referencia as versões corrigidas.
