# Fase 0 — Fundação e kernel transversal — Plano de Implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) ou superpowers:executing-plans para executar este plano tarefa-por-tarefa. Steps usam sintaxe checkbox (`- [ ]`) para tracking.

> **Aprovação (critério de aceite 5 da spec A5):** aprovado pelo dono do produto (Joseleno Dias Moreira dos Santos) em 2026-07-02, após revisão adversarial em 2 passadas com Opus 4.8 (1 achado crítico + 4 achados menores corrigidos — ver seção "Dívidas registradas" ao final para os pontos de atenção remanescentes, não-bloqueantes). Gerado a partir das specs `docs/superpowers/specs/tarefas/B1-estrutura-solution.md` a `B9-ci-pipeline.md` e da matriz `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`.

**Goal:** Entregar a fundação do monólito modular nota-fiscal-hub: solution com 5 módulos + kernel `BuildingBlocks/`, 2 deployables (Api/Worker), tenant context com filtro global fail-closed, outbox/inbox por módulo sem eventos perdidos em silêncio, idempotência de API, auditoria append-only, NetArchTest travando fronteiras, observabilidade fundacional e CI de 3 estágios — tudo sobre PostgreSQL/Testcontainers, sem tocar o legado em `old/`.

**Architecture:** Monólito modular .NET 10, EF Core 10 + Npgsql, schema PostgreSQL por módulo, outbox/inbox in-process (sem broker externo no MVP), tenant como fronteira de primeira classe via `AsyncLocal` + query filter global fail-closed, NetArchTest como gate de CI (não convenção).

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, xUnit + NetArchTest.Rules + Testcontainers, Serilog + OpenTelemetry, Terraform (AWS), GitHub Actions.

## Global Constraints (do plano-mestre — valem para TODAS as tarefas)

- Certificado A1 somente; chave privada nunca sai de Empresas & Certificados (`AssinarXml`). *(Não aplicável ao código desta fase — kernel não manipula certificados; vale como fronteira que o NetArchTest desta fase já começa a codificar.)*
- `conta_id` em toda tabela tenant-scoped + filtro global obrigatório + `BeginTenantScope` em todo job do Worker.
- Outbox POR MÓDULO; handlers idempotentes e tolerantes a reordenação, testados com duplicata/fora-de-ordem.
- IDs `ContaId`/`EmpresaId` são GUIDs opacos do módulo dono; sem FK/join cross-módulo na escrita.
- `tpAmb` permeia numeração, guarda, metering (só produção conta) e escopo de API key (`nfh_test_` não opera produção). *(Fase 0 só prepara o terreno — `tpAmb` concreto nasce na Fase 1/2.)*
- `Idempotency-Key` obrigatória na emissão; mesma key + payload diferente → 409.
- Eventos de integração carregam identificadores, nunca XML/PDF/PII.
- Erros da API em RFC 7807; logs estruturados sem PII.
- TDD em todas as fases; NetArchTest no CI desde a Fase 0.
- Contingência: novo XML `tpEmis=9` (`dhCont`/`xJust`), chave própria, QR Code v3 (NT 2025.001); transmissão em ≤ 24h com alerta de primeira classe. *(Fase 3 — fora do escopo de código desta fase.)*
- Transporte SEFAZ: `indSinc=1`, lote unitário; a decisão é o `cStat` do `protNFe` (nunca o do lote). *(Fase 3 — fora do escopo de código desta fase.)*

**Rastreabilidade matriz A2:** esta fase materializa as linhas 8 (Kernel = REESCREVER), 9 (Outbox/Inbox = ADAPTAR), 10 (Deployables = REESCREVER) e a parte "arquitetura" da linha 11 (Suíte) da matriz `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`. Decisão D-2026-07-02-03 (handlers dos 3 eventos do achado Q1) é escopo da Tarefa 3 (Outbox/Inbox).

---

## Sequenciamento das 9 tarefas

```
Tarefa 1 (B1: estrutura solution) ─┬─→ Tarefa 2 (B2: tenant context) ──┬─→ Tarefa 4 (B4: idempotency-key)
                                    │                                   │
                                    ├─→ Tarefa 6 (B6: NetArchTest) ←────┘   (T5/T6 do B6 dependem de B2)
                                    │
                                    └─→ Tarefa 3 (B3: outbox/inbox) ──┬─→ Tarefa 5 (B5: auditoria append-only)
                                                                       │
                                                                       └─→ Tarefa 7 (B7: observabilidade)
                                                                            (T4 do B7 depende do dispatcher de B3)

Tarefa 8 (B8: provisionamento AWS) — trilha paralela, sem dependência de código; desbloqueia Fases 2/4.
Tarefa 9 (B9: CI pipeline) — depende de B1 (solution/tests layout) e B6 (suíte a rodar); fecha a fase.
```

Ordem de execução recomendada: **1 → 2 → 6 → 3 → 4 → 5 → 7 → (8 em paralelo a qualquer momento) → 9 (por último, fecha o CI sobre tudo)**.

---

### Tarefa 1: Estrutura da solution (módulos + kernel)

**Spec de origem:** `docs/superpowers/specs/tarefas/B1-estrutura-solution.md`

**Files:**
- Create: `NotaFiscalHub.sln`, `Directory.Build.props`, `Directory.Packages.props`
- Create: `src/Api/NotaFiscalHub.Api/{NotaFiscalHub.Api.csproj,Program.cs,AssemblyMarker.cs}`
- Create: `src/Worker/NotaFiscalHub.Worker/{NotaFiscalHub.Worker.csproj,Program.cs,AssemblyMarker.cs}`
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Kernel/{NotaFiscalHub.BuildingBlocks.Kernel.csproj,AssemblyMarker.cs}`
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Persistence/{NotaFiscalHub.BuildingBlocks.Persistence.csproj,ModuleDbContext.cs,AssemblyMarker.cs}`
- Create (por módulo `ContasPlanos`, `EmpresasCertificados`, `Emissao`, `Documentos`, cada um com 4 projetos): `src/Modules/<Modulo>/NotaFiscalHub.Modules.<Modulo>.{Domain,Application,Infrastructure,Contracts}/{<Nome>.csproj,AssemblyMarker.cs}`
- Create (Motor, reservado, sem DbContext): `src/Modules/MotorNfce/NotaFiscalHub.Modules.MotorNfce.{Domain,Application,Infrastructure,Contracts}/{<Nome>.csproj,AssemblyMarker.cs}`, `src/Modules/MotorNfce/README.md`
- Create: `tests/NotaFiscalHub.ArchitectureTests/{NotaFiscalHub.ArchitectureTests.csproj,ReferenceRulesTests.cs}`
- Create (por módulo com DbContext): `src/Modules/<Modulo>/NotaFiscalHub.Modules.<Modulo>.Infrastructure/{<Modulo>DbContext.cs,Migrations/,DesignTimeDbContextFactory.cs}`

**Interfaces:**
- Produces: `ModuleDbContext` (classe base abstrata em `NotaFiscalHub.BuildingBlocks.Persistence`, sem membros públicos ainda além do construtor — Tarefa 2 adiciona o filtro de tenant) — consumida por Tarefa 2.
- Produces: convenção de namespace `NotaFiscalHub.(Modules.<Modulo>.<Camada>|BuildingBlocks.<Bloco>|Api|Worker|ArchitectureTests)` — consumida por todas as tarefas seguintes.
- Produces: matriz de referências permitidas (tabela da spec B1, passo "Referências permitidas") — consumida por Tarefa 6 (NetArchTest codifica as mesmas regras).

- [ ] **Step 1: Criar a solution e o guard-condition dos props**

```powershell
dotnet new sln -n NotaFiscalHub
```

Criar `Directory.Build.props` na raiz:

```xml
<Project>
  <PropertyGroup Condition="$(MSBuildProjectName.StartsWith('NotaFiscalHub'))">
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

Criar `Directory.Packages.props` na raiz:

```xml
<Project>
  <PropertyGroup Condition="$(MSBuildProjectName.StartsWith('NotaFiscalHub'))">
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup Condition="$(MSBuildProjectName.StartsWith('NotaFiscalHub'))">
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Verificar que o legado continua compilando com os props novos na raiz**

> **Nota de caminho:** o legado já foi movido para `old/` na reestruturação do Gate 0 (commit `7b96d51`) — vive hoje em `old/src/VisuFiscalHub.*` e `old/tests/VisuFiscalHub.Tests`, não em `src/VisuFiscalHub.*`. Os `Directory.Build.props`/`Directory.Packages.props` da raiz herdam por MSBuild também para `old/` (está abaixo da raiz na árvore) — a guard-condition por nome (`StartsWith('NotaFiscalHub')`) continua isolando-o normalmente. A solution nova cria `src/` e `tests/` **vazios na raiz, ao lado de `old/`** (não dentro dele) — a árvore-alvo da spec B1 (que previa coexistência dentro de `src/`) foi superada pela reestruturação; os Steps 3+ deste plano já assumem `src/`/`tests/` como raízes novas.

Run: `dotnet build old/src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj`
Expected: build verde, sem novos erros/warnings promovidos a erro (NU1903 continua warning). Se falhar, a condição `$(MSBuildProjectName.StartsWith('NotaFiscalHub'))` está vazando para o legado — revisar os props antes de prosseguir.

- [ ] **Step 3: Criar os projetos dos hosts e do kernel com `AssemblyMarker`**

Para cada projeto da árvore-alvo (Api, Worker, BuildingBlocks.Kernel, BuildingBlocks.Persistence): `dotnet new classlib` (ou `webapi` para Api/Worker) no caminho correto, seguido de um arquivo `AssemblyMarker.cs`:

```csharp
namespace NotaFiscalHub.BuildingBlocks.Kernel;

public static class AssemblyMarker;
```

(mesmo padrão, namespace ajustado, em todo projeto novo — dá tipo à assembly para o `Types.InAssembly` do NetArchTest na Tarefa 6).

`Program.cs` do Api e do Worker sobem vazios (`WebApplication.CreateBuilder(args).Build().Run();` mínimo, sem endpoints ainda).

- [ ] **Step 4: Criar os 4 projetos de cada um dos 4 módulos de negócio + o Motor reservado**

Repetir para `ContasPlanos`, `EmpresasCertificados`, `Emissao`, `Documentos`: 4 `classlib` (`Domain`, `Application`, `Infrastructure`, `Contracts`) com `AssemblyMarker.cs` e as `ProjectReference` da tabela:

| Projeto | Referencia |
|---|---|
| `*.Contracts` | nada |
| `*.Domain` | `Kernel` |
| `*.Application` | `Domain` e `Contracts` do próprio módulo; `Kernel` |
| `*.Infrastructure` | `Application`/`Domain`/`Contracts` próprios; `Persistence`; `Kernel` |

Para `MotorNfce`: os mesmos 4 projetos, sem `Infrastructure` referenciando `Persistence` (Motor é stateless — sem DbContext). Criar `src/Modules/MotorNfce/README.md`:

```markdown
# Motor NFCe/NFe — reservado

Este módulo recebe o porte cirúrgico do Motor legado (`Infrastructure/Fiscal/{Sefaz,XmlBuilder}` + `QrCodeGenerator`)
na Fase 3, condicionado a golden files validados contra XSD (D-2026-07-02-02).
Ver matriz A2 linha 6: `docs/superpowers/reviews/2026-07-02-gate0-matriz-final.md`.
```

Adicionar todos os projetos à solution: `dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj)` (ou listar explicitamente).

- [ ] **Step 5: Registrar referências cross-módulo permitidas (matriz §2.5)**

Adicionar `ProjectReference` extras conforme a matriz cross-módulo: `Emissao.Application → {EmpresasCertificados,MotorNfce,Documentos}.Contracts`; `MotorNfce.Application → EmpresasCertificados.Contracts`. Hosts (`Api`, `Worker`) referenciam `Infrastructure` + `Contracts` de todos os módulos + `BuildingBlocks.*`.

Run: `dotnet build NotaFiscalHub.sln`
Expected: build verde, zero warnings.

- [ ] **Step 6: Escrever o teste de arquitetura RED — referência ilegal**

Criar `tests/NotaFiscalHub.ArchitectureTests/NotaFiscalHub.ArchitectureTests.csproj` (referencia `Microsoft.NET.Test.Sdk`, `xunit`, `NetArchTest.Rules`, e nada mais — a suíte não pode depender dos módulos que testa via `ProjectReference`, só via reflexão de assembly carregada em runtime).

