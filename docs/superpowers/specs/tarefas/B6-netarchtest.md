# Spec B6 — Suíte NetArchTest travando fronteiras
> Card: https://app.clickup.com/t/86e24c1jz | Grupo B | Gerada em 2026-07-02 por time de agentes (escritor especialista + revisor adversarial)

## Objetivo
Codificar as regras de fronteira do design (§2.3 e §2.5) como testes de arquitetura executáveis (NetArchTest.Rules + xUnit) que **falham o build no CI (B9)** a cada violação. Governança como código: a matriz de dependências entre módulos, a pureza dos contratos e as invariantes de tenant deixam de ser convenção e viram gate obrigatório desde a Fase 0 (constraint global: "NetArchTest no CI desde a Fase 0").

## Contexto e referências
- Design §2.3 (regras de fronteira 1–7, em especial 3, 5 e 7) e §2.5 (setas de dependência permitidas): `docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`.
- Plano-mestre, Global Constraints + Fase 0 (item "Suite NetArchTest"): `docs/superpowers/plans/2026-07-01-nota-fiscal-hub-mvp-plano-mestre.md`.
- Decisões D-2026-07-01-01..10 (`docs/decisions.md` §0) — nenhuma altera fronteiras; D-10 confirma `leitura`/`NotaConsulta` como propriedade da Emissão (afeta a matriz: projeto de leitura pertence à Emissão, não é módulo).
- Integrações: **B2** (kernel de tenant: `ITenantContext`, `ITenantScopedEntity`, `TenantDbContext` base), **B9** (pipeline CI que executa a suíte no estágio de testes de arquitetura).

### Assunções dependentes da matriz do Gate 0 (A2) — validar antes de codar
1. Estratégia = solution nova (matriz preliminar do handoff); estrutura `Modules/<Modulo>/{Domain,Application,Infrastructure,Contracts}` + `BuildingBlocks/` + `Api/` + `Worker/`. Se A2 decidir evoluir in-place, os nomes de assembly/namespace desta spec são re-mapeados, **as regras não mudam**.
2. Convenção de namespace raiz: `NotaFiscalHub.Modules.{ContasPlanos|EmpresasCertificados|Emissao|MotorNfce|Documentos}.*` e `NotaFiscalHub.BuildingBlocks.*`.
3. B2 entrega os tipos-marcadores que os testes T5/T6 referenciam. Se B6 for executada antes de B2, T5/T6 nascem `[Fact(Skip="aguardando B2")]` com issue de rastreio — nunca omitidos.

## Escopo (dentro / fora)
**Dentro:** projeto `Tests/ArchitectureTests` (xUnit + NetArchTest.Rules); regras T1–T7 abaixo; allowlist de exceções com justificativa obrigatória; documentação do processo de exceção; integração no target de teste consumido pelo CI.
**Fora:** o pipeline em si (B9); implementação do kernel de tenant (B2); regras de estilo/naming não ligadas a fronteira; validação de conteúdo de eventos (regra §2.3.6 é coberta por testes funcionais, não estruturais); fitness functions de runtime.

## Abordagem / Passos
TDD estrito (constraint global): cada regra nasce com um **teste que falha contra um projeto-isca** (`Tests/ArchitectureTests/Violations/` — código mínimo que viola a regra, compilado apenas no teste ou simulado com tipos internos) antes de a regra ser considerada implementada. Ordem:

1. **Criar `Tests/ArchitectureTests`** com fixture que carrega todos os assemblies da solution por convenção (`NotaFiscalHub.*`), falhando se a lista estiver vazia (proteção contra suíte verde por não carregar nada).
2. **T1 — Matriz de referências entre módulos** (§2.5). Um teste por linha da matriz, com nome que aponta a regra violada:

| De (assembly) | Pode referenciar (além do próprio módulo e BuildingBlocks) |
|---|---|
| Emissao.* | EmpresasCertificados.Contracts, MotorNfce.Contracts, Documentos.Contracts |
| MotorNfce.* | EmpresasCertificados.Contracts (somente — CSC/chave não transitam) |
| Documentos.* | (nenhum outro módulo) |
| EmpresasCertificados.* | (nenhum outro módulo) |
| ContasPlanos.* | (nenhum outro módulo) |
| Api, Worker (hosts) | *.Contracts de todos os módulos + BuildingBlocks |
| BuildingBlocks (kernel) | **ContasPlanos.Contracts apenas** (exceção codificada: middleware de autenticação consome credencial/consumo/webhook-config) |

   Regra dura complementar: `NenhumModuloDeNegocio_Referencia_ContasPlanos` — só host e kernel entram na exceção (§2.5, última seta). Implementação: `Types.InAssembly(x).ShouldNot().HaveDependencyOnAny("NotaFiscalHub.Modules.ContasPlanos", ...)` com allowlist explícita.
