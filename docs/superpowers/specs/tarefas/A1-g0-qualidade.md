# Spec A1 — Concluir g0-qualidade (análise da suíte de testes)
> Card: https://app.clickup.com/t/86e24c1b5 | Grupo A | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo

Produzir o 5º e último relatório do Gate 0 (**g0-qualidade**): parecer fundamentado sobre a qualidade da suíte de testes do código legado (VisuFiscalHub) e sobre a confiabilidade do código que ela cobre, com (a) inventário de silent failures em `src/` com `arquivo:linha`, (b) diagnóstico dos 5 eventos MSG0005 sem handler, (c) matriz de cobertura real vs design §4.4, e (d) nota 0-10 por camada + parecer APROVEITAR/ADAPTAR/REESCREVER da suíte — insumo direto da matriz final do Gate 0.

## Contexto e referências

- **Handoff §4** (base desta tarefa): `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` — dados já apurados: suíte verde **761/761 em 16s**; 53 arquivos, 363 `[Fact]`+`[Theory]` (Domain 89, Infrastructure 180, Integration 60, Application 24, Api 10); avisos NU1903 ×2, CS0618, ASPDEPR005 e **MSG0005 ×5**.
- **Design §4.4** (tabela de cobertura-alvo): `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — usar a **versão atual** (já revisada em 2026-07-01).
- **Plano-mestre** (Gate 0 + Global Constraints): `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`.
- **Decisões**: `docs/decisions.md` §0 (D-2026-07-01-01..10; o design prevalece sobre o legado).
- **Relatórios g0 anteriores** (aderência, fiscal, escalabilidade, segurança): consolidados nas seções 3–5 do handoff `docs/superpowers/handoffs/2026-07-02-gate0-handoff.md` — não repetir achados deles; referenciá-los.
- Código sob análise: `src/VisuFiscalHub.{Api,Application,Domain,Infrastructure}` e `tests/`.

## Escopo (dentro / fora)

**Dentro (análise SÓ-LEITURA):**
1. Varredura de silent failures em `src/` (padrões na seção Abordagem).
2. Investigação dos 5 eventos MSG0005 sem handler registrado: `ClienteAppWebhookSecretRotadoEvent`, `DocumentoFiscalCanceladoEvent`, `DocumentoFiscalFalhouEvent`, `DocumentoFiscalRejeitadoEvent`, `TenantProvisionadoEvent` — rastrear o caminho no `OutboxRelayJob` (`src/VisuFiscalHub.Infrastructure/Jobs/OutboxRelayJob.cs`) e no registro de handlers (`DependencyInjection.cs`): a mensagem é descartada, fica presa como pendente, marca erro, ou é marcada processada sem efeito?
3. Cobertura real vs design §4.4 — **todos** os itens da tabela.
4. Nota 0-10 por camada de teste + parecer APROVEITAR/ADAPTAR/REESCREVER da suíte.
5. Relatório persistido em `docs/superpowers/reviews/`.

**Fora:**
- Qualquer modificação em código de produção ou de teste (regra SÓ-LEITURA do gate — memória do projeto e handoff §1.5). Bugs encontrados são **registrados**, nunca corrigidos.
- Reexecutar as 4 revisões já entregues (aderência, fiscal, escalabilidade, segurança).
- Medir cobertura por instrumentação (coverlet etc.) — a análise é estrutural/por leitura; rodar a suíte (`dotnet test`) é permitido por ser não-destrutivo.
- Decisão da matriz final do Gate 0 (tarefa seguinte; esta spec apenas a alimenta).

## Abordagem / Passos

Método sugerido: **multi-agente com verificação adversarial por achado** (padrão usado na revisão de 2026-07-01). Se subagentes indisponíveis, executar inline na mesma ordem. Responsáveis: **agente** executa passos 1–6; **dono do produto** recebe o relatório e ratifica no passo 7.

1. **Baseline** (agente): `dotnet test` para confirmar 761/761 verdes e capturar os avisos de build (MSG0005, NU1903, CS0618, ASPDEPR005) como evidência datada.
2. **Varredura de silent failures** (agente/subagente 1) — grep + leitura de contexto em `src/`, um achado por linha com `arquivo:linha`, trecho e severidade (crítico/alto/médio):
   - `catch` genérico ou vazio que engole exceção (sem rethrow e sem sinalizar falha ao chamador);
   - `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` (deadlock/AggregateException mascarada);
   - `async void` fora de event handler de UI;
   - `ContinueWith` sem observar `Task.Exception`/`IsFaulted`; tasks fire-and-forget não aguardadas;
   - jobs/handlers que capturam, logam e **seguem o lote** sem marcar a mensagem como falha (atenção especial ao `OutboxRelayJob` e demais `Jobs/*`).
   - Falso-positivo esperado: catch que loga e **rethrow**, ou que converte em resultado de erro explícito, não é silent failure — descartar com justificativa.
3. **MSG0005** (agente/subagente 2): para cada um dos 5 eventos, documentar publicador (agregado/handler que o levanta), o que o OutboxRelay faz na ausência de handler, e a consequência de negócio (ex.: cancelamento sem webhook ao ClienteApp; tenant provisionado sem efeito colateral). Classificar cada um: inofensivo / funcionalidade silenciosamente ausente / bug latente.
4. **Cobertura vs §4.4** (agente/subagente 3): tabela item a item da versão atual do §4.4 com status COBERTO / PARCIAL / AUSENTE / **N/A-herdado** e evidência (arquivo de teste ou ausência). Itens mínimos: máquina de estados completa (incl. `Denegada`/`EmContingencia`/`RejeitadaAposContingencia`); numeração sob concorrência (incl. pool de rejeitadas); prazos (cancelamento 30 min, contingência 24h, inutilização dia 10); classificação pelo `cStat` do `protNFe` (não do lote); golden files XML validados contra XSD, incl. posicionamento de `<Signature>` irmã de `infNFe`; QR v2/v3; testes de arquitetura (NetArchTest); integração com eventos duplicados/fora de ordem; isolamento de tenant; E2E/smoke homologação. **Contingência = N/A herdado** (não existe no legado — registrar assim, não como falha da suíte). Cruzar com os 3 bugs fiscais críticos já conhecidos (handoff §3): a suíte deixou passar `cStat` do nível errado e `<Signature>` dentro de `infNFe` — isso é evidência de lacuna de golden files e deve pesar na nota.
5. **Verificação adversarial** (revisor): cada achado dos passos 2–4 é reverificado contra o código; achado refutado sai do relatório com anotação. Nenhum achado entra sem `arquivo:linha` conferido.
6. **Notas e parecer** (agente): nota 0-10 por camada (Domain 89 testes, Application 24, Infrastructure 180, Integration 60, Api 10) com critérios explícitos (assertividade dos asserts, isolamento, cobertura dos caminhos de erro, aderência ao §4.4, flakiness aparente) e parecer único da suíte — APROVEITAR / ADAPTAR / REESCREVER — coerente com a matriz preliminar do Gate 0 (porte cirúrgico do Motor + testes). Escrever o relatório em `docs/superpowers/reviews/2026-07-02-g0-qualidade.md`.
7. **Entrega** (dono do produto): relatório apresentado como 5º parecer para compor a matriz final do Gate 0. Não commitar sem pedido do dono.

## Critérios de aceite (verificáveis)

- [ ] Relatório existe em `docs/superpowers/reviews/2026-07-02-g0-qualidade.md` (ou nome equivalente datado) e é o único artefato novo — `git status` não mostra nenhuma modificação em `src/` nem em `tests/`.
- [ ] Todo silent failure listado tem `arquivo:linha` + trecho + severidade; seção explícita "nenhum encontrado" por padrão pesquisado quando for o caso.
- [ ] Os 5 eventos MSG0005 têm, cada um, publicador, comportamento do OutboxRelay e consequência de negócio classificada.
- [ ] Tabela de cobertura contém **todas** as linhas do §4.4 atual, cada uma com status e evidência; contingência marcada N/A-herdado.
- [ ] 5 notas 0-10 (uma por camada) com justificativa de ≥ 2 frases cada, e parecer final APROVEITAR/ADAPTAR/REESCREVER com racional.
- [ ] Achados passaram por verificação adversarial (relatório indica quantos brutos → confirmados → refutados).
- [ ] Relatório referencia os 4 pareceres g0 anteriores sem duplicá-los.

## Plano de testes / Evidências

Tarefa de análise — não há testes novos. Evidências exigidas no relatório:
- Saída resumida do `dotnet test` (contagem verde + duração) e do build com os avisos citados.
- Comandos de varredura usados (padrões grep) para reprodutibilidade.
- Para cada item COBERTO da tabela §4.4: caminho do arquivo de teste que o comprova.
- Contagem de achados por etapa da verificação adversarial.

## Dependências

- Handoff §4 (dados apurados) e os 4 relatórios g0 anteriores — já disponíveis.
- Suíte verde 761/761 (fix do `GlobalExceptionHandlerTests.cs` já aplicado na working tree; **não commitado** — não sobrescrever).
- Subagentes disponíveis (limite de sessão do handoff §7 já expirado); fallback inline previsto.
- Bloqueia: matriz final do Gate 0 e ratificação das D-2026-07-01-01..10 (sequência do handoff §6).

## Riscos e pontos de atenção

- **Tentação de corrigir bugs encontrados** — violaria a regra SÓ-LEITURA do gate; registrar e seguir (regra já registrada na memória do projeto após correção do dono).
- **Suíte verde ≠ suíte boa**: 761 verdes conviveram com 3 bugs fiscais críticos; a análise deve avaliar o que os testes *afirmam*, não só se passam.
- **§4.4 descreve o produto novo**: julgar o legado por ele exige o rótulo N/A-herdado para o que o design introduziu (contingência, QR v3, `RejeitadaAposContingencia`) — sem isso a nota fica injustamente baixa e a matriz distorce.
- **Falsos positivos de silent failure** (catch-log-rethrow, `.Result` em código de teste) inflariam o relatório — daí a verificação adversarial obrigatória.
- **Contexto de sessão**: trabalho fatiável por subagente (passos 2–4 são independentes); se a sessão atingir ~70% de contexto, encerrar com handoff parcial em vez de degradar a análise.