```csharp
using NetArchTest.Rules;
using Xunit;

namespace NotaFiscalHub.ArchitectureTests;

public class ReferenceRulesTests
{
    [Fact]
    public void Domain_NaoReferenciaInfrastructure()
    {
        var result = Types.InAssembly(typeof(Modules.Emissao.Domain.AssemblyMarker).Assembly)
            .That().ResideInNamespace("NotaFiscalHub.Modules.Emissao.Domain")
            .ShouldNot().HaveDependencyOn("NotaFiscalHub.Modules.Emissao.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }
}
```

Temporariamente adicionar `<ProjectReference Include="...Emissao.Infrastructure...">` ao `Emissao.Domain.csproj` e um `using` fictício em qualquer arquivo do Domain.

Run: `dotnet test tests/NotaFiscalHub.ArchitectureTests`
Expected: **FAIL** — `Domain_NaoReferenciaInfrastructure` falha, confirmando que o teste pega a violação (prova RED).

- [ ] **Step 7: Reverter a violação e confirmar GREEN**

Remover a `ProjectReference`/`using` temporários do Step 6.

Run: `dotnet test tests/NotaFiscalHub.ArchitectureTests`
Expected: **PASS**.

Registrar no PR: "RED confirmado no Step 6 (violação `Emissao.Domain → Emissao.Infrastructure` derrubou o teste); GREEN após reverter."

- [ ] **Step 8: DbContexts por módulo + migration inicial vazia**

Para cada módulo com dados (`ContasPlanos`, `EmpresasCertificados`, `Emissao`, `Documentos` — não o Motor), criar `<Modulo>DbContext : ModuleDbContext` em `Infrastructure/`:

```csharp
namespace NotaFiscalHub.Modules.Emissao.Infrastructure;

public sealed class EmissaoDbContext(DbContextOptions<EmissaoDbContext> options) : ModuleDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("emissao");
    }
}
```

`ModuleDbContext` (em `BuildingBlocks.Persistence`) fixa `MigrationsHistoryTable("__ef_migrations_history", schema)` via override de `OnConfiguring`/convenção compartilhada. Schemas: `contas`, `empresas`, `emissao`, `documentos`. **Não criar o schema `leitura`** — pertence à Emissão e nasce só com `NotaConsulta` (D-2026-07-01-10); deixar comentário no `EmissaoDbContext` apontando isso.

Criar `IDesignTimeDbContextFactory<T>` por contexto (para `dotnet ef` funcionar sem o host completo).

- [ ] **Step 9: Gerar e aplicar as 4 migrations iniciais vazias**

Run (repetir por contexto, ajustando `--context`):
```bash
dotnet ef migrations add InitialCreate --project src/Modules/Emissao/NotaFiscalHub.Modules.Emissao.Infrastructure --startup-project src/Api/NotaFiscalHub.Api --context EmissaoDbContext
```
Expected: migration gerada em `Infrastructure/Migrations/` do módulo, sem nenhuma tabela de negócio (schema vazio).

Run (contra PostgreSQL local ou Testcontainers):
```bash
dotnet ef database update --project src/Modules/Emissao/NotaFiscalHub.Modules.Emissao.Infrastructure --startup-project src/Api/NotaFiscalHub.Api --context EmissaoDbContext
```
Expected: schema `emissao` criado no banco com `__ef_migrations_history` dentro dele (não no `public`).

Repetir para os outros 3 contextos.

- [ ] **Step 10: CI mínimo (build + testes de arquitetura)**

Criar (ou estender, se B9 ainda não rodou) `.github/workflows/ci.yml` com steps `restore` → `build` → `test` (rodando só `NotaFiscalHub.ArchitectureTests` por ora — o pipeline completo é a Tarefa 9).

- [ ] **Step 11: Verificar os 8 critérios de aceite da spec B1**

Run: `dotnet build NotaFiscalHub.sln` (verde, zero warnings) · `dotnet test tests/NotaFiscalHub.ArchitectureTests` (verde) · `dotnet ef migrations list` por contexto (mostra a `InitialCreate`) · grep de `namespace` nos `.cs` novos (100% seguem a convenção) · `git diff --stat -- old/` (vazio — legado intocado) · build do legado (Step 2, repetir).

- [ ] **Step 12: Commit**

```bash
git add NotaFiscalHub.sln Directory.Build.props Directory.Packages.props src/Api src/Worker src/BuildingBlocks src/Modules tests/NotaFiscalHub.ArchitectureTests .github/workflows/ci.yml
git commit -m "feat: estrutura da solution NotaFiscalHub (modulos + kernel + hosts)"
```

---

### Tarefa 2: Tenant context + filtro global `conta_id`

**Spec de origem:** `docs/superpowers/specs/tarefas/B2-tenant-context.md`

**Depende de:** Tarefa 1 (projetos `BuildingBlocks.*`, hosts, `ArchitectureTests` existentes).

**Files:**
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Kernel/Tenancy/{ITenantContext.cs,ITenantScopeFactory.cs,ITenantScope.cs,ITenantScopedEntity.cs,AmbientTenantContext.cs,TenantNaoResolvidoException.cs,TenantMismatchException.cs}`
- Modify: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Persistence/ModuleDbContext.cs` (torna-se `TenantDbContext` com query filter + interceptor)
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Persistence/TenantWriteInterceptor.cs`
- Create: `src/Api/NotaFiscalHub.Api/Authentication/{TenantResolutionMiddleware.cs,ITenantResolver.cs,StubTenantResolver.cs}`
- Create: `src/Worker/NotaFiscalHub.Worker/TenantJobExecutor.cs`
- Create: `tests/NotaFiscalHub.BuildingBlocks.Kernel.UnitTests/Tenancy/AmbientTenantContextTests.cs`
- Create: `tests/NotaFiscalHub.ArchitectureTests/TenantScopedEntityTests.cs`
- Create: `tests/NotaFiscalHub.IntegrationTests/Tenancy/TenantIsolationTests.cs`

**Interfaces:**
- Produces: `ITenantContext { Guid ContaId; bool HasTenant; bool IsSystemScope; IReadOnlyCollection<Guid>? EmpresasPermitidas; }` — consumido por todas as tarefas seguintes e por todos os módulos de negócio das Fases 1+.
- Produces: `ITenantScopeFactory { ITenantScope BeginTenantScope(Guid contaId); ITenantScope BeginSystemScope(string motivo, string origem); }` — consumido pela Tarefa 3 (dispatcher outbox), Tarefa 5 (handler de auditoria) e todo job do Worker nas fases seguintes.
- Produces: `ITenantScopedEntity { Guid ContaId { get; } }` — marker consumido pela Tarefa 6 (T5/T6 do NetArchTest).
- Produces: `TenantDbContext` (substitui `ModuleDbContext` como base) — consumido por todo `<Modulo>DbContext`.
- Consumes: `ModuleDbContext` da Tarefa 1 (vira a classe que esta tarefa estende).

- [ ] **Step 1: Teste primeiro — `AmbientTenantContext` sem escopo lança**

```csharp
using NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;
using Xunit;

namespace NotaFiscalHub.BuildingBlocks.Kernel.UnitTests.Tenancy;

public class AmbientTenantContextTests
{
    [Fact]
    public void ContaId_SemEscopoAtivo_LancaTenantNaoResolvido()
    {
        var context = new AmbientTenantContext();
        Assert.Throws<TenantNaoResolvidoException>(() => _ = context.ContaId);
    }
}
```

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Kernel.UnitTests --filter ContaId_SemEscopoAtivo_LancaTenantNaoResolvido`
Expected: FAIL (tipo `AmbientTenantContext` não existe).

- [ ] **Step 2: Implementar os contratos e `AmbientTenantContext`**

```csharp
namespace NotaFiscalHub.BuildingBlocks.Kernel.Tenancy;

public interface ITenantScopedEntity { Guid ContaId { get; } }

public interface ITenantContext
{
    Guid ContaId { get; }
    bool HasTenant { get; }
    bool IsSystemScope { get; }
    IReadOnlyCollection<Guid>? EmpresasPermitidas { get; }
}

public interface ITenantScopeFactory
{
    ITenantScope BeginTenantScope(Guid contaId);
    ITenantScope BeginSystemScope(string motivo, string origem);
}

public interface ITenantScope : IDisposable;
```

`AmbientTenantContext` implementa `ITenantContext` e `ITenantScopeFactory` com `AsyncLocal<TenantScopeState>` (pilha para aninhamento); `BeginTenantScope(Guid.Empty)` lança `ArgumentException`; `Dispose()` do escopo restaura o estado anterior.

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Kernel.UnitTests --filter ContaId_SemEscopoAtivo_LancaTenantNaoResolvido`
Expected: PASS.

- [ ] **Step 3: Testes de aninhamento e `BeginSystemScope`**

```csharp
[Fact]
public void BeginTenantScope_Aninhado_RestauraEscopoAnteriorAoDispose()
{
    var context = new AmbientTenantContext();
    var contaA = Guid.NewGuid();
    var contaB = Guid.NewGuid();
    using (context.BeginTenantScope(contaA))
    {
        using (context.BeginTenantScope(contaB))
        {
            Assert.Equal(contaB, context.ContaId);
        }
        Assert.Equal(contaA, context.ContaId);
    }
    Assert.False(context.HasTenant);
}

[Fact]
public void BeginTenantScope_GuidEmpty_LancaArgumentException()
{
    var context = new AmbientTenantContext();
    Assert.Throws<ArgumentException>(() => context.BeginTenantScope(Guid.Empty));
}

[Fact]
public void BeginSystemScope_MarcaIsSystemScope()
{
    var context = new AmbientTenantContext();
    using (context.BeginSystemScope("seed", "migration"))
    {
        Assert.True(context.IsSystemScope);
    }
    Assert.False(context.IsSystemScope);
}
```

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Kernel.UnitTests --filter "FullyQualifiedName~AmbientTenantContextTests"`
Expected: FAIL nos 3 novos (comportamento ainda não implementado) → implementar → PASS.

- [ ] **Step 4: `TenantDbContext` base com query filter (teste com EF InMemory primeiro)**

```csharp
[Fact]
public void QueryFilter_SemEscopo_LancaAoConsultar()
{
    using var db = CriarContextoDeTeste(tenantContext: new AmbientTenantContext());
    Assert.Throws<TenantNaoResolvidoException>(() => db.EntidadesDeTeste.ToList());
}
```

Run: FAIL (tipo `TenantDbContext` ainda não existe/expõe o comportamento).

Implementar `TenantDbContext : ModuleDbContext` em `BuildingBlocks.Persistence`:

```csharp
public abstract class TenantDbContext(DbContextOptions options, ITenantContext tenantContext) : ModuleDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScopedEntity).IsAssignableFrom(entityType.ClrType)) continue;
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter("Tenant", BuildTenantFilter(entityType.ClrType))
                .Property(nameof(ITenantScopedEntity.ContaId)).HasColumnName("conta_id").IsRequired();
        }
    }
    // BuildTenantFilter monta a expression `e => tenantContext.ContaId == e.ContaId`
    // — a leitura de tenantContext.ContaId SEM escopo já lança TenantNaoResolvidoException (fail-closed).
    // Filtro NOMEADO "Tenant" (EF Core 10): permite IgnoreQueryFilters(["Tenant"]) seletivo (uso restrito ao
    // kernel — Step 12) e é o que o teste T6 da suite NetArchTest (Tarefa 6) inspeciona por nome.
}
```

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Kernel.UnitTests --filter QueryFilter_SemEscopo_LancaAoConsultar`
Expected: PASS.

- [ ] **Step 5: Interceptor de escrita — teste primeiro**

```csharp
[Fact]
public void SaveChanges_EntidadeDeOutraConta_LancaTenantMismatch()
{
    using var db = CriarContextoDeTeste(tenantContext: EscopoDaContaA());
    db.EntidadesDeTeste.Add(new EntidadeDeTeste { ContaId = ContaB });
    Assert.Throws<TenantMismatchException>(() => db.SaveChanges());
}

[Fact]
public void SaveChanges_EntidadeNova_EstampaContaIdDoContexto()
{
    using var db = CriarContextoDeTeste(tenantContext: EscopoDaContaA());
    var entidade = new EntidadeDeTeste();
    db.EntidadesDeTeste.Add(entidade);
    db.SaveChanges();
    Assert.Equal(ContaA, entidade.ContaId);
}
```

Run: FAIL → implementar `TenantWriteInterceptor : SaveChangesInterceptor` (estampa `ContaId` em `Added` se `Guid.Empty`; valida mismatch fora de system scope) → registrar no `TenantDbContext.OnConfiguring` → Run novamente.
Expected: PASS.

- [ ] **Step 6: Teste de arquitetura — entidade tenant-scoped sem marker (RED primeiro)**

```csharp
[Fact]
public void TenantScoped_ExigeContaId()
{
    // varre o IModel de cada DbContext concreto; toda entidade mapeada
    // implementa ITenantScopedEntity OU está na allowlist versionada (Plano, Outbox, Inbox).
    var ofensores = TodosOsDbContexts()
        .SelectMany(ctx => ctx.Model.GetEntityTypes())
        .Where(et => !typeof(ITenantScopedEntity).IsAssignableFrom(et.ClrType))
        .Where(et => !AllowlistEntidadesGlobais.Contains(et.ClrType))
        .Select(et => et.ClrType.Name)
        .ToList();

    Assert.Empty(ofensores);
}
```

Adicionar temporariamente uma entidade fixture sem `ITenantScopedEntity` a um DbContext de teste.

Run: FAIL (RED) → remover a fixture → Run novamente → PASS (GREEN). Registrar RED/GREEN no PR.

- [ ] **Step 7: Middleware de resolução (stub) na API**

```csharp
namespace NotaFiscalHub.Api.Authentication;

