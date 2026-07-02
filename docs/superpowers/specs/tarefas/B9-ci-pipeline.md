# Spec B9 — CI completo (build → unidade+arquitetura → integração)
> Card: https://app.clickup.com/t/86e24c1kp | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Pipeline de CI que valida todo push/PR em três estágios encadeados — **build → testes de unidade + arquitetura → testes de integração (Testcontainers)** — com a suite NetArchTest de B6 atuando como gate obrigatório (violação de fronteira = build vermelho). CI verde é critério de saída da Fase 0 junto com B1–B7 (plano-mestre, Fase 0: "CI: build → testes unidade+arquitetura → integração").

## Contexto e referências
- Design §4.4 (camadas de teste e ferramentas) e §4.5 (pipeline completo: `build → unidade+arquitetura → integração → homologação (tpAmb=2) → smoke SEFAZ-SE → produção` — B9 implementa só os 3 primeiros estágios): `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`.
- Plano-mestre, Global Constraints ("TDD em todas as fases; **NetArchTest no CI desde a Fase 0**") e Fase 0: `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`.
- Decisões D-2026-07-01-01..10 (`docs/decisions.md` §0): nenhuma afeta diretamente o CI; D-07 (um ambiente produtivo; "homologação" = `tpAmb=2`) condiciona a extensão futura de deploy (Fase 7), não o B9.
- Handoff Gate 0 (`docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md`): matriz APROVEITAR/ADAPTAR/REESCREVER ainda não aprovada; NU1903 conhecidos (`Microsoft.OpenApi 2.0.0`, `System.Security.Cryptography.Xml 10.0.0`).
- **Plataforma:** remoto verificado com `git remote -v` → `https://github.com/Joseleno/nota-fiscal-hub.git` (GitHub). **Proposta: GitHub Actions** — registrar como decisão em `docs/decisions.md` §0 (sugestão: D-2026-07-02-xx "CI em GitHub Actions, runner `ubuntu-latest`") na abertura do PR de B9.

## Escopo (dentro / fora)
**Dentro:**
- Workflow `.github/workflows/ci.yml`: triggers `push` (branch principal) e `pull_request`; concurrency com `cancel-in-progress` por ref.
- Job único em `ubuntu-latest` (Docker nativo p/ Testcontainers) com steps sequenciais: restore → build (`--no-restore`) → unidade+arquitetura (`--no-build`) → integração (`--no-build`). Job único evita rebuild entre jobs; separação de estágios fica visível por step nomeado.
- Cache de NuGet via `actions/setup-dotnet` (`cache: true` + `cache-dependency-path: '**/packages.lock.json'`) — exige `RestorePackagesWithLockFile=true` no `Directory.Build.props` (se A2/B1 não o criar, B9 cria só a propriedade; qualquer outro conteúdo é de B1).
- Pin do SDK: `global.json` com o SDK .NET 10 usado localmente (`rollForward: latestFeature`) — criado por B9 se não existir.
- Seleção de testes por convenção de projeto (contrato abaixo), sem filtro por trait no caminho feliz.
- Publicação de artefatos: resultados TRX + cobertura.
- Badge de status no `README.md`.
**Fora:** deploy/homologação `tpAmb=2`/smoke SEFAZ-SE (Fase 7, design §4.5); publicação de imagem Docker; release/versionamento; cobertura mínima como gate (fica para revisão na Fase 5); CD de infra AWS (trilha paralela da Fase 0, fora do CI de código).

## Abordagem / Passos
Convenção que o pipeline consome (contrato com B4–B7 — projetos de teste da nova solution):

```
tests/
  <Nome>.UnitTests/            # xUnit, sem I/O (design §4.4 Unidade + Golden files)
  <Nome>.ArchitectureTests/    # NetArchTest (B6) — gate de fronteiras §2.3/§2.5
  <Nome>.IntegrationTests/     # Testcontainers (Postgres; SEFAZ fake) — exige Docker
```

Passos (TDD — teste primeiro, conforme constraint global; para pipeline, "teste" = validação executável do workflow e prova de que cada gate falha quando deve):

1. **RED — validador do workflow:** adicionar step/execução local de `actionlint` (via `docker run rhysd/actionlint`) e script `scripts/ci-local.sh` que reproduz os 3 estágios localmente com os mesmos comandos `dotnet` do workflow (fonte única da sequência; o YAML chama os mesmos comandos). Rodar antes de existir o `ci.yml` → falha (arquivo ausente) é o RED.
2. **GREEN — workflow mínimo:** criar `ci.yml` (triggers, concurrency, `setup-dotnet` + `global.json` + cache NuGet, restore, build com `--configuration Release`). `actionlint` passa; build verde no GitHub.
3. **Estágio unidade+arquitetura:** `dotnet test --no-build -c Release --logger "trx;LogFileName=unit.trx"` sobre os projetos `*.UnitTests` e `*.ArchitectureTests` (glob por convenção). **Prova do gate (RED de verdade):** em branch descartável, introduzir referência proibida (ex.: projeto de um módulo referenciando `Infrastructure` de outro, violando §2.5) → CI fica vermelho no step `arquitetura`; reverter. Evidência (link do run vermelho) anexada ao PR.
4. **Estágio integração:** `dotnet test --no-build -c Release` sobre `*.IntegrationTests`, com `TESTCONTAINERS_RYUK_DISABLED=false` (default) — `ubuntu-latest` tem Docker; nenhum service container manual (Testcontainers gerencia). Prova: teste de integração sabotado em branch descartável → run vermelho; reverter.
5. **Artefatos e acabamento:** upload de TRX + cobertura (`--collect:"XPlat Code Coverage"`) com `actions/upload-artifact` e `if: always()`; `timeout-minutes` no job (ver critérios); badge no README; registrar a decisão de plataforma em `docs/decisions.md` §0.
6. **Extensão futura (documentar, não implementar):** comentário no `ci.yml` reservando os estágios da Fase 7 (`homologacao-tpamb2`, `smoke-sefaz`, `deploy`) conforme design §4.5 — fora de escopo aqui.

