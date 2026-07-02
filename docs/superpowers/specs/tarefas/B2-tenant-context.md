# Spec B2 — Tenant context + filtro global `conta_id`
> Card: https://app.clickup.com/t/86e24c1fy | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Entregar o isolamento de tenant como infraestrutura transversal do kernel (`BuildingBlocks`): `ITenantContext` resolvido na borda, filtro global por `conta_id` em todo DbContext tenant-scoped (fail-closed: query sem escopo lança exceção, nunca "retorna tudo"), `BeginTenantScope` explícito para jobs do Worker, bypass administrativo auditável e teste de arquitetura que **falha o build** se entidade tenant-scoped não tiver `conta_id` ou se um DbContext não registrar o filtro (design §2.3.5, plano-mestre Fase 0).

## Contexto e referências
- Design: `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md` — §2.2 (cross-cutting kernel), §2.3.5 (tenant é fronteira de primeira classe), §2.5 (nenhum módulo de negócio → Contas & Planos; credencial resolvida uma vez na borda), §4.4 (linha "Arquitetura" e "Integração"), §6 (risco "vazamento entre tenants").
- Plano-mestre: `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md` — Fase 0 (bullet "Tenant context") e **Global Constraints** (linha 14: `conta_id` + filtro + `BeginTenantScope`; TDD; NetArchTest no CI desde a Fase 0; logs sem PII).
- Decisões: `docs/decisions.md` §0 — D-2026-07-01-10 (schema tem módulo dono) informa a allowlist de entidades não-tenant.
- Handoff: `docs/superpowers/handoffs/2026-07-01-revisao-docs-e-gate0-handoff.md` — Gate 0 aponta reestruturação da solution (matriz preliminar); ver Assunções.

## Escopo (dentro / fora)
**Dentro:** contratos `ITenantContext`/`ITenantScopeFactory`/`ITenantScope` e marker `ITenantScopedEntity` no kernel; implementação ambiente via `AsyncLocal` (única, servindo API e Worker); `TenantDbContext` base com query filter global e interceptor de escrita (stamp + validação de `ContaId` em INSERT/UPDATE/DELETE); middleware da API que abre o escopo por request (com **stub de resolução declarado** até a Fase 1); `BeginSystemScope` (bypass auditável); suíte de testes de arquitetura, unidade e integração conta A vs conta B; registro DI e documentação de uso no README do kernel.
**Fora:** autenticação por API key e allowlist de empresas (Fase 1 — aqui só o *slot* `EmpresasPermitidas` no contrato); revalidação de tenant no módulo Documentos (Fase 4, defesa em profundidade); RLS no PostgreSQL (não previsto no design; ver Riscos); outbox/inbox (B3), idempotência (B4), auditoria consumidora dos eventos (B5) — B2 só emite o log estruturado do bypass.

## Abordagem / Passos
Constraint global: **TDD — cada passo começa pelo teste que falha.** Ordem:

1. **Contratos no kernel** (`BuildingBlocks/Tenancy`). Testes de unidade primeiro (acesso sem escopo lança; escopos aninhados; restauração ao dispose), depois:
```csharp
public interface ITenantScopedEntity { Guid ContaId { get; } }        // marker p/ entidades e teste de arquitetura

public interface ITenantContext
{
    Guid ContaId { get; }                 // lança TenantNaoResolvidoException se não houver escopo ativo
    bool HasTenant { get; }
    bool IsSystemScope { get; }           // true somente dentro de BeginSystemScope
    IReadOnlyCollection<Guid>? EmpresasPermitidas { get; }  // null = sem restrição; populado na Fase 1 (API keys)
}

public interface ITenantScopeFactory
{
    ITenantScope BeginTenantScope(Guid contaId);                       // Worker/handlers: obrigatório por job
    ITenantScope BeginSystemScope(string motivo, string origem);       // bypass auditável; loga abertura/fechamento
}
public interface ITenantScope : IDisposable { }                        // dispose restaura o escopo anterior
```
   Implementação `AmbientTenantContext` com `AsyncLocal<TenantScopeState>`: flui por `async/await`, suporta aninhamento (pilha), e `BeginTenantScope(Guid.Empty)` lança `ArgumentException`.