public interface ITenantResolver
{
    Task<(Guid ContaId, IReadOnlyCollection<Guid>? EmpresasPermitidas)?> ResolveAsync(HttpContext context);
}

public sealed class StubTenantResolver : ITenantResolver
{
    // SOMENTE em Development/testes: lê X-Nfh-Conta-Id (GUID). Fora disso, retorna null -> 401.
    public Task<(Guid, IReadOnlyCollection<Guid>?)?> ResolveAsync(HttpContext context) { /* ... */ }
}
```

`TenantResolutionMiddleware` chama o resolver, abre `BeginTenantScope(contaId)` e garante dispose ao fim do request; endpoints `/alive`/`/health` ficam em allowlist e não passam pelo middleware.

- [ ] **Step 8: Teste de integração — 2 requests concorrentes não vazam `AsyncLocal`**

```csharp
[Fact]
public async Task DoisRequestsConcorrentes_NaoVazamEscopoDeTenant()
{
    var (respostaA, respostaB) = await (
        ClienteComHeader(ContaA).GetAsync("/teste/tenant-atual"),
        ClienteComHeader(ContaB).GetAsync("/teste/tenant-atual"));

    Assert.Equal(ContaA.ToString(), await respostaA.Content.ReadAsStringAsync());
    Assert.Equal(ContaB.ToString(), await respostaB.Content.ReadAsStringAsync());
}
```

Run (WebApplicationFactory): FAIL até o middleware estar corretamente registrado com escopo por request → implementar → PASS.

- [ ] **Step 9: `TenantJobExecutor` para o Worker + teste**

```csharp
public sealed class TenantJobExecutor(ITenantScopeFactory scopeFactory)
{
    public async Task Execute(Guid contaId, Func<Task> job)
    {
        using var scope = scopeFactory.BeginTenantScope(contaId);
        await job();
    }
}
```

```csharp
[Fact]
public async Task Execute_SemEscopoNoJob_JobQueChamaContaId_Lanca()
{
    // job que resolve um serviço tenant-scoped diretamente (sem passar pelo executor) falha —
    // prova que "job sem escopo é erro, não processa tudo".
}
```

Run: FAIL → implementar → PASS.

- [ ] **Step 10: Teste de integração Testcontainers — conta A nunca lê dado da conta B**

Seed de entidade tenant-scoped de teste com contas A e B num PostgreSQL real (Testcontainers); asserções: `Find`, `Include`, projeção, `AsNoTracking`, `ToQueryString()` contém `conta_id` no SQL gerado.

Run: `dotnet test tests/NotaFiscalHub.IntegrationTests --filter "FullyQualifiedName~TenantIsolationTests"`
Expected: PASS (evidência: print do `ToQueryString()` no PR).

- [ ] **Step 11: `BeginSystemScope` emite log estruturado — teste com sink em memória**

```csharp
[Fact]
public void BeginSystemScope_EmiteLogTenantScopeBypassed()
{
    var sink = new InMemorySink();
    using (scopeFactory.BeginSystemScope(motivo: "seed", origem: "migration")) { }
    Assert.Contains(sink.Events, e => e.MessageTemplate.Text.Contains("TenantScopeBypassed"));
}
```

Run: FAIL → implementar o log no `BeginSystemScope` → PASS.

- [ ] **Step 12: Verificar os 8 critérios de aceite da spec B2**

Run: `dotnet test` das 3 suítes (unidade, arquitetura, integração) — todas verdes. Confirmar zero uso de `IgnoreQueryFilters` fora de `BuildingBlocks` (grep).

- [ ] **Step 13: Commit**

```bash
git add src/BuildingBlocks src/Api/NotaFiscalHub.Api/Authentication src/Worker tests/NotaFiscalHub.BuildingBlocks.Kernel.UnitTests tests/NotaFiscalHub.ArchitectureTests tests/NotaFiscalHub.IntegrationTests
git commit -m "feat: tenant context com filtro global fail-closed e BeginTenantScope"
```

---

### Tarefa 3: Biblioteca Outbox/Inbox por módulo

**Spec de origem:** `docs/superpowers/specs/tarefas/B3-outbox-inbox.md`

**Depende de:** Tarefa 1 (estrutura), Tarefa 2 (`ITenantContext`/`BeginTenantScope`). Se a Tarefa 7 (CorrelationId) ainda não rodou, usar `Activity.Current?.RootId` como interface provisória (registrar dívida no PR).

**Files:**
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Messaging.Abstractions/{EventoIntegracao.cs,IOutboxPublisher.cs,IInboxHandler.cs,MensagemContexto.cs}`
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Messaging/{OutboxMessage.cs,InboxMessage.cs,OutboxPublisher.cs,OutboxDispatcher.cs,ServiceCollectionExtensions.cs,RetentionCleanupService.cs}`
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Messaging.TestKit/OutboxTestHarness.cs`
- Create: `tests/NotaFiscalHub.BuildingBlocks.Messaging.UnitTests/{BackoffCalculatorTests.cs,OrphanEventPolicyTests.cs}`
- Create: `tests/NotaFiscalHub.IntegrationTests/Messaging/{OutboxTransactionalityTests.cs,InboxDedupeTests.cs,OutboxConcurrencyTests.cs,RetentionTests.cs,TenantScopeDispatchTests.cs}`
- Create: `tests/NotaFiscalHub.ArchitectureTests/EventPayloadRulesTests.cs`

**Interfaces:**
- Consumes: `ITenantContext.ContaId`, `ITenantScopeFactory.BeginTenantScope` (Tarefa 2).
- Produces: `IOutboxPublisher.Publicar<T>(T evento)` — consumido por todo handler de domínio nas Fases 1–4.
- Produces: `IInboxHandler<T>.HandleAsync(T evento, MensagemContexto ctx, CancellationToken ct)` — implementado pela Tarefa 5 (auditoria) e por handlers de negócio futuros.
- Produces: `EventoIntegracao { Guid MessageId; Guid? ContaId; DateTimeOffset OcorridoEm; }` — tipo base consumido por todo evento de integração do produto.
- Produces: `AddOutboxInbox<TDbContext>(schema, opções)`, `AddInboxHandler<TEvento,THandler>()`, `AddEventoDePlataforma<TEvento>()` — extensões de DI consumidas pelos hosts.

- [ ] **Step 1: Contratos em `Messaging.Abstractions` (sem EF)**

```csharp
namespace NotaFiscalHub.BuildingBlocks.Messaging.Abstractions;

public abstract record EventoIntegracao
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();
    public Guid? ContaId { get; init; }
    public DateTimeOffset OcorridoEm { get; init; } = DateTimeOffset.UtcNow;
}

public interface IOutboxPublisher
{
    void Publicar<T>(T evento) where T : EventoIntegracao;
}

public sealed record MensagemContexto(Guid MessageId, string CorrelationId, int Tentativa);

public interface IInboxHandler<in T> where T : EventoIntegracao
{
    Task HandleAsync(T evento, MensagemContexto ctx, CancellationToken ct);
}
```

- [ ] **Step 2: Teste de unidade — cálculo de backoff (RED primeiro)**

```csharp
[Theory]
[InlineData(1, 5)]   // tentativa 1: base 5s
[InlineData(2, 10)]  // tentativa 2: 5s * 2^1
[InlineData(4, 40)]  // tentativa 4: 5s * 2^3
public void CalcularBackoff_CresceExponencialmenteComTeto1Hora(int tentativa, int segundosMinimosEsperados)
{
    var backoff = BackoffCalculator.Calcular(tentativa, seed: 42); // seed fixa para o teste (jitter determinístico)
    Assert.True(backoff.TotalSeconds >= segundosMinimosEsperados);
    Assert.True(backoff <= TimeSpan.FromHours(1));
}
```

Run: FAIL (tipo não existe) → implementar `BackoffCalculator.Calcular(tentativa, seed)` com base 5s, fator 2, teto 1h, jitter — → PASS.

- [ ] **Step 3: Tabelas `Outbox`/`Inbox` — migration por schema**

```csharp
public static class OutboxInboxModelBuilderExtensions
{
    public static void AplicarOutboxInbox(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox", schema);
            b.HasKey(x => x.Id); // Id = MessageId
            b.HasIndex(x => new { x.Status, x.ProximaTentativaEm })
                .HasFilter("status = 'Pendente'");
        });
        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox", schema);
            b.HasKey(x => new { x.MessageId, x.Handler }); // dedupe por handler
        });
    }
}
```

Colunas conforme spec B3 passo 2 (`tipo_evento`, `payload` jsonb, `conta_id`, `correlation_id`, `ocorrido_em`, `status` enum `Pendente|Processada|SemHandler|Poison`, `tentativas`, `proxima_tentativa_em`, `processada_em`, `erro_ultimo`).

Aplicar em cada `<Modulo>DbContext.OnModelCreating`: `modelBuilder.AplicarOutboxInbox("<schema>")`. Gerar e aplicar migration por contexto (mesmo processo da Tarefa 1, Step 9).

- [ ] **Step 4: `OutboxPublisher` transacional — teste de integração primeiro**

```csharp
[Fact]
public async Task Publicar_DentroDaTransacaoDoAgregado_RollbackNaoDeixaLinha()
{
    using var transacao = await db.Database.BeginTransactionAsync();
    publisher.Publicar(new EventoDeTeste { ContaId = contaId });
    await db.SaveChangesAsync();
    await transacao.RollbackAsync();

    Assert.Empty(await db.Set<OutboxMessage>().ToListAsync());
}

[Fact]
public async Task Publicar_Commit_ExatamenteUmaLinha()
{
    publisher.Publicar(new EventoDeTeste { ContaId = contaId });
    await db.SaveChangesAsync();

    Assert.Single(await db.Set<OutboxMessage>().ToListAsync());
}
```

Run: FAIL → implementar `OutboxPublisher : IOutboxPublisher` (usa o `DbContext` corrente via DI scoped; captura `ContaId` de `ITenantContext`; sem transação ativa → exceção) → PASS.

- [ ] **Step 5: Dispatcher — evento sem handler vira `SemHandler`, nunca `Processada` (RED primeiro)**

```csharp
[Fact]
public async Task Dispatcher_EventoSemHandlerRegistrado_TerminaSemHandler_NuncaProcessada()
{
    await PublicarEventoSemHandlerRegistrado();
    await dispatcher.ProcessarLoteAsync();

    var mensagem = await db.Set<OutboxMessage>().SingleAsync();
    Assert.Equal(OutboxStatus.SemHandler, mensagem.Status);
    Assert.NotEqual(OutboxStatus.Processada, mensagem.Status);
}
```

Run: FAIL → implementar `OutboxDispatcher<TDbContext>` (polling `FOR UPDATE SKIP LOCKED`, resolve handlers via registry, zero handlers resolvidos → `SemHandler` + log Warning + métrica) → PASS.

- [ ] **Step 6: Dedupe de Inbox — mesma mensagem 2x, handler executa 1x (RED primeiro)**

```csharp
[Fact]
public async Task Handler_MesmaMensagem2Vezes_EfeitoExecutaUmaVez()
{
    var contador = new ContadorDeExecucoes();
    RegistrarHandlerQueIncrementa(contador);

    await DespacharDuasVezes(mesmoMessageId);

    Assert.Equal(1, contador.Total);
}
```

Run: FAIL → implementar o `INSERT ... ON CONFLICT DO NOTHING` na Inbox como dedupe primário (não check-then-insert) → PASS.

- [ ] **Step 7: Regra de escopo de tenant no dispatch (RED primeiro)**

