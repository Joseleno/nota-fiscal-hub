# Handoff — 4 bugs pré-existentes corrigidos + 4 achados adicionais mascarados; CI pronto para virar gate obrigatório assim que o plano do GitHub permitir

**Data:** 2026-07-07
**Como retomar:** ler este arquivo primeiro (é o mais recente, supera `2026-07-06-fase0-codigo-concluida-handoff.md`). Não há bug de produto conhecido bloqueando o CI — o único item pendente é aplicar branch protection quando o repositório sair do plano gratuito do GitHub.

---

## 1. O que esta sessão fez (concluído e commitado na branch `dev`)

Retomou exatamente o item de maior prioridade apontado pelo handoff anterior: corrigir os 4 bugs pré-existentes que impediam `test-integracao` de fechar verde. Trabalho conduzido via `superpowers:systematic-debugging` (Fase 1 — causa-raiz confirmada por execução real contra Postgres via Testcontainers, nunca por suposição) + `superpowers:test-driven-development` (RED→GREEN em cada fix).

### Commit `909d251` — os 4 bugs originais do handoff anterior

1. **Filtro de tenant (Tarefa 2) impedia tradução SQL de qualquer query tenant-scoped.** `TenantFilterEvaluatableExpressionFilter` bloqueava a funcletização da leitura de `ContaId` para preservar o tipo da exceção fail-closed — mas isso também impedia o EF Core de traduzir a query para SQL (nem client-eval funcionava). Removido o filtro customizado (arquivo deletado); restaurado o comportamento padrão do EF Core, que funcletiza normalmente. Efeito colateral aceito e documentado: `TenantNaoResolvidoException` agora chega ao chamador como `InnerException` de `InvalidOperationException`, não mais solta — 3 testes unitários ajustados para refletir isso (nenhum consumidor de produção dependia do tipo exato antes). Também corrigido um segundo bug descoberto na mesma investigação: `BeginSystemScope` não fazia bypass automático do filtro nomeado `"Tenant"` (só `IgnoreQueryFilters` explícito fazia) — agora o filtro é `IsSystemScope || ContaId == e.ContaId`, com curto-circuito.
2. **`AddOutboxInbox<TDbContext>` (Tarefa 3) não deixava `OutboxDispatcher`/`RetentionCleanupService` resolvíveis via `GetRequiredService<T>()`** — só registrava via `AddHostedService<T>()`, que expõe apenas `IHostedService`. Agora ambos são singleton concreto, registrado também como `IHostedService` pela mesma instância. Removido o workaround manual que só existia em `ObservabilityTestFixture` (ficaria redundante).
3. **`AuditoriaTestFixture` (Tarefa 5) chamava `EnsureCreatedAsync` duas vezes contra o mesmo banco físico** (`ModuloProdutorDbContext` primeiro, depois `AuditoriaDbContext`) — a segunda chamada é ignorada pelo EF Core (que decide "criar ou não" pela existência do banco como um todo, não por schema/tabela), deixando `auditoria.registro_auditoria` de fora. Trocado por `GenerateCreateScript()` + `ExecuteSqlRawAsync`, que aplica o script de criação incondicionalmente. Ajuste relacionado: `AplicarMigrationsReaisAsync` (usado só por `ImmutabilityTests`) agora dropa a tabela antes de rodar `MigrateAsync`, já que os dois caminhos de criação (script do model vs. migration real) materializam a tabela de formas incompatíveis (só a migration real tem o trigger de bloqueio).

**Resultado do commit `909d251`:** suíte de integração foi de 36/60 falhando para ~7-8/60 falhando (achados novos, ver abaixo). 100% dos 141 testes unitários/arquitetura seguiram verdes. Legado (`old/`) intocado.

### Commit `f5d6253` — 4 achados adicionais, descobertos porque estavam mascarados pelos bugs acima

Ao destravar a suíte, apareceram 8 falhas novas e distintas que antes ficavam escondidas atrás dos erros de tradução SQL/DI. Investigadas uma a uma (não presumidas "mais do mesmo"):