2. **`TenantDbContext` base** (`BuildingBlocks/Persistence`). Teste primeiro com SQLite in-memory/EF InMemory para o comportamento do filtro; depois: no `OnModelCreating`, varrer `modelBuilder.Model` e, para cada entidade que implementa `ITenantScopedEntity`, registrar query filter nomeado `e => e.ContaId == _tenantContext.ContaId` (filtro nomeado `"Tenant"`, EF Core; avalia `ContaId` a cada query — sem escopo, a exceção sobe: **fail-closed**) e configurar `ContaId` como `conta_id` requerido + índice iniciando por `conta_id` nos índices compostos default. Entidade tenant-scoped **sem** o marker não compila o filtro — é pega pelo teste de arquitetura (passo 4).
3. **Interceptor de escrita** (`SaveChangesInterceptor` no `TenantDbContext`): em `Added`, estampa `ContaId` do contexto se `Guid.Empty`; em qualquer estado, `ContaId != _tenantContext.ContaId` fora de system scope → `TenantMismatchException` (rollback). Escrita em system scope exige `ContaId` explícito na entidade (nunca estampa "sistema").
4. **Testes de arquitetura** (projeto `Tests.Architecture`, NetArchTest + reflexão sobre o `IModel` de cada DbContext concreto — rodam no CI desde já):
   - Toda entidade mapeada em DbContext de módulo implementa `ITenantScopedEntity` **ou** está na allowlist explícita versionada no próprio teste (candidatos: `Plano` — catálogo global; `Outbox`/`Inbox` — infraestrutura, correlação por payload; a allowlist exige justificativa em comentário). Violação = build vermelho.
   - Toda entidade `ITenantScopedEntity` tem query filter registrado no model (`entityType.GetQueryFilter() != null`) e coluna `conta_id` não-nula.
   - Todo DbContext concreto herda de `TenantDbContext`.
   - `IgnoreQueryFilters` proibido fora de `BuildingBlocks`: teste que varre os fontes dos módulos (`Directory.EnumerateFiles` + regex) — bypass só por `BeginSystemScope`.
5. **Middleware da API** (`TenantResolutionMiddleware` no host): resolve o tenant via contrato `ITenantResolver` e envolve o pipeline downstream em `BeginTenantScope`. **Na Fase 0, `StubTenantResolver`**: lê `X-Nfh-Conta-Id` (GUID) **somente** em ambiente `Development`/testes de integração; em qualquer outro ambiente, ausência de resolvedor real → 401. Declarado como interino: a Fase 1 substitui a implementação pelo pipeline de API key **sem alterar o contrato** (`ITenantResolver` devolve `contaId + EmpresasPermitidas`). Endpoints não-tenant (`/alive`, `/health`) são allowlist do middleware e não tocam DbContext tenant-scoped.
6. **Worker**: helper `TenantJobExecutor.Execute(contaId, job)` que abre o escopo, executa e garante dispose; job que resolver serviços tenant-scoped sem escopo ativo falha com `TenantNaoResolvidoException` (comportamento verificado por teste — "job sem escopo é erro, não 'processa tudo'", design §2.2).
7. **Bypass auditável**: `BeginSystemScope(motivo, origem)` desativa o filtro (o predicado vira `IsSystemScope || ContaId == ...`) e emite log estruturado `TenantScopeBypassed { motivo, origem, correlationId }` na abertura — sem PII. Uso legítimo: migrações/seed, projeções cross-conta do Worker (ex.: varredura de certificados a vencer, que então reabre `BeginTenantScope` por conta encontrada), backoffice futuro.

## Critérios de aceite (verificáveis)
1. `dotnet test` do projeto de arquitetura **falha** ao adicionar entidade de teste tenant-scoped sem `ContaId`/marker (demonstrado por fixture negativa no PR) e passa no estado final.
2. Query em entidade tenant-scoped sem escopo ativo lança `TenantNaoResolvidoException` — nunca retorna linhas (teste de integração).
3. Com escopo da conta A, nenhuma query retorna dados da conta B, incluindo `Find`/`Include`/projeções (teste de integração Testcontainers/PostgreSQL).
4. `SaveChanges` com entidade da conta B sob escopo da conta A lança `TenantMismatchException` e nada é persistido.
5. Job do Worker via `TenantJobExecutor` opera só o tenant do escopo; o mesmo job sem escopo falha.
6. `BeginSystemScope` lê cross-conta **e** o log `TenantScopeBypassed` é emitido com motivo/origem (asserção sobre o sink de log de teste).
7. Requisição HTTP com stub resolve o tenant e o dispose ocorre ao fim do request (sem vazamento de `AsyncLocal` entre requests — teste com 2 requests concorrentes).
8. CI executa unidade + arquitetura + integração; tudo verde; nenhum uso de `IgnoreQueryFilters` fora do kernel.