```csharp
[Fact]
public async Task Dispatcher_ContaIdNulo_TipoNaoRegistradoComoPlataforma_VaiPraPoison()
{
    await PublicarEventoTenantScopedComContaIdNulo();
    await dispatcher.ProcessarLoteAsync();

    var mensagem = await db.Set<OutboxMessage>().SingleAsync();
    Assert.Equal(OutboxStatus.Poison, mensagem.Status);
    Assert.Contains("evento tenant-scoped sem conta_id", mensagem.ErroUltimo);
}

[Fact]
public async Task Dispatcher_EventoDePlataforma_ExecutaSemBeginTenantScope()
{
    RegistrarComoEventoDePlataforma<EventoDePlataformaDeTeste>();
    await PublicarEventoDePlataforma();
    await dispatcher.ProcessarLoteAsync();

    Assert.False(_tenantContextObservadoNoHandler.HasTenant);
}
```

Run: FAIL → implementar a regra (Contexto preenchido → `BeginTenantScope` obrigatório; nulo + não registrado → `Poison`; nulo + registrado via `AddEventoDePlataforma<T>()` → executa sem escopo) → PASS.

- [ ] **Step 8: Retry/poison — 10ª falha vira Poison, demais mensagens seguem (RED primeiro)**

```csharp
[Fact]
public async Task Handler_FalhaRepetidamente_10aTentativaViraPoison_FilaContinua()
{
    RegistrarHandlerQueSempreLanca();
    await PublicarDoisEventos(); // A (vai falhar 10x) e B (handler ok)

    for (var i = 0; i < 10; i++) await dispatcher.ProcessarLoteAsync();

    Assert.Equal(OutboxStatus.Poison, StatusDoEvento("A"));
    Assert.Equal(OutboxStatus.Processada, StatusDoEvento("B"));
}
```

Run: FAIL → implementar retry com backoff (Step 2) + limite `MaxTentativas=10` → `Poison` + métrica `outbox_mensagens_poison_total` → PASS.

- [ ] **Step 9: Concorrência — 2 dispatchers não processam a mesma mensagem 2x (RED primeiro)**

```csharp
[Fact]
public async Task DoisDispatchersConcorrentes_MensagemProcessadaUmaVezSo()
{
    var contador = new ContadorDeExecucoes();
    RegistrarHandlerQueIncrementa(contador);
    await PublicarUmEvento();

    await Task.WhenAll(dispatcher1.ProcessarLoteAsync(), dispatcher2.ProcessarLoteAsync());

    Assert.Equal(1, contador.Total);
}
```

Run (Testcontainers, 2 hosts lógicos contra o mesmo banco): FAIL até `SELECT ... FOR UPDATE SKIP LOCKED` estar correto → PASS.

- [ ] **Step 10: Retenção — `Processada` > 7 dias apagada; `Poison`/`SemHandler` preservados mesmo antigos (RED primeiro)**

```csharp
[Fact]
public async Task Limpeza_RemoveProcessadaAntiga_PreservaPoisonESemHandlerAntigos()
{
    PlantarMensagem(status: OutboxStatus.Processada, processadaEm: DateTimeOffset.UtcNow.AddDays(-8));
    PlantarMensagem(status: OutboxStatus.Poison, criadaEm: DateTimeOffset.UtcNow.AddDays(-30));
    PlantarMensagem(status: OutboxStatus.SemHandler, criadaEm: DateTimeOffset.UtcNow.AddDays(-30));

    await retentionService.ExecutarUmCicloAsync();

    Assert.Equal(2, await db.Set<OutboxMessage>().CountAsync());
}
```

Run: FAIL → implementar `RetentionCleanupService` (hosted service, lotes de 1000, `Processada`>7d, `Inbox`>30d, nunca `Poison`/`SemHandler`) → PASS.

- [ ] **Step 11: Teste de arquitetura — payload de evento só identificadores (RED primeiro, os dois lados)**

```csharp
[Fact]
public void EventoIntegracao_CertificadoId_Passa_XmlAutorizado_Falha()
{
    // fixture: EventoComCertificadoId (Guid CertificadoId) — deve passar
    // fixture: EventoComXmlAutorizado (string XmlAutorizado) — deve falhar
    var resultado = EventPayloadRules.Validar(typeof(EventoComXmlAutorizado));
    Assert.False(resultado.IsSuccessful);

    var resultadoOk = EventPayloadRules.Validar(typeof(EventoComCertificadoId));
    Assert.True(resultadoOk.IsSuccessful);
}
```

Run: FAIL (regra ainda não existe) → implementar a regra NetArchTest (denylist de nomes `(?i)(xml|pdf|cpf|senha|certificado|payload)`, exceto `Guid`/`Guid?` terminado em `Id`, mais a allowlist `PropriedadesDeEventoPermitidas`) → PASS.

- [ ] **Step 12: Test-kit para as fases seguintes**

```csharp
namespace NotaFiscalHub.BuildingBlocks.Messaging.TestKit;

public sealed class OutboxTestHarness
{
    // despacha em memória com duplicação e embaralhamento determinísticos (seed injetada)
    // — usado pelos módulos nas Fases 1-4 para provar tolerância a duplicata/fora-de-ordem.
}
```

- [ ] **Step 13: Verificar os 9 critérios de aceite da spec B3**

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Messaging.UnitTests tests/NotaFiscalHub.IntegrationTests/Messaging tests/NotaFiscalHub.ArchitectureTests --filter "FullyQualifiedName~Messaging|EventPayload"`
Expected: todos verdes.

- [ ] **Step 14: Commit**

```bash
git add src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Messaging* tests/NotaFiscalHub.BuildingBlocks.Messaging.UnitTests tests/NotaFiscalHub.IntegrationTests/Messaging tests/NotaFiscalHub.ArchitectureTests/EventPayloadRulesTests.cs
git commit -m "feat: biblioteca outbox/inbox por modulo com dedupe e evento sem handler explicito"
```

---

### Tarefa 4: Middleware de Idempotency-Key

**Spec de origem:** `docs/superpowers/specs/tarefas/B4-idempotency-key.md`

**Depende de:** Tarefa 2 (`ITenantContext` fornece `ContaId` na borda; ambiente da credencial pode ser fake no host de teste — a integração real com API keys fecha na Fase 1). `TimeProvider` injetável do kernel para testes de expiração.

**Files:**
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Idempotency/{IdempotencyRecord.cs,IIdempotencyStore.cs,IdempotencyStore.cs,IdempotencyEndpointFilter.cs,RequireIdempotencyKeyAttribute.cs,IdempotencyExpirationJob.cs,ProblemDetailsFactory.Idempotency.cs}`
- Create: `tests/NotaFiscalHub.BuildingBlocks.Idempotency.UnitTests/{KeyValidationTests.cs,DecisionMachineTests.cs}`
- Create: `tests/NotaFiscalHub.IntegrationTests/Idempotency/{ReplayTests.cs,ConflictTests.cs,ConcurrencyTests.cs,OrphanTakeoverTests.cs,ExpirationTests.cs}`
- Create: `tests/NotaFiscalHub.ArchitectureTests/IdempotencyIsolationTests.cs`

**Interfaces:**
- Consumes: `ITenantContext.ContaId` (Tarefa 2), `TimeProvider` (kernel).
- Produces: `IIdempotencyStore { Task<IdempotencyBeginResult> BeginAsync(...); CompleteAsync; ReleaseAsync }` e o endpoint filter `RequireIdempotencyKey()`/`AcceptIdempotencyKey()` — consumido por `POST /v1/nfce` na Fase 3.

- [ ] **Step 1: Teste de unidade — validação da key (RED primeiro)**

```csharp
[Theory]
[InlineData("", false)]
[InlineData("a", true)]
[InlineData("chave-valida-123", true)]
public void ValidarKey_RetornaConformeTamanhoEChars(string key, bool esperadoValido)
{
    Assert.Equal(esperadoValido, IdempotencyKeyValidator.EhValida(key));
}
```

Run: FAIL → implementar `IdempotencyKeyValidator.EhValida` (1–255 chars visíveis) → PASS.

- [ ] **Step 2: Teste de unidade — hash sobre bytes crus (RED primeiro)**

```csharp
[Fact]
public void CalcularHash_CorposComWhitespaceDiferente_GeramHashesDistintos()
{
    var hashA = PayloadHasher.Sha256(Encoding.UTF8.GetBytes("{\"a\":1}"));
    var hashB = PayloadHasher.Sha256(Encoding.UTF8.GetBytes("{ \"a\": 1 }"));
    Assert.NotEqual(hashA, hashB);
}
```

Run: FAIL → implementar `PayloadHasher.Sha256(byte[])` → PASS.

- [ ] **Step 3: Tabela `kernel.idempotency_registro`**

```csharp
public sealed record IdempotencyRecord(
    Guid ContaId, string Ambiente, string Key, string Rota, string PayloadHashSha256,
    IdempotencyState Estado, int? RespostaStatus, string? RespostaCorpo, string? RespostaContentType,
    string? RespostaLocation, DateTimeOffset CriadaEm, DateTimeOffset ExpiraEm);

public enum IdempotencyState { EmProcessamento = 1, Concluida = 2 }
```

Migration: PK composta `(conta_id, ambiente, rota, key)`; índice em `expira_em`. Schema `kernel` (schema próprio da tabela, dono = kernel — mesma exceção documentada que a Tarefa 5 usa para `auditoria`).

- [ ] **Step 4: `IIdempotencyStore.BeginAsync` — máquina de decisão com fake (RED primeiro)**

```csharp
public enum IdempotencyBeginOutcome { Inserted, ReplayConcluida, ConflitoHashDiferente, CorridaEmProcessamento }
public sealed record IdempotencyBeginResult(IdempotencyBeginOutcome Outcome, IdempotencyRecord? Existente);

[Theory]
[InlineData("inexistente", "hash-novo", IdempotencyBeginOutcome.Inserted)]
[InlineData("Concluida-hash-igual", "hash-igual", IdempotencyBeginOutcome.ReplayConcluida)]
[InlineData("Concluida-hash-diferente", "hash-novo", IdempotencyBeginOutcome.ConflitoHashDiferente)]
[InlineData("EmProcessamento-hash-igual", "hash-igual", IdempotencyBeginOutcome.CorridaEmProcessamento)]
public void BeginAsync_Decide_Inserted_Replay_Conflito_OuCorrida(string estadoExistente, string hashNovo, IdempotencyBeginOutcome esperado)
{
    var store = new IdempotencyStore(FakeRepositorioCom(estadoExistente));
    var resultado = store.BeginAsync(NovoRegistro(hashNovo), CancellationToken.None).Result;

    Assert.Equal(esperado, resultado.Outcome);
}
```

Run: FAIL (tipo `IdempotencyBeginResult`/`IdempotencyBeginOutcome` não existe) → implementar `IIdempotencyStore.BeginAsync` retornando `Task<IdempotencyBeginResult>` (sem endpoint filter ainda, só a função pura) → PASS.

- [ ] **Step 5: Endpoint filter — replay devolve resposta original (RED primeiro, integração)**

```csharp
[Fact]
public async Task DoisPostsSequenciais_MesmaKeyMesmoCorpo_HandlerExecutaUmaVez_SegundaEhReplay()
{
    var resposta1 = await cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "abc"));
    var resposta2 = await cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "abc"));

    Assert.Equal(1, contadorDoHandlerFake.Total);
    Assert.Equal(await resposta1.Content.ReadAsStringAsync(), await resposta2.Content.ReadAsStringAsync());
    Assert.Equal("true", resposta2.Headers.GetValues("Idempotency-Replayed").Single());
}
```

Run: FAIL → implementar `IdempotencyEndpointFilter` (lê corpo bufferizado, hash, `BeginAsync`, captura resposta, `CompleteAsync` se status < 500, `ReleaseAsync` se ≥ 500) → PASS.

- [ ] **Step 6: 400 sem key obrigatória; 409 payload diferente (RED primeiro)**

```csharp
[Fact]
public async Task PostSemIdempotencyKey_Retorna400ComProblemType()
{
    var resposta = await cliente.PostAsync("/v1/nfce", corpo); // sem header
    Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    var problem = await resposta.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal("idempotency_key_obrigatoria", problem!.Type);
}

[Fact]
public async Task MesmaKeyCorpoDiferente_Retorna409ComTraceId()
{
    await cliente.PostAsync("/v1/nfce", corpoA, ComHeader("Idempotency-Key", "x"));
    var resposta = await cliente.PostAsync("/v1/nfce", corpoB, ComHeader("Idempotency-Key", "x"));

    Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
    var problem = await resposta.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal("idempotency_key_conflito", problem!.Type);
    Assert.NotNull(problem.Extensions["traceId"]);
}
```

Run: FAIL → completar o filter com os problem types RFC 7807 → PASS.

- [ ] **Step 7: Corrida concorrente — N≥8 requests, 1 executa (RED primeiro)**