3. **T2 — Contratos só com DTOs** (§2.3.3): tipos em `*.Contracts` só dependem de `System.*`, do próprio namespace e de primitivas do BuildingBlocks; `*.Contracts` **não referencia** `*.Domain`, `*.Application`, `*.Infrastructure` nem EF Core (`Microsoft.EntityFrameworkCore`).
4. **T3 — Entidade de domínio não cruza fronteira**: tipos de `{Modulo}.Domain` não são referenciados por nenhum assembly fora de `{Modulo}.*`. Combinado com T2, garante que comunicação síncrona só carrega DTOs.
5. **T4 — Domain não referencia Infrastructure** (nem Application, nem EF Core, nem `Api`/`Worker`): `Types.InAssembly({Modulo}.Domain).ShouldNot().HaveDependencyOnAny("...Infrastructure", "...Application", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")`.
6. **T5 — Entidade tenant-scoped tem `conta_id`** (§2.3.5, integra B2): toda classe mapeada em um `DbContext` de módulo (exceto allowlist explícita de entidades globais: `Plano`, catálogos, `Outbox`/`Inbox` — dedupe é por `messageId`) implementa `ITenantScopedEntity` (B2) e expõe `ContaId` não-anulável. Verificação por reflexão sobre `IModel` de cada contexto (NetArchTest não lê o mapping EF).
7. **T6 — Todo DbContext tem filtro de tenant**: (a) estrutural — todo `DbContext` da solution herda de `TenantDbContext` (B2); (b) semântica — para cada contexto, toda entidade `ITenantScopedEntity` no `IModel` possui `GetQueryFilter()` não-nulo cujo corpo referencia `ContaId` (inspeção da expression tree). Um DbContext novo sem filtro **falha o build**, não o code review.
8. **T7 — Física espelha a lógica** (§2.3.7): nenhum projeto `Modules/*` referencia `Api`/`Worker`; `BuildingBlocks` não referencia nenhum `Modules/*` exceto a exceção de T1.
9. **Processo de exceção**: allowlist única `ArchitectureExceptions.cs` — `record ExcecaoArquitetura(string Regra, string Origem, string Destino, string Justificativa, DateOnly Data, string DecisaoRef)`. Teste-guardião falha se `Justificativa` ou `DecisaoRef` (ex.: `D-2026-07-01-xx` ou link do card) estiver vazia. Nova exceção = PR alterando este arquivo + registro em `docs/decisions.md`; exceções são **por par origem→destino**, nunca por módulo inteiro. A seed inicial contém exatamente 1 entrada: kernel/host → ContasPlanos.Contracts (§2.5).

## Critérios de aceite (verificáveis)
1. `dotnet test Tests/ArchitectureTests` verde na solution conforme; **vermelho** (build do CI falha) ao introduzir qualquer uma das violações do plano de testes abaixo.
2. Cada regra T1–T7 é um teste nomeado individualmente (`Fronteiras_Emissao_SoReferenciaContratosPermitidos`, `Contratos_NaoExpoeEntidadesDeDominio`, `TenantScoped_ExigeContaId`, `DbContext_ExigeFiltroDeTenant`, `Domain_NaoReferenciaInfrastructure`, ...) — a mensagem de falha lista os tipos ofensores (`GetResult().FailingTypeNames`).
3. Exceção sem justificativa ou sem `DecisaoRef` → teste-guardião falha.
4. Fixture falha se zero assemblies `NotaFiscalHub.*` forem carregados.
5. Suíte roda no estágio de arquitetura do CI (B9) como gate obrigatório de merge; tempo de execução < 30s.
6. README curto em `Tests/ArchitectureTests/` documentando: mapa regra→teste→seção do design e o processo de exceção (passo 9).

## Plano de testes / Evidências
Para cada regra, um caso de violação provado vermelho antes do verde (TDD):
- T1: adicionar `ProjectReference` de `Emissao.Application` → `ContasPlanos.Contracts` — build de CI falha com o nome do teste da matriz.
- T2: expor `NotaFiscal` (entidade) como retorno em interface de `MotorNfce.Contracts`.
- T3: `Documentos.Application` usando tipo de `Emissao.Domain`.
- T4: `using Microsoft.EntityFrameworkCore` em `Emissao.Domain`.
- T5: nova entidade mapeada sem `ContaId` fora da allowlist global.
- T6: `DbContext` derivando direto de `Microsoft.EntityFrameworkCore.DbContext`; e entidade tenant-scoped com `HasQueryFilter` removido.
- T7: `BuildingBlocks` referenciando `Emissao.Contracts` sem entrada na allowlist.
Evidências no PR: saída do `dotnet test` com cada violação (screenshot/log do vermelho) + execução verde final; link do run do CI (B9) com o estágio de arquitetura obrigatório.

## Dependências
- **A2 (matriz Gate 0)**: ratifica estrutura da solution e nomes de assembly (assunções 1–2). Bloqueia apenas o hard-coding dos nomes, não o desenho das regras.
- **B2**: `ITenantScopedEntity` + `TenantDbContext` para T5/T6 (senão, `Skip` rastreado).
- **B9**: estágio de CI que executa a suíte e bloqueia merge.
- Pacotes: `NetArchTest.Rules` (verificar manutenção via skill dependency-audit; alternativa aprovável: `ArchUnitNET` — decisão registrada no PR).

## Riscos e pontos de atenção
- **Falso verde por assembly não carregado**: NetArchTest só analisa o que foi carregado; mitigado pelo critério 4 + carga por convenção com contagem mínima esperada por módulo.
- **T6 não é 100% estrutural**: inspeção de expression tree do query filter é o único jeito de garantir a semântica; manter o teste (b) simples (filtro existe e menciona `ContaId`) — a corretude do isolamento é coberta pelos testes de integração de B2.
- **Erosão por exceção**: sem o teste-guardião do passo 9, a allowlist vira ralo; exceções por par origem→destino e revisão obrigatória em `docs/decisions.md` são o freio.
- **Renomeações quebram regras baseadas em string de namespace**: centralizar os nomes de módulo em constantes únicas (`ModuleNames.cs`) usadas por todas as regras.
- **Motor NFSe/Cadastros Fiscais (fases futuras)**: ao nascerem, entram na matriz T1 por PR — a seta Emissão → Cadastros Fiscais (§2.5) já está prevista no design e deve ser adicionada com a fase, não antecipada.