1. **`IdempotencyDecisionRules.Decidir` nunca checava `ExpiraEm`** — único bug real de **produção** desta segunda rodada. Um registro `Concluida` com `ExpiraEm` no passado ainda era tratado como replay válido, dependendo do `IdempotencyExpirationJob` (roda de hora em hora) já ter deletado a linha primeiro. A spec (plano Fase 0, Step 9, critério de aceite 8) exige que o handler execute de novo IMEDIATAMENTE após expirar. Corrigido adicionando o parâmetro `agora` a `Decidir` e a checagem `existente is null || existente.ExpiraEm <= agora`.
2. **`InboxDedupeTests.Handler_DoisHandlersInscritos` (Messaging)** — bug no **teste**: registrava `HandlerA`/`HandlerB` manualmente com factory keyed *antes* de `AddInboxHandler`, que internamente também registra o mesmo tipo via `AddScoped<THandler>()` sem factory. O container resolvia o último registro (o genérico), que falhava ao injetar `ContadorDeExecucoes` não-keyed — a exceção era capturada silenciosamente por `RegistrarFalhaAsync` (retry, sem relançar), então o teste só via "0 execuções" sem pista do motivo. Corrigido invertendo a ordem de registro.
3. **`OrphanTakeoverTests.RegistroEmProcessamento_...NaoSofreTakeover` (Idempotency)** — bug no **teste**: `PlantarRegistroEmProcessamentoAsync` plantava `PayloadHashSha256 = "hash-fake-do-request-que-travou"` (texto arbitrário) em vez do hash SHA-256 real do corpo `{ valor = 1 }` enviado pelo POST. `Decidir` corretamente via hash diferente e retornava `ConflitoHashDiferente` em vez de `CorridaEmProcessamento` — resposta ainda vinha 409, mas sem o header `Retry-After` esperado. Corrigido plantando `PayloadHasher.Sha256(...)` do corpo real.
4. **3 falhas `OperationCanceledException: Flush was canceled on underlying PipeWriter`** (`ConcurrencyTests` × 2, `OrphanTakeoverTests.TakeoverConcorrente_ApenasUmVence`) — bug no **teste**: padrão `using var cliente = ...` dentro de um `Select` lambda síncrono que dispara `PostAsync` sem aguardar. O `Dispose()` do `HttpClient` rodava ao sair do escopo do lambda — imediatamente, antes de `Task.WhenAll` aguardar as tasks — cancelando a requisição em voo. Corrigido trocando para lambda `async` que aguarda a resposta antes do `using` sair de escopo.

**Regressão pega no processo:** o fix #1 quebrou `EndpointFilterPlumbingTests` (2 testes) porque `FakeIdempotencyStore.BeginAsync` nunca recalculava `CriadaEm`/`ExpiraEm` — o `IdempotencyEndpointFilter` só passa placeholders (`default`), esperando que o store recalcule (o `IdempotencyStore` real já fazia isso; o fake não). Com a checagem de expiração nova, todo registro salvo pelo fake parecia "já vencido" (`ExpiraEm = DateTimeOffset.MinValue`). Identificada e corrigida na mesma sessão, antes do commit.

**Resultado do commit `f5d6253`:** suíte de integração foi de ~52/60 para **59/60** estável (confirmado em 2 execuções seguidas). O único item remanescente é um flake de concorrência pré-existente em `Observability/OtelTests`/`PiiGuardrailTests` (intermitente entre execuções, já documentado no handoff anterior como não-relacionado — `Collection was modified`/`Sequence contains more than one matching element`, corrida no próprio teste).

## 2. Estado do projeto

- **Fase 0 (código): 8 de 9 tarefas concluídas, e agora sem bugs de produto conhecidos bloqueando o CI.** Falta só a Tarefa 8 (provisionamento AWS — Terraform/KMS/S3/RDS não-produção), que exige credenciais de administração do dono.
- **CI:** suíte de integração estável em 59/60 (só o flake conhecido de Observability). Pronto, do ponto de vista de código, para virar gate obrigatório.
- **Branch protection:** dono pediu para marcar `test-integracao` como check obrigatório no `master`. Descoberto que **não é possível hoje**: `gh api repos/Joseleno/nota-fiscal-hub/branches/master/protection` retorna 403 — branch protection rules para repositório **privado** exige GitHub Pro/Team, ou tornar o repo público. Repositório confirmado privado (`gh repo view --json isPrivate` → `true`), plano atual não permite.
  - **Quando o dono resolver isso** (upgrade de plano ou tornar público), o passo é: Settings → Branches → regra do `master` → marcar "Require status checks to pass before merging" → adicionar o check **`build-and-test`** (não `test-integracao` — é um job único do `ci.yml` com steps sequenciais restore→build→guard-arquitetura→test-unidade→test-arquitetura→test-integracao→artefatos→cobertura; branch protection só expõe jobs, não steps individuais).
- **Docker:** confirmado disponível neste ambiente (`docker ps` rodou limpo). Toda a suíte de integração desta sessão rodou contra Postgres real via Testcontainers, não em modo degradado.
- **Branch:** `dev` (não `master`) — segue valendo.
- **Legado (`old/src/VisuFiscalHub.*`):** intocado, confirmado a cada commit.