```csharp
[Fact]
public async Task NRequisicoesParalelas_MesmaKey_ExatamenteUmaExecutaOHandler()
{
    var tarefas = Enumerable.Range(0, 8).Select(_ => cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "y")));
    await Task.WhenAll(tarefas);

    Assert.Equal(1, contadorDoHandlerFake.Total);
}
```

Run (com barreira de sincronização no handler fake para forçar a corrida real): FAIL → garantir que `BeginAsync` usa `INSERT` com PK única como fonte de verdade da corrida (perdedor recebe `409 requisicao_em_processamento` + `Retry-After: 2`) → PASS.

- [ ] **Step 8: Órfão — takeover atômico (RED primeiro, `TimeProvider` injetado)**

```csharp
[Fact]
public async Task RegistroOrfaoMaisVelhoQueOrphanTimeout_NovoRequestFazTakeover()
{
    await PlantarRegistroEmProcessamento(criadaEm: relogio.GetUtcNow() - TimeSpan.FromSeconds(61));
    relogio.Avancar(TimeSpan.FromSeconds(1));

    var resposta = await cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "orfa"));

    Assert.Equal(1, contadorDoHandlerFake.Total); // executou de novo (takeover)
}

[Fact]
public async Task TakeoverConcorrente_ApenasUmVence()
{
    await PlantarRegistroEmProcessamento(criadaEm: relogio.GetUtcNow() - TimeSpan.FromSeconds(61));
    var tarefas = Enumerable.Range(0, 4).Select(_ => cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "orfa2")));
    var respostas = await Task.WhenAll(tarefas);

    Assert.Equal(1, contadorDoHandlerFake.Total);
    Assert.Contains(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
}
```

Run: FAIL → implementar o `UPDATE ... WHERE estado='EmProcessamento' AND criada_em < now() - @orphanTimeout` atômico (1 linha afetada = venceu) → PASS.

- [ ] **Step 9: Expiração — TTL 24h + job por tenant (RED primeiro)**

```csharp
[Fact]
public async Task AposExpiraEm_MesmaKeyExecutaHandlerDeNovo()
{
    await PlantarRegistroConcluida(expiraEm: relogio.GetUtcNow() - TimeSpan.FromMinutes(1));

    var resposta = await cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "expirada"));

    Assert.Equal(1, contadorDoHandlerFake.Total);
}

[Fact]
public async Task JobDeExpiracao_AbreBeginTenantScopePorConta_RemoveSoVencidos()
{
    await PlantarRegistroConcluida(contaId: contaA, expiraEm: relogio.GetUtcNow() - TimeSpan.FromDays(1));
    await PlantarRegistroConcluida(contaId: contaA, expiraEm: relogio.GetUtcNow() + TimeSpan.FromDays(1));

    await job.ExecutarUmCicloAsync();

    Assert.Single(await db.Set<IdempotencyRecordEntity>().Where(r => r.ContaId == contaA).ToListAsync());
    Assert.True(spyDeEscopo.FoiAbertoPara(contaA));
}
```

Run: FAIL → implementar `IdempotencyExpirationJob` (varredura por tenant, `BeginTenantScope(contaId)` obrigatório — Global Constraint) → PASS.

- [ ] **Step 10: Escopo por rota e por credencial (RED primeiro)**

```csharp
[Fact]
public async Task MesmaKeyRotasDiferentes_DoisRegistrosIndependentes()
{
    await cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "z"));
    var resposta = await cliente.PostAsync("/v1/inutilizacoes", corpo, ComHeader("Idempotency-Key", "z"));

    Assert.NotEqual(HttpStatusCode.Conflict, resposta.StatusCode); // executa normalmente, sem 409 nem replay cruzado
}

[Fact]
public async Task MesmaKeyAmbientesDiferentes_DoisRegistrosIndependentes()
{
    await cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "w"), ComCredencial("nfh_test_"));
    var resposta = await cliente.PostAsync("/v1/nfce", corpo, ComHeader("Idempotency-Key", "w"), ComCredencial("nfh_live_"));

    Assert.False(resposta.Headers.Contains("Idempotency-Replayed"));
}

[Fact]
public async Task RotaGet_IgnoraHeaderIdempotencyKey_MesmoSeEnviado()
{
    var resposta1 = await cliente.GetAsync("/v1/nfce/123", ComHeader("Idempotency-Key", "ignorada"));
    var resposta2 = await cliente.GetAsync("/v1/nfce/123", ComHeader("Idempotency-Key", "ignorada"));

    Assert.False(resposta1.Headers.Contains("Idempotency-Replayed"));
    Assert.False(resposta2.Headers.Contains("Idempotency-Replayed")); // filter é no-op em GET, mesmo com key repetida
}

[Fact]
public async Task Resposta202DeContingencia_EhArmazenadaEReplayadaIdentica_SemRedisparaEmissao()
{
    var resposta1 = await cliente.PostAsync("/v1/nfce", corpoDeContingencia, ComHeader("Idempotency-Key", "cont-1"));
    Assert.Equal(HttpStatusCode.Accepted, resposta1.StatusCode);

    var resposta2 = await cliente.PostAsync("/v1/nfce", corpoDeContingencia, ComHeader("Idempotency-Key", "cont-1"));

    Assert.Equal(HttpStatusCode.Accepted, resposta2.StatusCode);
    Assert.Equal(await resposta1.Content.ReadAsStringAsync(), await resposta2.Content.ReadAsStringAsync());
    Assert.Equal("true", resposta2.Headers.GetValues("Idempotency-Replayed").Single());
    Assert.Equal(1, contadorDoHandlerFake.Total); // handler nao re-executou
}
```

Run: FAIL → confirmar que `rota` e `ambiente` compõem a PK (já definidos no Step 3), que o filter é no-op para métodos GET/DELETE (Step 3 da abordagem original), e que `CompleteAsync` armazena também respostas `202` → PASS.

- [ ] **Step 11: Teste de arquitetura — isolamento do kernel**

```csharp
[Fact]
public void Idempotency_NaoReferenciaNenhumModuloDeNegocio()
{
    var result = Types.InAssembly(typeof(IdempotencyEndpointFilter).Assembly)
        .ShouldNot().HaveDependencyOnAny("NotaFiscalHub.Modules")
        .GetResult();
    Assert.True(result.IsSuccessful);
}
```

Run: FAIL até o projeto existir isolado → PASS (deve já estar correto pela estrutura da Tarefa 1).

- [ ] **Step 12: Verificar os 13 critérios de aceite da spec B4**

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Idempotency.UnitTests tests/NotaFiscalHub.IntegrationTests/Idempotency tests/NotaFiscalHub.ArchitectureTests --filter "FullyQualifiedName~Idempotency"`
Expected: todos verdes.

- [ ] **Step 13: Commit**

```bash
git add src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Idempotency tests/NotaFiscalHub.BuildingBlocks.Idempotency.UnitTests tests/NotaFiscalHub.IntegrationTests/Idempotency tests/NotaFiscalHub.ArchitectureTests/IdempotencyIsolationTests.cs
git commit -m "feat: middleware de Idempotency-Key com replay, conflito e takeover de orfao"
```

---

### Tarefa 5: Auditoria append-only

**Spec de origem:** `docs/superpowers/specs/tarefas/B5-auditoria-append-only.md`

**Depende de:** Tarefa 3 (outbox/inbox — o handler de auditoria é um consumidor inbox), Tarefa 2 (`ITenantContext`/filtro global, `BeginSystemScope`).

**Files:**
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Auditoria.Contracts/{RegistroAuditoria.cs,IConsultaAuditoria.cs,FiltroAuditoria.cs,IAuditoriaEventCatalog.cs}`
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Auditoria/{AuditoriaDbContext.cs,AuditoriaEventHandler.cs,AuditoriaEventCatalog.cs,PiiSanitizer.cs,ConsultaAuditoria.cs,Migrations/}`
- Create: `tests/NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests/{CatalogMappingTests.cs,PiiSanitizerTests.cs}`
- Create: `tests/NotaFiscalHub.IntegrationTests/Auditoria/{EventToRecordTests.cs,ImmutabilityTests.cs,DedupeTests.cs,TenantIsolationTests.cs}`

**Interfaces:**
- Consumes: `IInboxHandler<T>` (Tarefa 3), `ITenantContext`/`BeginSystemScope` (Tarefa 2).
- Produces: `IConsultaAuditoria.ConsultarAsync(FiltroAuditoria, ct)` — consumido pelo Backoffice na Fase 6.
- Produces: `IAuditoriaEventCatalog` — o catálogo opt-in que as Fases 1+ estendem ao adicionar novos eventos auditáveis.

- [ ] **Step 1: Contratos**

```csharp
namespace NotaFiscalHub.BuildingBlocks.Auditoria.Contracts;

public sealed record RegistroAuditoria(
    Guid Id, Guid? ContaId, string TipoEvento, string Acao, string RecursoTipo, string RecursoId,
    string Ator, DateTimeOffset OcorridoEm, DateTimeOffset RegistradoEm, string CorrelationId,
    Guid MessageId, string? DadosJson);

public sealed record FiltroAuditoria(Guid ContaId, DateTimeOffset? De, DateTimeOffset? Ate,
    string? Acao, string? RecursoTipo, int Pagina = 1, int TamanhoPagina = 50);

public interface IConsultaAuditoria
{
    Task<PaginaDe<RegistroAuditoria>> ConsultarAsync(FiltroAuditoria filtro, CancellationToken ct);
}

public interface IAuditoriaEventCatalog
{
    bool TryMap(EventoIntegracao evento, out RegistroAuditoriaNovo registro);
}
```

- [ ] **Step 2: Teste de unidade — catálogo mapeia evento conhecido, ignora desconhecido (RED primeiro)**

```csharp
[Fact]
public void TryMap_EventoConhecido_RetornaRegistroComTodosOsCampos()
{
    var catalogo = new AuditoriaEventCatalog();
    var sucesso = catalogo.TryMap(new ApiKeyRevogadaDeTeste { ContaId = contaId }, out var registro);

    Assert.True(sucesso);
    Assert.Equal("apikey.revogada", registro.Acao);
}

[Fact]
public void TryMap_EventoDesconhecido_RetornaFalse()
{
    var catalogo = new AuditoriaEventCatalog();
    var sucesso = catalogo.TryMap(new EventoNaoCatalogadoDeTeste(), out _);
    Assert.False(sucesso);
}
```

Run: FAIL → implementar `AuditoriaEventCatalog` com registro explícito por tipo (`ContaCriada`, `ApiKeyRotacionada`, `ApiKeyRevogada`, `WebhookConfigAlterada`, `PlanoAlterado`, `ContaSuspensa`) → PASS.

- [ ] **Step 3: Teste de unidade — sanitizador anti-PII (RED primeiro)**

```csharp
[Theory]
[InlineData("cpf")]
[InlineData("xml")]
[InlineData("senha")]
public void Sanitizar_ChaveDaDenylist_RemoveEEmiteWarn(string chaveSensivel)
{
    var payload = new Dictionary<string, object?> { [chaveSensivel] = "valor-sensivel", ["notaId"] = "123" };
    var resultado = PiiSanitizer.Sanitizar(payload, out var alertouWarn);

    Assert.False(resultado.ContainsKey(chaveSensivel));
    Assert.True(resultado.ContainsKey("notaId"));
    Assert.True(alertouWarn);
}
```

Run: FAIL → implementar `PiiSanitizer.Sanitizar` (denylist case-insensitive: `cpf,cnpjConsumidor,nome,senha,xml,pfx,csc,token,secret`) → PASS.

- [ ] **Step 4: Migration com trigger de bloqueio + REVOKE**

```sql
CREATE FUNCTION auditoria.bloquear_mutacao() RETURNS trigger AS
$$ BEGIN RAISE EXCEPTION 'registro_auditoria e append-only'; END $$ LANGUAGE plpgsql;

CREATE TRIGGER trg_append_only BEFORE UPDATE OR DELETE ON auditoria.registro_auditoria
  FOR EACH ROW EXECUTE FUNCTION auditoria.bloquear_mutacao();

