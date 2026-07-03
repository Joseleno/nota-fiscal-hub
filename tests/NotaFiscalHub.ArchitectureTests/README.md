# NotaFiscalHub.ArchitectureTests

Suíte de testes de arquitetura (xUnit + [NetArchTest.Rules](https://github.com/BenMorris/NetArchTest)) que
codifica as regras de fronteira do design (`docs/superpowers/specs/2026-07-01-nota-fiscal-hub-produto-design.md`
§2.3 e §2.5) como gate obrigatório de merge. Uma violação de fronteira falha o build aqui, não no code
review.

Executar: `dotnet test tests/NotaFiscalHub.ArchitectureTests` — suíte inteira roda em menos de 30s
(tipicamente < 5s).

## Mapa regra → teste → seção do design

| Regra | O que garante | Teste(s) | Seção do design |
|---|---|---|---|
| **T1** | Matriz de referências permitidas entre módulos (quem pode depender de quem, e só via `*.Contracts`) | `ReferenceRulesTests.Fronteiras_Emissao_SoReferenciaContratosPermitidos`, `Fronteiras_MotorNfce_SoReferenciaEmpresasCertificadosContracts`, `Fronteiras_Documentos_NaoReferenciaOutrosModulos`, `Fronteiras_EmpresasCertificados_NaoReferenciaOutrosModulos`, `Fronteiras_ContasPlanos_NaoReferenciaOutrosModulos`, `NenhumModuloDeNegocio_Referencia_ContasPlanosForaDaAllowlist` | §2.5 (setas de dependência) |
| **T2** | Contratos (`*.Contracts`) só expõem DTOs — nunca EF Core, nunca Domain/Application/Infrastructure de nenhum módulo | `ContractPurityTests.Contratos_NaoReferenciaEntityFrameworkCore`, `Contratos_NaoExpoeEntidadesDeDominio` | §2.3 regra 3 |
| **T3** | Entidade de domínio de um módulo nunca é referenciada por outro módulo (comunicação síncrona só carrega DTOs) | `DomainIsolationTests.EntidadeDeDominio_NaoEUsadaForaDoProprioModulo` | §2.3 regra 3 (combinado com T2) |
| **T4** | `Domain` de cada módulo não referencia a própria `Infrastructure`/`Application` nem EF Core/ASP.NET Core (Domain é POCO puro) | `DomainIsolationTests.Domain_NaoReferenciaInfrastructureNemFrameworks`, `Domain_NaoReferenciaInfrastructure` | Clean Architecture (regra geral, não numerada no design) |
| **T5** | Toda entidade mapeada em um `DbContext` de módulo implementa `ITenantScopedEntity` com `ContaId` não-anulável, salvo allowlist explícita de entidade global | `TenantScopedEntityTests.TenantScoped_ExigeContaId` | §2.3.5 (tenant como fronteira de primeira classe) |
| **T6** | (a) todo `DbContext` concreto herda de `TenantDbContext`; (b) toda entidade tenant-scoped tem o query filter nomeado `"Tenant"` registrado, com `ContaId` não-anulável | `TenantScopedEntityTests.TodoDbContextConcreto_HerdaDeTenantDbContext`, `TenantScoped_TemQueryFilterRegistradoEColunaContaIdNaoNula`, `IgnoreQueryFilters_ProibidoForaDoKernel` | §2.3.5 |
| **T7** | Física espelha a lógica: nenhum módulo referencia os hosts (`Api`/`Worker`); `BuildingBlocks` não referencia módulo de negócio fora da exceção codificada | `PhysicalStructureTests.Modulos_NaoReferenciamHosts`, `BuildingBlocks_NaoReferenciaModulosForaDaExcecaoCodificada` | §2.3 regra 7 |

Além das 7 regras: `ReferenceRulesTests.Fixture_CarregaPeloMenosUmaAssemblyPorModulo` é a fixture de carga
(guarda contra suíte "verde" por não ter carregado nenhum assembly `NotaFiscalHub.*`) e
`ArchitectureExceptionsGuardTests` é o teste-guardião do processo de exceção (ver abaixo).

## Processo de exceção

Toda exceção a uma regra de fronteira vive em **um único lugar**: `ArchitectureExceptions.cs`
(`ArchitectureExceptions.Todas`, uma lista de `ExcecaoArquitetura`). Regras:

1. **Por par origem→destino, nunca por módulo inteiro.** Abrir uma exceção para
   `BuildingBlocks → ContasPlanos.Contracts` não relaxa a regra para `BuildingBlocks → Emissao.Contracts`.
2. **Justificativa e `DecisaoRef` são obrigatórias.** `ArchitectureExceptionsGuardTests.TodaExcecao_TemJustificativaEDecisaoRef`
   falha o build se qualquer entrada tiver `Justificativa` ou `DecisaoRef` vazia/em branco — a allowlist
   não pode crescer silenciosamente.
3. **Nova exceção = PR alterando `ArchitectureExceptions.cs` + registro em `docs/decisions.md`.** O PR
   precisa referenciar a decisão (`D-2026-07-01-xx`) ou a seção do design que a motivou.

Seed inicial (única entrada, verificada por `ArchitectureExceptionsGuardTests.SeedInicial_ContemExatamenteUmaEntrada`):

| Regra | Origem | Destino | Justificativa |
|---|---|---|---|
| T1 | `BuildingBlocks` | `ContasPlanos.Contracts` | Middleware de autenticação consome credencial/consumo/webhook-config (§2.5) |

## Convenções usadas pelas regras

- **Nomes de módulo centralizados** em `ModuleNames.cs` — nenhuma regra usa string de namespace solta;
  uma renomeação de módulo quebra a compilação da suíte em vez de deixar uma regra incapaz de encontrar
  seu assembly.
- **Carga de assembly por convenção** em `AssemblyLoader.cs` — cada projeto tem um `AssemblyMarker` no
  namespace raiz, referenciado a partir daqui, para forçar o carregamento eager e determinístico do
  assembly antes da suíte rodar.
- **T5/T6 não são NetArchTest puro** — usam reflexão sobre `IModel` do EF Core (via
  `DbContext` instanciado com `UseInMemoryDatabase`) para inspecionar o mapeamento real de entidades e
  query filters, algo que NetArchTest (que só analisa referências entre assemblies) não alcança.