## 3. Commits desta sessão (branch `dev`)

```
f5d6253 fix: corrigir 4 achados de Idempotency/Messaging mascarados pelos bugs anteriores
909d251 fix: corrigir 4 bugs pre-existentes que bloqueavam o CI de integracao
```

Nenhum commit tem referência a Claude/IA, conforme padrão do time. Nenhum push feito (branch `dev` local está à frente do remoto).

## 4. Decisões desta sessão — não reabrir

- **Filtro de tenant customizado removido, não "consertado".** Investigação (`superpowers:systematic-debugging` Fase 1-3, com sondas reais comentando/restaurando o registro do filtro) provou que a intenção original (impedir funcletização para preservar o tipo da exceção) era incompatível com tradução SQL — não existe meio-termo limpo sem reimplementar internals não-oficiais do EF Core. Decisão ratificada pelo dono: aceitar a mudança de contrato observável (exceção embrulhada) em vez de manter o filtro.
- **`BeginSystemScope` ganhou bypass automático do filtro "Tenant"** — achado durante a mesma investigação, fora do escopo original dos "4 bugs", mas corrigido junto por decisão do dono (mesmo arquivo, mesma causa-raiz de investigação).
- **Dos 8 achados da "task #12" (5 Idempotency + 1 Messaging + 2 Observability), só 1 era bug de produção** (`Decidir` sem checar `ExpiraEm`). Os outros 6 investigados eram bugs no harness de teste; os 2 de Observability continuam como flake conhecido, não investigados nesta sessão (eram os únicos já documentados como tal desde o handoff anterior).
- **Branch protection não aplicado** — bloqueio de plano do GitHub (não uma escolha de escopo). Decisão do dono: aplicar manualmente via UI quando resolver o plano; instruções deixadas prontas acima (§2).

## 5. Decisões de processo válidas (herdadas, seguem valendo)

- **Modelo:** Sonnet 5 padrão (`.claude/settings.local.json`); Opus 4.8 reservado para gates de decisão e revisão adversarial de entregáveis de alto impacto estrutural.
- **Commits:** nenhuma referência a Claude/IA (sem Co-Authored-By).
- **Contexto:** parar a sessão ao chegar ~70% de contexto.
- **Debugging sistemático (`superpowers:systematic-debugging`) + TDD (`superpowers:test-driven-development`) para todo bug fix**: usado nesta sessão para os 8 achados — toda causa-raiz confirmada por execução real (sondas comentando código, instrumentação temporária revertida antes do fix) antes de qualquer correção; todo fix teve RED confirmado antes do GREEN. Vale repetir esse padrão.

## 6. Próximo passo — decisão do dono, não um passo de código automático

Não há bug de produto conhecido bloqueando nada. As opções, em ordem de provável prioridade:

1. **Resolver o plano do GitHub** (upgrade Pro/Team ou tornar o repo público) e aplicar o branch protection em `build-and-test` — item que o próprio dono já pediu nesta sessão, só falta o desbloqueio externo.
2. **Investigar o flake de Observability** (`OtelTests`/`PiiGuardrailTests`, concorrência intermitente) — não bloqueia nada hoje (CI segue verde na maioria das execuções), mas seria o último item para 60/60 estável.
3. **Retomar a Tarefa 8 (provisionamento AWS)** — quando o dono tiver as credenciais de administração à mão.
4. **Iniciar a Fase 1** do plano-mestre.

## 7. Handoffs anteriores (contexto histórico)

- `2026-07-06-fase0-codigo-concluida-handoff.md` — Fase 0 (código) concluída via `subagent-driven-development`, os 4 bugs originais descobertos (não corrigidos ainda). **Superado por este arquivo.**
- `2026-07-02-a5-plano-fase0-handoff.md` — A5 concluída, plano-mestre ajustado, plano da Fase 0 aprovado.
- `2026-07-02-gate0-fechado-solution-nova-handoff.md` — Gate 0 fechado, reestruturação física da solution (`old/`).
- `2026-07-02-gate0-handoff.md` — os 4 primeiros pareceres do Gate 0.
- `2026-07-01-revisao-docs-e-gate0-handoff.md` — revisão dos documentos + estado do Gate 0.
- `2026-07-02-board-clickup-specs-handoff.md` — referência histórica; ClickUp não é mais sincronizado.
- **Este arquivo supera todos para retomada** — é o mais recente e reflete o CI pronto para gate obrigatório, pendente só de desbloqueio de plano do GitHub.