REVOKE UPDATE, DELETE, TRUNCATE ON auditoria.registro_auditoria FROM nfh_app;
```

PK `id`, índices `(conta_id, registrado_em DESC)` e único em `message_id`, coluna `dados` jsonb. Incluir no `.sql` de migration via `migrationBuilder.Sql(...)`.

- [ ] **Step 5: Teste de integração — UPDATE/DELETE lançam mesmo por superusuário (RED primeiro)**

```csharp
[Fact]
public async Task UpdateDireto_LancaPostgresException()
{
    await SemearRegistro();
    await Assert.ThrowsAsync<PostgresException>(() =>
        db.Database.ExecuteSqlRawAsync("UPDATE auditoria.registro_auditoria SET ator = 'hack' WHERE id = {0}", registroId));
}
```

Run: FAIL até a migration do Step 4 estar aplicada no banco de teste → PASS.

- [ ] **Step 6: Handler inbox — evento→registro, dedupe, evento não catalogado não envenena (RED primeiro)**

```csharp
[Fact]
public async Task EventoCatalogado_ViaOutbox_GeraExatamenteUmRegistro()
{
    outboxPublisher.Publicar(new ApiKeyRevogadaDeTeste { ContaId = contaId });
    await dispatcher.ProcessarLoteAsync();

    var registro = await db.Set<RegistroAuditoriaEntity>().SingleAsync();
    Assert.Equal(contaId, registro.ContaId);
    Assert.NotEqual(Guid.Empty, registro.MessageId);
}

[Fact]
public async Task ReentregaMesmaMensagem_ForaDeOrdem_UmRegistroSo()
{
    outboxPublisher.Publicar(new ApiKeyRevogadaDeTeste { ContaId = contaId, MessageId = messageId });
    await dispatcher.ProcessarLoteAsync();
    await ForcarReentrega(messageId); // simula redelivery
    await dispatcher.ProcessarLoteAsync();

    Assert.Single(await db.Set<RegistroAuditoriaEntity>().ToListAsync());
}

[Fact]
public async Task EventoNaoCatalogado_ConsumidoSemErro_SemRegistro()
{
    outboxPublisher.Publicar(new EventoNaoCatalogadoDeTeste { ContaId = contaId });
    await dispatcher.ProcessarLoteAsync();

    Assert.Empty(await db.Set<RegistroAuditoriaEntity>().ToListAsync());
}
```

Run: FAIL → implementar `AuditoriaEventHandler : IInboxHandler<EventoIntegracao>` (insert + `ON CONFLICT (message_id) DO NOTHING`, mesma transação do inbox-insert) → PASS.

- [ ] **Step 7: PII barrada ponta a ponta (RED primeiro)**

```csharp
[Fact]
public async Task EventoComChaveSensivel_RegistroPersisteSemAChave()
{
    outboxPublisher.Publicar(new EventoComCpfDeTeste { ContaId = contaId, Cpf = "12345678900" });
    await dispatcher.ProcessarLoteAsync();

    var registro = await db.Set<RegistroAuditoriaEntity>().SingleAsync();
    Assert.DoesNotContain("12345678900", registro.Dados);
}
```

Run: FAIL → conectar o `PiiSanitizer` (Step 3) ao handler antes do insert → PASS.

- [ ] **Step 8: Consulta com isolamento de tenant (RED primeiro)**

```csharp
[Fact]
public async Task ConsultarAsync_ContaA_NaoRetornaDaContaB_NemDeEscopoDeSistema()
{
    await SemearRegistro(contaId: contaA);
    await SemearRegistro(contaId: contaB);
    await SemearRegistroDeSistema(contaId: null);

    var pagina = await consultaAuditoria.ConsultarAsync(new FiltroAuditoria(contaA), CancellationToken.None);

    Assert.All(pagina.Itens, r => Assert.Equal(contaA, r.ContaId));
}
```

Run: FAIL → implementar `ConsultaAuditoria : IConsultaAuditoria` (filtro obrigatório por `ContaId`, participa do filtro global de tenant) → PASS.

- [ ] **Step 9: Verificar os 7 critérios de aceite da spec B5**

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests tests/NotaFiscalHub.IntegrationTests/Auditoria`
Expected: todos verdes. Evidência: `dotnet ef migrations script` mostra trigger + REVOKE presentes.

- [ ] **Step 10: Registrar decisão de retenção em `docs/decisions.md`**

Adicionar entrada em `docs/decisions.md` §0: "retenção da trilha de auditoria ≥ 5 anos (espelha guarda fiscal); job de expurgo e particionamento adiados" — próximo ID sequencial `D-2026-07-0X-NN`.

- [ ] **Step 11: Commit**

```bash
git add src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Auditoria* tests/NotaFiscalHub.BuildingBlocks.Auditoria.UnitTests tests/NotaFiscalHub.IntegrationTests/Auditoria docs/decisions.md
git commit -m "feat: auditoria append-only consumindo eventos de integracao via inbox"
```

---

### Tarefa 6: Suíte NetArchTest travando fronteiras

**Spec de origem:** `docs/superpowers/specs/tarefas/B6-netarchtest.md`

**Depende de:** Tarefa 1 (estrutura/namespaces), Tarefa 2 (`ITenantScopedEntity`/`TenantDbContext` para T5/T6 — se executada antes da Tarefa 2, os testes T5/T6 nascem `[Fact(Skip="aguardando Tarefa 2")]`).

**Files:**
- Modify: `tests/NotaFiscalHub.ArchitectureTests/{ReferenceRulesTests.cs}` (já criado na Tarefa 1 — vira o container das 7 regras)
- Create: `tests/NotaFiscalHub.ArchitectureTests/{ContractPurityTests.cs,DomainIsolationTests.cs,PhysicalStructureTests.cs,ArchitectureExceptions.cs,ModuleNames.cs,README.md}`

**Interfaces:**
- Consumes: `ITenantScopedEntity`, `TenantDbContext` (Tarefa 2).
- Produces: as 7 regras T1–T7 como gate — consumido pela Tarefa 9 (CI, estágio de arquitetura).

- [ ] **Step 1: Fixture de carga de assemblies + guarda contra suíte vazia**

```csharp
public static class AssemblyLoader
{
    public static IReadOnlyCollection<Assembly> TodasAsAssembliesDaSolution() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name!.StartsWith("NotaFiscalHub"))
            .ToList();
}

[Fact]
public void Fixture_CarregaPeloMenosUmaAssemblyPorModulo()
{
    Assert.NotEmpty(AssemblyLoader.TodasAsAssembliesDaSolution());
}
```

- [ ] **Step 2: `ModuleNames.cs` — nomes centralizados**

```csharp
public static class ModuleNames
{
    public const string ContasPlanos = "NotaFiscalHub.Modules.ContasPlanos";
    public const string EmpresasCertificados = "NotaFiscalHub.Modules.EmpresasCertificados";
    public const string Emissao = "NotaFiscalHub.Modules.Emissao";
    public const string MotorNfce = "NotaFiscalHub.Modules.MotorNfce";
    public const string Documentos = "NotaFiscalHub.Modules.Documentos";
    public const string BuildingBlocks = "NotaFiscalHub.BuildingBlocks";
}
```

- [ ] **Step 3: T1 — matriz de referências entre módulos (RED por violação-isca primeiro)**

```csharp
[Fact]
public void Fronteiras_Emissao_SoReferenciaContratosPermitidos()
{
    var result = Types.InAssembly(EmissaoApplicationAssembly)
        .ShouldNot().HaveDependencyOnAny(
            $"{ModuleNames.ContasPlanos}", // ver exceção codificada abaixo
            $"{ModuleNames.EmpresasCertificados}.Domain", $"{ModuleNames.EmpresasCertificados}.Application", $"{ModuleNames.EmpresasCertificados}.Infrastructure",
            $"{ModuleNames.MotorNfce}.Domain", $"{ModuleNames.MotorNfce}.Application", $"{ModuleNames.MotorNfce}.Infrastructure",
            $"{ModuleNames.Documentos}.Domain", $"{ModuleNames.Documentos}.Application", $"{ModuleNames.Documentos}.Infrastructure")
        .GetResult();

    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}

[Fact]
public void NenhumModuloDeNegocio_Referencia_ContasPlanosForaDaAllowlist()
{
    foreach (var modulo in new[] { ModuleNames.EmpresasCertificados, ModuleNames.Emissao, ModuleNames.MotorNfce, ModuleNames.Documentos })
    {
        var result = Types.InAssembly(AssemblyDoModulo(modulo))
            .ShouldNot().HaveDependencyOnAny($"{ModuleNames.ContasPlanos}")
            .GetResult();
        Assert.True(result.IsSuccessful, $"{modulo} viola: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
```

Adicionar temporariamente `ProjectReference` de `Documentos.Application → ContasPlanos.Contracts`.

Run: FAIL (RED) → remover → Run novamente → PASS (GREEN). Registrar no PR.

- [ ] **Step 4: T2 — contratos só com DTOs (RED por violação-isca primeiro)**

```csharp
[Fact]
public void Contratos_NaoExpoeEntidadesDeDominio()
{
    var result = Types.InAssembly(TodosOsAssembliesDeContracts())
        .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore")
        .And().ShouldNot().HaveDependencyOnAny(
            $"{ModuleNames.Emissao}.Domain", $"{ModuleNames.Emissao}.Application", $"{ModuleNames.Emissao}.Infrastructure")
        .GetResult();

    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}
```

Violação-isca: expor `NotaFiscal` (entidade) como retorno em `MotorNfce.Contracts`. RED → remover → GREEN.

- [ ] **Step 5: T3 — entidade de domínio não cruza fronteira (RED por violação-isca primeiro)**

```csharp
[Fact]
public void EntidadeDeDominio_NaoEUsadaForaDoProprioModulo()
{
    var result = Types.InAssembly(TodosOsAssembliesQueNaoSaoDocumentos())
        .ShouldNot().HaveDependencyOnAny($"{ModuleNames.Documentos}.Domain")
        .GetResult();

    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}
```

Violação-isca: `Documentos.Application` usando tipo de `Emissao.Domain`. RED → remover → GREEN.

- [ ] **Step 6: T4 — Domain não referencia Infrastructure/EF/AspNetCore (RED por violação-isca primeiro)**

```csharp
[Fact]
public void Domain_NaoReferenciaInfrastructureNemFrameworks()
{
    foreach (var modulo in ModuleNames.TodosOsModulosDeNegocio)
    {
        var result = Types.InAssembly(AssemblyDomainDoModulo(modulo))
            .ShouldNot().HaveDependencyOnAny(
                $"{modulo}.Infrastructure", $"{modulo}.Application",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();
        Assert.True(result.IsSuccessful, $"{modulo}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
```

Violação-isca: `using Microsoft.EntityFrameworkCore` em `Emissao.Domain`. RED → remover → GREEN.

- [ ] **Step 7: T5/T6 — entidade tenant-scoped e filtro de tenant (dependem da Tarefa 2)**

Se a Tarefa 2 já rodou: implementar os testes reais (usam reflexão sobre `IModel` — não são NetArchTest puro):

```csharp
[Fact]
public void TenantScoped_ExigeContaId()
{
    // idêntico ao Step 6 da Tarefa 2 — este é o lar definitivo do teste;
    // se já existe em tests/NotaFiscalHub.ArchitectureTests/TenantScopedEntityTests.cs, apenas mover/consolidar aqui.
}

[Fact]
public void DbContext_ExigeFiltroDeTenant()
{
    foreach (var dbContextType in TodosOsDbContextsConcretos())
    {
        Assert.True(typeof(TenantDbContext).IsAssignableFrom(dbContextType));
    }
    // + inspeção de expression tree: toda entidade ITenantScopedEntity no IModel
    // tem GetQueryFilter() não-nulo cujo corpo referencia ContaId.
}
```

Se a Tarefa 2 ainda não rodou: `[Fact(Skip = "aguardando Tarefa 2 - ITenantScopedEntity/TenantDbContext")]` com comentário de rastreio.

- [ ] **Step 8: T7 — física espelha a lógica (RED por violação-isca primeiro)**

```csharp
[Fact]
public void Modulos_NaoReferenciamHosts()
{
    var result = Types.InAssembly(TodosOsAssembliesDeModulos())
        .ShouldNot().HaveDependencyOnAny("NotaFiscalHub.Api", "NotaFiscalHub.Worker")
        .GetResult();
    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}

[Fact]
public void BuildingBlocks_NaoReferenciaModulosForaDaExcecaoCodificada()
{
    var result = Types.InAssembly(TodosOsAssembliesDeBuildingBlocks())
        .ShouldNot().HaveDependencyOnAny($"{ModuleNames.EmpresasCertificados}", $"{ModuleNames.Emissao}", $"{ModuleNames.MotorNfce}", $"{ModuleNames.Documentos}")
        .GetResult();
    Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
}
```

Violação-isca: `BuildingBlocks` referenciando `Emissao.Contracts` sem entrada na allowlist. RED → remover → GREEN.

- [ ] **Step 9: Processo de exceção com teste-guardião**

