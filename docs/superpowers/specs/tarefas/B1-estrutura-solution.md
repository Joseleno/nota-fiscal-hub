# Spec B1 — Estrutura da solution (módulos + kernel)
> Card: https://app.clickup.com/t/86e24c1em | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Criar a solution do nota-fiscal-hub como monólito modular .NET: 2 deployables (`Api`, `Worker`), 5 módulos de negócio com 4 projetos cada (`Domain`, `Application`, `Infrastructure`, `Contracts`), kernel transversal em `BuildingBlocks/`, EF Core com schema PostgreSQL e migrations POR módulo, e suíte NetArchTest que trava as fronteiras do design §2.3/§2.5 desde o primeiro commit. Entregável: esqueleto mínimo que compila, com CI capaz de falhar o build em referência indevida.

## Contexto e referências
- Design §2 (módulos, regras de fronteira, dependências): `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`
- Plano-mestre, Global Constraints e Fase 0: `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`
- Decisões D-2026-07-01-01..10 (§0): `docs/decisions.md` — em especial D-04 (contador), D-10 (schema `leitura` pertence à Emissão)
- Handoff Gate 0: `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md`
- Os 5 módulos do MVP (nomes reais, design §2.2): **Contas & Planos**, **Empresas & Certificados**, **Emissão**, **Motor NFCe/NFe**, **Documentos**.

**Assunções pendentes da matriz A2 (Gate 0)** — marcar no PR e revisar quando a matriz for ratificada:
- **A2-a:** solution **NOVA** (matriz preliminar do Gate 0), com **porte cirúrgico do Motor** legado (`Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator` + testes) na Fase 3 — B1 só reserva o lugar.
- **A2-b:** destino físico da solution legada (mesmo repo em `src/` novo vs. repo novo) é decisão em aberto em `docs/decisions.md` §0; esta spec assume **mesmo repo, código novo sob `src/`**, sem tocar o legado.
- **A2-c:** stack herdada do legado mantida onde não conflita com o design: .NET 10, EF Core 10 + Npgsql, xUnit + Shouldly + NSubstitute.

## Escopo (dentro / fora)
**Dentro:** solution + projetos + referências; convenção de namespaces; `Directory.Build.props`/`Directory.Packages.props` com guard-condition que exclui o legado; DbContext por módulo com schema próprio e migration inicial vazia; classe base `ModuleDbContext` no kernel; suíte NetArchTest; placeholder do Motor; CI mínimo (build + testes).
**Fora:** tenant context/middleware, biblioteca Outbox/Inbox, Idempotency-Key, auditoria, observabilidade (tarefas B2+ da Fase 0); qualquer entidade de negócio; provisionamento AWS; porte do Motor (Fase 3); Portal/Backoffice.

## Abordagem / Passos
Árvore-alvo (raiz do repo; legado intocado — o legado vive DENTRO de `src/` e `tests/`, coexistindo lado a lado):
```
NotaFiscalHub.sln
Directory.Build.props            # net10.0, Nullable, TreatWarningsAsErrors, LangVersion — TUDO sob guard-condition (abaixo)
Directory.Packages.props         # central package management — habilitado só p/ projetos novos (guard-condition)
src/
  VisuFiscalHub.Api/                           # LEGADO — intocado, fora da nova sln
  VisuFiscalHub.Application/                   # LEGADO
  VisuFiscalHub.Domain/                        # LEGADO
  VisuFiscalHub.Infrastructure/                # LEGADO
  Api/NotaFiscalHub.Api/                       # host REST (composição DI de todos os módulos)
  Worker/NotaFiscalHub.Worker/                 # host de jobs/handlers assíncronos
  BuildingBlocks/
    NotaFiscalHub.BuildingBlocks.Kernel/       # abstrações puras (sem EF): marcador de entidade tenant-scoped, Result, relógio
    NotaFiscalHub.BuildingBlocks.Persistence/  # ModuleDbContext base, convenções EF (snake_case, schema, history table)
  Modules/
    ContasPlanos/            NotaFiscalHub.Modules.ContasPlanos.{Domain,Application,Infrastructure,Contracts}/
    EmpresasCertificados/    NotaFiscalHub.Modules.EmpresasCertificados.{...}/
    Emissao/                 NotaFiscalHub.Modules.Emissao.{...}/
    MotorNfce/               NotaFiscalHub.Modules.MotorNfce.{...}/   # RESERVADO — porte na Fase 3 (A2-a); sem DbContext (stateless)
    Documentos/              NotaFiscalHub.Modules.Documentos.{...}/
tests/
  VisuFiscalHub.Tests/                         # LEGADO — intocado
  NotaFiscalHub.ArchitectureTests/             # NetArchTest — única suíte obrigatória em B1
```
**Props na raiz SEM quebrar o legado (guard-condition obrigatória):** `Directory.Build.props`/`Directory.Packages.props` na raiz se aplicam por herança MSBuild também aos projetos `VisuFiscalHub.*` — e `TreatWarningsAsErrors` + Central Package Management quebrariam o build legado (NU1903 confirmado no handoff do Gate 0). Solução: todo conteúdo dos dois props fica condicionado ao nome do projeto, ex.: `<PropertyGroup Condition="$(MSBuildProjectName.StartsWith('NotaFiscalHub'))">` e, no Packages.props, `<ManagePackageVersionsCentrally Condition="$(MSBuildProjectName.StartsWith('NotaFiscalHub'))">true</ManagePackageVersionsCentrally>` (com `ItemGroup` de `PackageVersion` sob a mesma condição). Assim os projetos legados continuam compilando exatamente como hoje, sem editar nenhum `.csproj` legado e sem arquivo de opt-out dentro das pastas do legado.