## Plano de testes / Evidências
- **Unidade (xUnit, sem I/O):** `AmbientTenantContext` (sem escopo lança; aninhado restaura; fluxo por `await`/`Task.WhenAll` isolado por branch lógico); `BeginTenantScope(Guid.Empty)` lança; `EmpresasPermitidas` default null; interceptor (stamp, mismatch, system scope exige `ContaId` explícito).
- **Arquitetura (NetArchTest + reflexão de model):** os 4 testes do passo 4; evidência = execução no CI com a fixture negativa revertida no mesmo PR.
- **Integração (Testcontainers/PostgreSQL, espelhando design §4.4 "conta A jamais lê dado da conta B"):** seed A+B numa entidade exemplo do kernel de teste; leitura/escrita/delete cross-tenant; filtro sobrevive a `AsNoTracking`, `Include` e SQL projetado (`ToQueryString` contém predicado `conta_id`); middleware + stub ponta a ponta (2 contas, 2 requests paralelos); Worker com `TenantJobExecutor`; system scope + asserção de log.
- Evidências no PR: saída do `dotnet test` (3 suítes), print do build vermelho com a fixture negativa, `ToQueryString` de uma query filtrada.

## Dependências
- **B1 (estrutura da solution/kernel):** projetos `BuildingBlocks`, hosts API/Worker e projeto de testes de arquitetura existentes — B2 não inicia sem B1 mergeada.
- **A2 (matriz do Gate 0):** ver Assunções abaixo.
- CI da Fase 0 ativo (build → unidade+arquitetura → integração) para o critério "falha o build" ter efeito.
- Fase 1 consome (não bloqueia): `ITenantResolver` real por API key substitui o stub.

## Riscos e pontos de atenção
- **Assunção A2-1 (Gate 0):** esta spec assume a estratégia preliminar da matriz — solution nova na Fase 0, sem reaproveitar `TenantService`/filtros do código legado (`VisuFiscalHub`). Se a matriz final decidir ADAPTAR o kernel legado, os contratos desta spec permanecem o alvo e o legado é portado até passar exatamente nesta suíte de testes.
- **Assunção A2-2:** nomes de namespace/solution (`NotaFiscalHub.*` vs legado) saem de B1/A2; a spec referencia caminhos lógicos (`BuildingBlocks/Tenancy`), não nomes finais.
- **Fail-open acidental:** o maior risco é filtro registrado mas contornável. Mitigações já no escopo: exceção (não default) quando sem escopo; proibição de `IgnoreQueryFilters`; interceptor de escrita como segunda linha. SQL cru/Dapper **não** é coberto pelo query filter — fica proibido nos módulos até existir revisão específica (adicionar à mesma varredura de fontes do passo 4, bloqueando `FromSqlRaw`/`SqlQueryRaw` fora do kernel).
- **Vazamento de `AsyncLocal`** em pools de threads do Worker/Hangfire-like: `ITenantScope` deve restaurar o estado anterior no dispose e o executor deve envolver todo o job — coberto pelo critério 7 e pelos testes de aninhamento.
- **Allowlist de entidades não-tenant** é ponto de revisão obrigatório em todo PR que a alterar (justificativa no comentário do teste); crescimento silencioso da allowlist anula a garantia.
- **Filtro adicional por `EmpresasPermitidas`** (escopo de key) NÃO entra no query filter global nesta tarefa — decisão explícita para a Fase 1 (aplicá-lo na borda/handlers ou como segundo filtro nomeado), evitando design especulativo agora.
- RLS de banco como cinturão extra não está no design/MVP; se algum cliente exigir, tratar como decisão nova em `docs/decisions.md` (não retrofitar em silêncio).