```csharp
public sealed record ExcecaoArquitetura(string Regra, string Origem, string Destino, string Justificativa, DateOnly Data, string DecisaoRef);

public static class ArchitectureExceptions
{
    public static readonly IReadOnlyList<ExcecaoArquitetura> Todas =
    [
        new("T1", "BuildingBlocks", $"{ModuleNames.ContasPlanos}.Contracts",
            "Middleware de autenticacao consome credencial/consumo/webhook-config (design SS2.5)",
            new DateOnly(2026, 7, 2), "docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md#2.5")
    ];
}

[Fact]
public void TodaExcecao_TemJustificativaEDecisaoRef()
{
    Assert.All(ArchitectureExceptions.Todas, e =>
    {
        Assert.False(string.IsNullOrWhiteSpace(e.Justificativa));
        Assert.False(string.IsNullOrWhiteSpace(e.DecisaoRef));
    });
}
```

Run: FAIL (tipo ainda não existe) → implementar → PASS. A seed inicial contém exatamente 1 entrada (kernel/host → `ContasPlanos.Contracts`).

- [ ] **Step 10: README da suíte**

Criar `tests/NotaFiscalHub.ArchitectureTests/README.md` documentando o mapa regra→teste→seção do design (T1→§2.5, T2→§2.3.3, T3→§2.3, T4→Clean Architecture, T5/T6→§2.3.5, T7→§2.3.7) e o processo de exceção (Step 9).

- [ ] **Step 11: Verificar os 6 critérios de aceite da spec B6**

Run: `dotnet test tests/NotaFiscalHub.ArchitectureTests`
Expected: verde, execução < 30s. Confirmar que cada teste tem nome individual e que a mensagem de falha lista `FailingTypeNames`.

- [ ] **Step 12: Commit**

```bash
git add tests/NotaFiscalHub.ArchitectureTests
git commit -m "feat: suite NetArchTest T1-T7 travando fronteiras entre modulos"
```

---

### Tarefa 7: Observabilidade fundacional no kernel

**Spec de origem:** `docs/superpowers/specs/tarefas/B7-observabilidade-fundacional.md`

**Depende de:** estrutura da solution (Tarefa 1). T4 do plano de testes depende do dispatcher da Tarefa 3 — se rodar antes, contribuir a extensão (campo `Headers` no envelope) via PR coordenado.

**Files:**
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Observability/{ICorrelationContext.cs,CorrelationContext.cs,ObservabilityExtensions.cs,PiiMaskingEnricher.cs,PiiFields.cs,CorrelationIdMiddleware.cs,PiiLogAssertions.cs,README.md}`
- Modify: `Directory.Build.props` (analyzers Serilog como erro)
- Modify: `src/Api/NotaFiscalHub.Api/Program.cs`, `src/Worker/NotaFiscalHub.Worker/Program.cs` (wire-up)
- Create: `tests/NotaFiscalHub.BuildingBlocks.Observability.UnitTests/{PiiMaskingEnricherTests.cs,CorrelationIdMiddlewareTests.cs}`
- Create: `tests/NotaFiscalHub.IntegrationTests/Observability/{PiiGuardrailTests.cs,CorrelationIdEndToEndTests.cs,OtelTests.cs,HealthChecksTests.cs}`

**Interfaces:**
- Consumes: dispatcher/envelope da Tarefa 3 (para T4 — CorrelationId atravessando outbox).
- Produces: `ICorrelationContext.CorrelationId` — consumido por todo módulo de negócio para enriquecimento de log a partir da Fase 1.
- Produces: `AddNfhObservability(builder, serviceName)`/`UseNfhObservability(app)` — consumido pelos 2 hosts.

- [ ] **Step 1: Contratos**

```csharp
namespace NotaFiscalHub.BuildingBlocks.Observability;

public interface ICorrelationContext
{
    string CorrelationId { get; }
}

public static class ObservabilityExtensions
{
    public static IHostApplicationBuilder AddNfhObservability(this IHostApplicationBuilder builder, string serviceName) { /* ... */ return builder; }
    public static WebApplication UseNfhObservability(this WebApplication app) { /* ... */ return app; }
}
```

- [ ] **Step 2: T1 — `PiiMaskingEnricher` mascara por denylist (RED primeiro)**

```csharp
[Theory]
[InlineData("cpf", "12345678900", "***")]
[InlineData("nome", "Joao Silva", "***")]
[InlineData("notaId", "abc-123", "abc-123")] // fora da denylist, intacto
public void PiiMaskingEnricher_MascaraPropriedadeDaDenylist(string propriedade, string valor, string esperado)
{
    var logEvent = CriarLogEventCom(propriedade, valor);
    new PiiMaskingEnricher().Enrich(logEvent, propertyFactory);

    Assert.Equal(esperado, logEvent.Properties[propriedade].ToString().Trim('"'));
}

[Fact]
public void PiiMaskingEnricher_ChaveAcesso_MascaraParcial()
{
    var logEvent = CriarLogEventCom("chaveAcesso", "35090614200167140065125001000001800100000097");
    new PiiMaskingEnricher().Enrich(logEvent, propertyFactory);

    Assert.Matches(@"^350906\.\.\.0097$", logEvent.Properties["chaveAcesso"].ToString().Trim('"'));
}
```

Run: FAIL → implementar `PiiFields` (denylist central) + `PiiMaskingEnricher` (destructuring policy) → PASS.

- [ ] **Step 3: Analyzer de interpolação como erro (compile-time)**

Adicionar ao `Directory.Build.props` (dentro do guard-condition `NotaFiscalHub`):

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);CA2254</WarningsAsErrors>
</PropertyGroup>
```

Verificação manual: escrever `_logger.LogInformation($"teste {variavel}")` num arquivo temporário do kernel → `dotnet build` falha com CA2254 → remover o snippet. Documentar no PR (diff do `Directory.Build.props` é a evidência do critério 2 da spec).

- [ ] **Step 4: T2 — CorrelationId middleware (RED primeiro)**

```csharp
[Fact]
public async Task Middleware_HeaderValido_PreservadoNaRespostaENoContexto()
{
    var resposta = await cliente.GetAsync("/teste", ComHeader("X-Correlation-Id", "abc-123"));
    Assert.Equal("abc-123", resposta.Headers.GetValues("X-Correlation-Id").Single());
}

[Fact]
public async Task Middleware_SemHeader_GeraGuid()
{
    var resposta = await cliente.GetAsync("/teste");
    var valor = resposta.Headers.GetValues("X-Correlation-Id").Single();
    Assert.True(Guid.TryParse(valor, out _));
}
```

Run: FAIL → implementar `CorrelationIdMiddleware` (aceita `[A-Za-z0-9\-]{8,64}`; inválido/ausente → gera GUID; popula `ICorrelationContext` + `LogContext.PushProperty` + tag no `Activity.Current`) → PASS.

- [ ] **Step 5: T3 — guardrail PII ponta a ponta (RED primeiro)**

```csharp
[Fact]
public async Task RequisicaoComCpfNoPayload_NenhumLogContemCpfOuXml()
{
    var sink = new InMemorySink();
    await cliente.PostAsync("/teste/emissao-fake", CorpoComCpfDoConsumidor("12345678900"));

    PiiLogAssertions.AssertNoPii(sink.Events);
}
```

`PiiLogAssertions.AssertNoPii` renderiza cada evento e falha se casar regex de CPF (`\d{3}\.?\d{3}\.?\d{3}-?\d{2}`) ou fragmento XML (`<infNFe`, `<NFe`, `<dest>`), incluindo logs de exceção com o payload no contexto.

Run: FAIL até o `PiiMaskingEnricher` estar wireado no pipeline Serilog completo → PASS.

- [ ] **Step 6: T4 — CorrelationId atravessa API→outbox→handler (RED primeiro, depende da Tarefa 3)**

```csharp
[Fact]
public async Task CorrelationId_AtravessaApiOutboxEHandler_MesmoValorNosDois()
{
    var correlationId = "corr-teste-123";
    await cliente.PostAsync("/teste/publica-evento", corpo, ComHeader("X-Correlation-Id", correlationId));
    await dispatcher.ProcessarLoteAsync();

    Assert.Equal(correlationId, correlationIdObservadoNoHandler);
    Assert.Contains(sink.Events, e => e.Properties["CorrelationId"].ToString().Contains(correlationId));
}
```

Run: FAIL → estender o envelope do outbox com `correlation-id` (publisher lê de `ICorrelationContext`) + dispatcher abre escopo de correlação antes de invocar o handler → PASS.

- [ ] **Step 7: T5 — OTel traces (RED primeiro)**

```csharp
[Fact]
public async Task OtelExporterEmMemoria_CapturaSpanHttpESpanEf_SemValorDeParametroSql()
{
    await cliente.GetAsync("/teste/consulta-com-parametro?cpf=12345678900");

    var spanEf = exportedItems.Single(s => s.Source.Name.Contains("Npgsql"));
    Assert.DoesNotContain("12345678900", spanEf.Tags.Select(t => t.Value?.ToString()));
}

[Fact]
public async Task Health_NaoGeraSpan()
{
    await cliente.GetAsync("/health");
    Assert.DoesNotContain(exportedItems, s => s.DisplayName.Contains("/health"));
}
```

Run: FAIL → implementar `AddOpenTelemetry().WithTracing(...)` com `SetDbStatementForText=true` e parâmetros SQL desabilitados + filtro de `/alive`,`/health` → PASS.

- [ ] **Step 8: T6 — health checks nos 2 deployables (RED primeiro)**

```csharp
[Fact]
public async Task Health_PostgresNoAr_Retorna200_Derrubado_Retorna503()
{
    var respostaOk = await cliente.GetAsync("/health");
    Assert.Equal(HttpStatusCode.OK, respostaOk.StatusCode);

    await postgresContainer.StopAsync();
    var respostaFalha = await cliente.GetAsync("/health");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, respostaFalha.StatusCode);
}
```

Run: FAIL → implementar `/alive` e `/health` (`AddNpgSql`) nos dois hosts (Worker sobe host HTTP mínimo só para isso) → PASS.

- [ ] **Step 9: Wire-up final nos hosts + README**

`Program.cs` de Api e Worker chamam `builder.AddNfhObservability("nfh-api"/"nfh-worker")` e `app.UseNfhObservability()`. Criar `BuildingBlocks/Observability/README.md` (denylist, como estender, o que é Fase 7).

- [ ] **Step 10: Verificar os 7 critérios de aceite da spec B7**

Run: `dotnet test tests/NotaFiscalHub.BuildingBlocks.Observability.UnitTests tests/NotaFiscalHub.IntegrationTests/Observability`
Expected: todos verdes; suíte NetArchTest (Tarefa 6) continua verde.

- [ ] **Step 11: Commit**

```bash
git add src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Observability src/Api/NotaFiscalHub.Api/Program.cs src/Worker/NotaFiscalHub.Worker/Program.cs Directory.Build.props tests/NotaFiscalHub.BuildingBlocks.Observability.UnitTests tests/NotaFiscalHub.IntegrationTests/Observability
git commit -m "feat: observabilidade fundacional - logs sem PII, CorrelationId, OTel, health checks"
```

---

### Tarefa 8: Provisionamento AWS mínimo (trilha paralela)

**Spec de origem:** `docs/superpowers/specs/tarefas/B8-provisionamento-aws.md`

**Depende de:** nada do código desta fase — pode rodar em paralelo a qualquer outra tarefa. Desbloqueia Fase 2 (custódia KMS) e Fase 4 (guarda S3); a Fase 0/CI de integração usa Testcontainers, não este RDS.

**Files:**
- Create: `infra/terraform/{main.tf,variables.tf,outputs.tf,kms.tf,s3.tf,rds.tf,vpc.tf,iam.tf,budgets.tf,README.md}`
- Create: `infra/terraform/tests/nonprod.tftest.hcl`
- Create: `src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Kernel/Aws/AwsRecursosOptions.cs`
- Create: `tests/NotaFiscalHub.IntegrationTests/Aws/AwsSmokeTests.cs` (trait `AwsSmoke`, excluído do CI default)

**Interfaces:**
- Produces: `AwsRecursosOptions { Region, KmsCertificadosKeyArn, S3DocumentosBucket }` — consumido pela Fase 2 (custódia) e Fase 4 (guarda de documentos).

- [ ] **Step 1: Bootstrap do Terraform**

```bash
mkdir -p infra/terraform/tests
```

Criar `main.tf` com backend S3 versionado (`nfh-terraform-state-<account_id>`, criado uma vez fora do estado) e pin de versões (`terraform >= 1.10`, `provider aws ~> 5.x`).

- [ ] **Step 2: Teste de plano primeiro — `nonprod.tftest.hcl` (RED)**