**Namespaces = nome do projeto** (RootNamespace implícito): `NotaFiscalHub.Modules.Emissao.Domain`, `NotaFiscalHub.BuildingBlocks.Kernel`, `NotaFiscalHub.Api`. Nomes sem acento (`Emissao`), módulos compostos em PascalCase (`ContasPlanos`, `EmpresasCertificados`, `MotorNfce`).

**Referências permitidas** (tudo fora disto é violação; NetArchTest codifica):
| Projeto | Pode referenciar |
|---|---|
| `*.Contracts` | nada (DTOs/interfaces puros — design §2.3.3) |
| `*.Domain` | `Kernel` |
| `*.Application` | `Domain` e `Contracts` do próprio módulo; `Kernel`; `Contracts` de outros módulos **só** conforme §2.5 (abaixo) |
| `*.Infrastructure` | `Application`/`Domain`/`Contracts` próprios; `Persistence`; `Kernel` |
| `Api`, `Worker` | `Infrastructure` + `Contracts` de todos os módulos (composição DI); `BuildingBlocks.*` |

Matriz cross-módulo (design §2.5): `Emissao.Application → {EmpresasCertificados, MotorNfce, Documentos}.Contracts`; `MotorNfce.Application → EmpresasCertificados.Contracts`; **nenhum módulo → ContasPlanos.Contracts** — exceção única: hosts e (futuro) middleware de autenticação do kernel, codificada como allowlist nomeada na suíte NetArchTest.

**EF Core — schema e migrations por módulo:** cada `Infrastructure` (exceto Motor) tem `<Modulo>DbContext : ModuleDbContext` com `HasDefaultSchema` fixo — `contas`, `empresas`, `emissao`, `documentos` — e `MigrationsHistoryTable("__ef_migrations_history", "<schema>")`; migrations em `Infrastructure/Migrations/` do próprio módulo + `IDesignTimeDbContextFactory` por contexto. O schema `leitura` NÃO nasce em B1: pertence à Emissão (D-2026-07-01-10) e sua migration chega com `NotaConsulta` na fase própria — registrar comentário no `EmissaoDbContext`.

Passos (TDD — constraint global):
1. Scaffold: `dotnet new sln` + projetos + referências da tabela acima; 1 arquivo `AssemblyMarker.cs` (classe `public static` vazia) por projeto para dar tipo às assemblies; hosts com `Program.cs` mínimo que sobe vazio.
2. **Testes primeiro na parte com comportamento:** escrever `ArchitectureTests` (regras da tabela + matriz §2.5 + "Domain/Contracts não referenciam EF/Npgsql" + "nenhum módulo referencia Infrastructure de outro"). Provar RED: adicionar temporariamente uma referência ilegal (`Emissao.Domain → Emissao.Infrastructure`) e ver o teste falhar; remover; GREEN. Registrar o par red/green na descrição do PR.
3. DbContexts + convenções na `Persistence` + migration inicial **vazia** por módulo (prova o pipeline de migrations por schema sem inventar entidade).
4. CI (GitHub Actions ou equivalente do repo): `dotnet build -warnaserror` → `dotnet test` (arquitetura). Estágio de integração fica reservado para B2+.