## Critérios de aceite (verificáveis)
1. `git push` em branch com PR aberto dispara o workflow; run verde com os steps nomeados `restore`, `build`, `test-unidade`, `test-arquitetura`, `test-integracao`, `artefatos` visíveis no log.
2. **Gate de arquitetura:** referência entre módulos fora do permitido em §2.5 (demonstrado em branch descartável) → step `test-arquitetura` falha → run vermelho → merge do PR bloqueado (branch protection com o check obrigatório configurada no repositório).
3. Falha em teste de unidade ou integração também deixa o run vermelho (nenhum `continue-on-error` em step de teste).
4. Cache de NuGet: segundo run consecutivo sem mudança de dependências loga `Cache restored from key:` e o restore cai para segundos.
5. **Tempo-alvo:** pipeline completo ≤ 10 min com cache quente (build+unidade+arquitetura ≤ 4 min; integração ≤ 6 min); `timeout-minutes: 20` como teto duro no job. Medido no run do PR de B9.
6. Artefatos `test-results` (TRX) e `coverage` baixáveis do run, inclusive em run vermelho (`if: always()`).
7. `actionlint` sem erros sobre `.github/workflows/ci.yml`.
8. `scripts/ci-local.sh` executa os 3 estágios localmente com exit code fiel (0 verde / ≠0 vermelho).
9. Decisão "CI em GitHub Actions" registrada em `docs/decisions.md` §0.

## Plano de testes / Evidências
| Verificação | Como | Evidência |
|---|---|---|
| Workflow sintaticamente válido | `actionlint` local e como step | saída limpa no log |
| Gate de arquitetura derruba o build | branch descartável com violação de §2.5 | link do run vermelho no PR de B9 |
| Gate de integração derruba o build | teste sabotado em branch descartável | link do run vermelho no PR de B9 |
| Testcontainers sobe Postgres no runner | run do estágio integração | log do container no TRX/console |
| Cache efetivo | 2 runs consecutivos | log `Cache restored` + duração do restore |
| Tempo-alvo | duração do run com cache quente | screenshot/link do run ≤ 10 min |
| Paridade local/CI | `scripts/ci-local.sh` na máquina dev (Docker Desktop) | mesmos resultados dos runs |

## Dependências
- **B1 (estrutura da solution):** nome/caminho do `.sln`/`.slnx` novo e layout `tests/` — o `ci.yml` referencia a solution raiz; bloqueante para o merge (o workflow pode ser escrito antes contra a convenção).
- **B6 (suite NetArchTest):** sem ela o step `test-arquitetura` não tem o que rodar — B9 só fecha depois de B6 mergeado (pode mergear com o glob vazio tratado como falha: step verifica que ≥ 1 projeto `*.ArchitectureTests` existe e tem ≥ 1 teste, senão falha — evita gate silenciosamente vazio).
- **B3/B4 (outbox/inbox, idempotência — testes de integração):** primeiro consumidor real do estágio de integração; mesmo guard de "≥ 1 teste" aplicado.
- Repositório GitHub com Actions habilitado e permissão para configurar branch protection (dono do repo).

## Riscos e pontos de atenção
- **Assunções dependentes da matriz do Gate 0 (A2) — explícitas:** (a) a decisão "solution nova vs. evoluir in-place" ainda não foi ratificada; esta spec assume solution nova da Fase 0 com a convenção `tests/*.{Unit,Architecture,Integration}Tests`. Se A2 decidir evoluir `VisuFiscalHub.slnx` in-place, os globs e o nome da solution mudam e a suíte legada (761 testes, projeto único `tests/VisuFiscalHub.Tests`) precisará de split ou filtro por trait `Category` — revisitar os steps 3–4. (b) O guard "≥ 1 teste por estágio" protege o período de transição em ambos os cenários.
- **NU1903 conhecidos** (`System.Security.Cryptography.Xml`, `Microsoft.OpenApi`): **não** ligar `-warnaserror` global no CI agora, ou o build nasce vermelho por dívida já rastreada na Fase 3 (plano-mestre). Ligar `TreatWarningsAsErrors` só para warnings de compilação C#, não de restore (NUxxxx), e registrar a exceção em comentário no workflow.
- **Testcontainers em runner hospedado:** flakiness por pull de imagem — fixar tag da imagem Postgres (ex.: `postgres:17-alpine`) nos testes (contrato com B4/B7) e considerar retry de step **não** (mascara flakiness; preferir investigar).
- **Tempo de integração crescerá** com as Fases 1–4; quando estourar o alvo, dividir em jobs paralelos por módulo (a convenção de projetos já permite) — não antecipar agora.
- **Segredos:** nenhum estágio de B9 usa segredo (SEFAZ real só na Fase 7); qualquer `secrets.*` no `ci.yml` antes disso é smell de escopo furado.
- Runner exige Docker: se o repo um dia migrar para runners self-hosted Windows, o estágio de integração quebra — a decisão de plataforma em `docs/decisions.md` deve anotar esse pré-requisito ("runner com Docker Linux").