```hcl
run "bucket_tem_object_lock_versionamento_e_sse_kms" {
  command = plan
  assert {
    condition     = aws_s3_bucket_versioning.documentos.versioning_configuration[0].status == "Enabled"
    error_message = "Versionamento deve estar habilitado"
  }
  assert {
    condition     = aws_s3_bucket_object_lock_configuration.documentos.rule[0].default_retention[0].mode == "GOVERNANCE"
    error_message = "Object Lock deve ser GOVERNANCE em nao-producao"
  }
}

run "cmks_tem_rotacao_automatica" {
  command = plan
  assert {
    condition     = aws_kms_key.certificados.enable_key_rotation == true
    error_message = "CMK de certificados deve ter rotacao automatica"
  }
}

run "rds_nao_publico_e_criptografado" {
  command = plan
  assert {
    condition     = aws_db_instance.principal.publicly_accessible == false
    error_message = "RDS nao pode ser publico"
  }
  assert {
    condition     = aws_db_instance.principal.storage_encrypted == true
    error_message = "RDS deve ter storage criptografado"
  }
}
```

Run: `terraform test` em `infra/terraform/`
Expected: FAIL (recursos ainda não existem nos `.tf`).

- [ ] **Step 3: Módulos `kms.tf`, `s3.tf`, `rds.tf`, `vpc.tf`, `iam.tf`, `budgets.tf`**

Implementar conforme spec B8 passo 3: 2 CMKs (`nfh-nonprod-certificados`, `nfh-nonprod-documentos`), bucket `nfh-nonprod-documentos-<account_id>` (Object Lock GOVERNANCE/1 dia, versionamento, SSE-KMS obrigatório via bucket policy, Block Public Access total), RDS `nfh-nonprod-pg` (`db.t4g.micro`, Single-AZ, gp3 20GB, `storage_encrypted`, `deletion_protection=true`, `publicly_accessible=false`, senha via Secrets Manager), role IAM `nfh-nonprod-app` de menor privilégio, `default_tags` obrigatórias, AWS Budgets US$50/mês.

Run: `terraform test`
Expected: PASS.

- [ ] **Step 4: `terraform validate`**

Run: `terraform validate`
Expected: sem erros.

- [ ] **Step 5: Contrato de configuração para a aplicação**

```csharp
namespace NotaFiscalHub.BuildingBlocks.Kernel.Aws;

public sealed class AwsRecursosOptions
{
    public required string Region { get; init; }
    public required string KmsCertificadosKeyArn { get; init; }
    public required string S3DocumentosBucket { get; init; }
}
```

Documentar no `infra/terraform/README.md` que os ARNs saem de `terraform output` → variáveis de ambiente/user-secrets, nunca commitados.

- [ ] **Step 6: `terraform apply` (execução manual, fora do CI)**

Requer credenciais de administração AWS. Executado uma vez pelo dono/CI de infra — **não** faz parte do fluxo de `dotnet test` desta fase.

- [ ] **Step 7: Smoke tests xUnit (pós-apply, trait `AwsSmoke`)**

```csharp
[Trait("Category", "AwsSmoke")]
public class AwsSmokeTests
{
    [Fact]
    public async Task GenerateDataKey_ComEncryptionContext_DecryptDevolveAMesmaDek()
    {
        var dataKey = await kmsClient.GenerateDataKeyAsync(new() { KeyId = arn, KeySpec = "AES_256", EncryptionContext = contexto });
        var decrypted = await kmsClient.DecryptAsync(new() { CiphertextBlob = dataKey.CiphertextBlob, EncryptionContext = contexto });
        Assert.Equal(dataKey.Plaintext, decrypted.Plaintext);
    }

    [Fact]
    public async Task Decrypt_SemEncryptionContext_LancaInvalidCiphertext()
    {
        await Assert.ThrowsAsync<InvalidCiphertextException>(() =>
            kmsClient.DecryptAsync(new() { CiphertextBlob = ciphertextComContexto })); // sem o contexto
    }
}
```

Run (com credenciais da role `nfh-nonprod-app`; skip explícito e claro se ausente): `dotnet test --filter Category=AwsSmoke`
Expected: PASS.

- [ ] **Step 8: Registrar decisões e verificar os 8 critérios de aceite da spec B8**

Adicionar em `docs/decisions.md` §0: Terraform como IaC, região `sa-east-1`, conta única nonprod, retenção GOVERNANCE/1 dia + pendência do modo de produção.

Run: `terraform validate && terraform test`; `aws s3api get-object-lock-configuration`; `aws kms get-key-rotation-status`; `aws rds describe-db-instances`; `git grep -iE 'AKIA|aws_secret|BEGIN.*PRIVATE'` (vazio).

- [ ] **Step 9: Commit**

```bash
git add infra/terraform src/BuildingBlocks/NotaFiscalHub.BuildingBlocks.Kernel/Aws tests/NotaFiscalHub.IntegrationTests/Aws docs/decisions.md
git commit -m "feat: provisionamento AWS minimo nao-producao (KMS, S3 Object Lock, RDS)"
```

---

### Tarefa 9: CI completo (build → unidade+arquitetura → integração)

**Spec de origem:** `docs/superpowers/specs/tarefas/B9-ci-pipeline.md`

**Depende de:** Tarefa 1 (`.sln`, layout `tests/`), Tarefa 6 (suíte NetArchTest — sem ela o estágio de arquitetura não tem o que rodar). Deve ser a última tarefa da fase, pois consolida o CI sobre tudo que as Tarefas 1–8 produziram.

**Files:**
- Create: `.github/workflows/ci.yml`
- Create: `global.json`
- Create: `scripts/ci-local.sh`
- Modify: `Directory.Build.props` (`RestorePackagesWithLockFile=true`, se ainda não presente)
- Modify: `README.md` (badge de status)

**Interfaces:**
- Consumes: convenção `tests/<Nome>.{Unit,Architecture,Integration}Tests` estabelecida pelas Tarefas 1–7.
- Produces: gate obrigatório de merge — consumido por todo PR das Fases 1+.

- [ ] **Step 1: RED — script local antes do workflow existir**

```bash
#!/usr/bin/env bash
set -euo pipefail
dotnet restore NotaFiscalHub.sln
dotnet build NotaFiscalHub.sln --no-restore --configuration Release
dotnet test --no-build --configuration Release --filter "FullyQualifiedName~UnitTests|FullyQualifiedName~ArchitectureTests" --logger "trx;LogFileName=unit.trx"
dotnet test --no-build --configuration Release --filter "FullyQualifiedName~IntegrationTests" --logger "trx;LogFileName=integration.trx"
```

Salvar como `scripts/ci-local.sh`.

Run: `bash scripts/ci-local.sh` (antes de `ci.yml` existir, isso já funciona localmente — o RED é a ausência do workflow no GitHub, verificável só depois do push).

- [ ] **Step 2: `global.json` com pin do SDK**

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

(ajustar a versão exata para a instalada localmente, via `dotnet --version`).

- [ ] **Step 3: Workflow mínimo — build**

```yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
jobs:
  build-and-test:
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
          cache: true
          cache-dependency-path: '**/packages.lock.json'
      - name: restore
        run: dotnet restore NotaFiscalHub.sln
      - name: build
        run: dotnet build NotaFiscalHub.sln --no-restore --configuration Release
```

Run: `docker run --rm -v "$PWD:/repo" rhysd/actionlint -color /repo/.github/workflows/ci.yml`
Expected: sem erros. Push em branch de PR → run verde no GitHub (steps `restore`, `build`).

- [ ] **Step 4: Estágio unidade+arquitetura — prova do gate (RED de verdade)**

Adicionar ao `ci.yml`:

```yaml
      - name: test-unidade
        run: dotnet test --no-build --configuration Release --filter "FullyQualifiedName~UnitTests" --logger "trx;LogFileName=unit.trx"
      - name: test-arquitetura
        run: dotnet test --no-build --configuration Release --filter "FullyQualifiedName~ArchitectureTests" --logger "trx;LogFileName=architecture.trx"
```

Em branch descartável: introduzir referência proibida (ex.: `Documentos.Application → ContasPlanos.Contracts`, violando §2.5).

Run: push da branch descartável → CI fica vermelho no step `test-arquitetura`.
Expected: run vermelho confirmado (RED). Reverter a violação. Anexar link do run vermelho ao PR de B9.

- [ ] **Step 5: Estágio integração — prova do gate**

```yaml
      - name: test-integracao
        run: dotnet test --no-build --configuration Release --filter "FullyQualifiedName~IntegrationTests" --logger "trx;LogFileName=integration.trx"
```

Em branch descartável: sabotar um teste de integração (ex.: assert invertido em `TenantIsolationTests`).

Run: push → CI vermelho no step `test-integracao`.
Expected: run vermelho confirmado. Reverter. Anexar link ao PR.

- [ ] **Step 6: Artefatos e acabamento**

```yaml
      - name: artefatos
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/*.trx'
```

Adicionar `--collect:"XPlat Code Coverage"` aos comandos de teste; badge no `README.md`; comentário no `ci.yml` reservando os estágios futuros (`homologacao-tpamb2`, `smoke-sefaz`, `deploy` — Fase 7, não implementar agora).

- [ ] **Step 7: Guard de "≥ 1 teste por estágio"**

```yaml
      - name: guard-arquitetura-nao-vazia
        run: |
          COUNT=$(find tests -path "*ArchitectureTests*" -name "*.cs" | xargs grep -l "\[Fact\]" | wc -l)
          if [ "$COUNT" -eq 0 ]; then echo "Nenhum teste de arquitetura encontrado — gate vazio!"; exit 1; fi
```

(step antes de `test-arquitetura`, evita merge com suíte vazia tratada como sucesso silencioso).

- [ ] **Step 8: Habilitar branch protection**

Ação manual do dono do repositório: marcar `test-arquitetura` e `test-integracao` como checks obrigatórios em Settings → Branches. Registrar no PR que isso foi feito (fora do escopo de código).

- [ ] **Step 9: Registrar decisão de plataforma**

Adicionar em `docs/decisions.md` §0: "CI em GitHub Actions, runner `ubuntu-latest` (Docker Linux nativo para Testcontainers)".

- [ ] **Step 10: Verificar os 9 critérios de aceite da spec B9**

Run: `bash scripts/ci-local.sh` (paridade local/CI); medir duração do run com cache quente (alvo ≤ 10 min, teto `timeout-minutes: 20`).

- [ ] **Step 11: Commit**

```bash
git add .github/workflows/ci.yml global.json scripts/ci-local.sh Directory.Build.props README.md docs/decisions.md
git commit -m "feat: pipeline CI completo build-unidade-arquitetura-integracao"
```

---

## Critério de saída da Fase 0

Conforme o plano-mestre: **solution compila com estrutura de módulos, CI verde com testes de arquitetura travando fronteiras, e a biblioteca outbox/inbox provada com duplicata e reordenação.** Verificação final:

- [ ] `dotnet build NotaFiscalHub.sln` verde, zero warnings.
- [ ] `dotnet test` das 3 suítes (unidade, arquitetura, integração) 100% verde via `scripts/ci-local.sh` e no GitHub Actions.
- [ ] Suíte NetArchTest (Tarefa 6) trava as 7 regras — confirmado por violação-isca revertida em cada uma.
- [ ] Outbox/Inbox (Tarefa 3) prova duplicata e fora-de-ordem sem efeito duplicado (Steps 6, 9 da Tarefa 3) e nenhum evento sem handler sai como `Processada` (Step 5).
- [ ] Tenant context (Tarefa 2) é fail-closed: query sem escopo lança, nunca retorna tudo.
- [ ] `git status --short old/` vazio — legado (`old/src/VisuFiscalHub.*`, `old/tests/VisuFiscalHub.Tests`) intocado durante toda a fase.
- [ ] Provisionamento AWS (Tarefa 8) aplicado e smoke tests passando, OU explicitamente adiado com decisão registrada (não bloqueia o fechamento da Fase 0 em código, mas bloqueia o início da Fase 2/4).

## Dívidas registradas (pontos de atenção das specs, não critérios de aceite numerados)

Achados de revisão adversarial (Opus) que não bloqueiam o fechamento da fase, mas devem virar item de PR/issue ao tocar a tarefa correspondente:

- **Tarefa 2:** proibir `FromSqlRaw`/`SqlQueryRaw` fora do kernel via a mesma varredura de fontes do Step 12 (SQL cru escapa do query filter — vetor de fail-open citado nos Riscos da spec B2).
- **Tarefa 7:** Camada 2 do anti-PII pede também as regras do pacote `Serilog.Analyzer`, não só `CA2254` — avaliar adicionar o pacote no Step 3.
- **Tarefa 8:** critério de aceite 6 da spec B8 (prova de menor-privilégio via `aws iam simulate-principal-policy` retornando `implicitDeny` para `kms:ScheduleKeyDeletion`/`s3:DeleteBucket`/`s3:BypassGovernanceRetention`) não tem step dedicado — adicionar ao Step 8.