## Critérios de aceite (verificáveis)
1. `dotnet build NotaFiscalHub.sln` verde na raiz, zero warnings (TreatWarningsAsErrors).
2. `dotnet test tests/NotaFiscalHub.ArchitectureTests` verde; com uma referência ilegal adicionada de propósito, ao menos 1 teste falha (evidência red/green anexada ao PR).
3. `dotnet ef migrations list` por contexto (4 contextos) mostra a migration inicial no diretório `Migrations/` do módulo dono; `dotnet ef database update` num PostgreSQL local cria os 4 schemas com `__ef_migrations_history` dentro de cada um (nenhuma tabela no `public`).
4. `Modules/MotorNfce/` existe com os 4 projetos compilando, sem DbContext, com `README.md` de 3 linhas apontando o porte da Fase 3 e a assunção A2-a.
5. Nenhum projeto `*.Domain` ou `*.Contracts` referencia `Microsoft.EntityFrameworkCore*`/`Npgsql*` (verificado por teste de arquitetura, não por revisão manual).
6. Grep por `namespace` nos `.cs` novos: 100% seguem `NotaFiscalHub.(Modules.<Modulo>.<Camada>|BuildingBlocks.<Bloco>|Api|Worker|ArchitectureTests)` — o padrão inclui a própria suíte de testes de arquitetura.
7. Código legado — definido como `src/VisuFiscalHub.*` e `tests/VisuFiscalHub.Tests` (que coexistem com o código novo dentro de `src/` e `tests/`) — sem nenhuma linha alterada (`git diff --stat` no PR mostra zero mudanças nesses caminhos).
8. Build do legado continua verde e idêntico ao de antes do PR: compilar `VisuFiscalHub.Api` (ou a sln legada) na branch do PR sem novos erros/warnings promovidos a erro (prova de que a guard-condition dos props isolou o legado; NU1903 permanece warning, não erro).

## Plano de testes / Evidências
- **ArchitectureTests (xUnit + NetArchTest.Rules):** um teste por linha da tabela de referências; um por seta da matriz §2.5 (positivo: permitido compila; negativo: tipos de `ContasPlanos.Contracts` não são referenciados por nenhum `Modules.*` — allowlist só com hosts); teste de pureza (Domain/Contracts sem EF). São os testes-guarda exigidos pela Global Constraint "NetArchTest no CI desde a Fase 0".
- **Evidências:** saída do build; print/log do RED provocado (passo 2); `\dn` + `\dt <schema>.*` do banco local após `database update`; link do run de CI verde; log do build legado verde na branch do PR (critério 8).
- Testes de unidade de negócio: N/A em B1 (não há comportamento de domínio); nascem em B2+ e Fases 1–4.

## Dependências
- Gate 0 formalmente fechado (matriz A2 ratificada) — B1 pode iniciar sob as assunções A2-a/b/c, mas o merge exige a decisão "solution nova" confirmada em `docs/decisions.md` §0.
- PostgreSQL local/Testcontainers para o critério 3 (sem dependência de AWS).
- Nada de B2+ depende para trás; B2 (tenant context), B3 (outbox/inbox) e B4 (idempotência) constroem SOBRE esta estrutura — B1 é o primeiro card executável da Fase 0.

## Riscos e pontos de atenção
- **Fronteira furada "por conveniência" na composição DI:** hosts referenciam `Infrastructure` de todos — é o único lugar onde isso é legal. Mitigação: cada módulo expõe `Add<Modulo>Module(IServiceCollection, IConfiguration)` na própria Infrastructure; `Program.cs` só compõe.
- **Matriz A2 divergir das assunções** (ex.: decisão por evoluir in-place): o custo de B1 é quase todo reaproveitável (projetos/testes migram), mas parar e reabrir a spec se a decisão contrariar A2-a/b.
- **Nome do módulo Motor:** design diz "Motor NFCe/NFe"; `MotorNfce` assume NFe entrando no mesmo módulo depois (design §2.2). Se o revisor preferir `MotorNfe` como nome guarda-chuva, decidir ANTES do primeiro commit — renomear projeto depois é caro.
- **Migrations por módulo exigem disciplina de `--context`:** documentar os comandos exatos (`dotnet ef migrations add X --project src/Modules/Emissao/...Infrastructure --startup-project src/Api/... --context EmissaoDbContext`) no README da raiz para não nascer migration no contexto errado.
- **Guard-condition dos props frágil a renomes:** a condição por prefixo `NotaFiscalHub` só funciona enquanto todo projeto novo seguir a convenção de nome e nenhum legado adotá-la; validar com o critério 8 (build legado) em TODO PR que tocar os props, e revisitar quando o legado for removido/migrado (assunção A2-b).
- **Kernel gordo:** tentação de antecipar tenant/outbox aqui. B1 entrega o Kernel quase vazio de propósito; conteúdo chega nos cards B2/B3 com seus próprios testes.
