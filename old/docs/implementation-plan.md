# VisuFiscalHub — Plano de Implementação

**Versão:** 3.0
**Data:** 2026-05-11
**Status do scaffold:** Estrutura de projetos criada, packages NuGet configurados, sem código de domínio implementado.

---

## Estado Atual do Repositório

### O que existe (scaffold)

| Artefato | Status |
|---|---|
| Estrutura de solução `.slnx` com 4 projetos + projetos de testes | **Criado** |
| `VisuFiscalHub.Domain.csproj` (sem dependências externas) | **Criado** |
| `VisuFiscalHub.Application.csproj` (FluentValidation, Mediator.SourceGenerator) | **Criado** |
| `VisuFiscalHub.Infrastructure.csproj` (EF Core 10, Npgsql, Hangfire.PostgreSql) | **Criado** |
| `VisuFiscalHub.Api.csproj` (Hangfire.AspNetCore, Scalar.AspNetCore, Serilog) | **Criado** |
| `VisuFiscalHub.UnitTests.csproj` (xUnit, Shouldly, NSubstitute, coverlet) | **Criar** |
| `VisuFiscalHub.IntegrationTests.csproj` (WebApplicationFactory, Testcontainers) | **Criar** |
| Pastas de namespace em todos os projetos (`Entities/`, `Enums/`, `Interfaces/`, etc.) | **Criadas** |
| `Program.cs` com placeholder WeatherForecast | **Criado** (substituir) |
| `appsettings.json` / `appsettings.Development.json` | **Criados** (configurar) |
| `docs/decisions.md` com decisões arquiteturais completas | **Criado** |

### O que falta implementar

Tudo que está listado nas Fases 1 a 10 abaixo. Nenhum arquivo `.cs` de domínio, aplicação, infraestrutura ou API foi escrito ainda.

---

## Visão Geral das Fases

```
Fase 1: Domain Layer              (base — zero dependências externas)
Fase 2: Infraestrutura: Banco     (EF Core + PostgreSQL)
Fase 3: Application: ClienteApp e Tenant
Fase 4: Autenticação JWT
Fase 5: Application: Emissão de Documentos
Fase 6a: Infraestrutura: Criptografia, Assinatura XML e Tributação  (sem banco)
Fase 6b: Infraestrutura: Certificados com banco e OutboxRelayJob    (depende Fase 2)
Fase 7: Integração SEFAZ
Fase 8: API Endpoints
Fase 9: Docker e Deploy
Fase 10: Testes
```

---

## Fase 1 — Domain Layer

**Esforço:** P

**Objetivo:** Construir os alicerces do domínio sem nenhuma dependência externa. Esta fase produz o núcleo imutável do sistema — primitivos, entidades, value objects, enums, interfaces de repositório e interfaces de serviço da Application layer.

### Entregáveis

#### Primitivos (`Domain/Common/`)

- `Result<T>` — wrapper de resultado que carrega `Value` ou `Error` sem lançar exceções
- `Error` — record com `Code` (string) e `Message` (string); sem herança de `Exception`
- `Entity<TId>` — classe base abstrata com Id fortemente tipado e comparação por identidade; implementa `IDomainEventSource` (expõe `IReadOnlyList<IDomainEvent> DomainEvents` e `void ClearDomainEvents()`)
- `StronglyTypedId<T>` — record struct base para identificadores, com conversão implícita para/de `T`

#### Identificadores (`Domain/Identifiers/`)

- `ClienteAppId` — wraps `Guid`
- `TenantId` — wraps `Guid`
- `DocumentoFiscalId` — wraps `Guid`
- `DeliveryAttemptId` — wraps `Guid`

#### Enumerações (`Domain/Enums/`)

- `TipoDocumento` — `NfCe = 65`, `NFe = 55`, `NFSe = 99`
- `StatusDocumento` — `Criado`, `Enfileirado`, `Processando`, `Autorizado`, `Rejeitado`, `Cancelado`, **`Falhou`**, **`Denegado`**
- `RegimeTributario` — `SimplesNacional = 1`, `SimplesNacionalExcesso = 2`, `RegimeNormal = 3`
- `TipoEmissao` — `Normal = 1`, `Contingencia = 9`
- `AmbienteSefaz` — `Producao = 1`, `Homologacao = 2`
- `TipoPagamento` — `Dinheiro = 01`, `Cheque = 02`, `CartaoCredito = 03`, `CartaoDebito = 04`, `CreditoLoja = 05`, `ValeAlimentacao = 10`, `ValeRefeicao = 11`, `ValePresente = 12`, `ValeCombustivel = 13`, `PixDinamico = 17`, `PixEstatico = 20`, `Outros = 99` — valores conforme IT 2024.002
- `ModalidadeFrete` — `SemFrete = 9`, `EmitenteCIF = 0`, `DestinatarioFOB = 1`, `TerceiroCIF = 2`, `TerceiroFOB = 3`, `ProprioRemetente = 4`, `ProprioDestinatario = 5`
- `TipoIcms` — `CSOSN`, `CST`
- `OrigemMercadoria` — `Nacional = 0`, `EstrangeiraImportacaoDireta = 1`, `EstrangeiraAdquiridaInterna = 2`, `NacionalConteudoImportacaoSuperior40 = 3`, `NacionalProcessosBasicos = 4`, `NacionalConteudoImportacaoInferior40 = 5`, `EstrangeiraImportacaoDiretaSemSimilar = 6`, `EstrangeiraAdquiridaInternaSemSimilar = 7`, `NacionalConteudoImportacao40A70 = 8`
- `CSOSN` — `Csosn102 = 102`, `Csosn300 = 300`, `Csosn400 = 400`, `Csosn500 = 500`, `Csosn900 = 900`
- `CstIcms` — `Cst00 = 0`, `Cst10 = 10`, `Cst20 = 20`, `Cst30 = 30`, `Cst40 = 40`, `Cst41 = 41`, `Cst50 = 50`, `Cst51 = 51`, `Cst60 = 60`, `Cst70 = 70`, `Cst90 = 90`
- `CstPisCofins` — `Cst01 = 1`, `Cst02 = 2`, `Cst03 = 3`, `Cst04 = 4`, `Cst05 = 5`, `Cst06 = 6`, `Cst07 = 7`, `Cst08 = 8`, `Cst09 = 9`, `Cst49 = 49`, `Cst50 = 50`, `Cst51 = 51`, `Cst99 = 99`
- `TipoTentativa` — `Envio`, `Consulta`, `Retry`

#### Value Objects (`Domain/ValueObjects/`)

- `Cnpj` — valida formato + dígitos verificadores; aceita entrada com ou sem máscara (normaliza automaticamente); expõe `Valor` sem máscara (14 dígitos)
- `Cpf` — valida formato + dígitos verificadores; aceita entrada com ou sem máscara; expõe `Valor` sem máscara (11 dígitos)
- `ChaveAcesso` — 44 dígitos; inclui método estático `Gerar(cUF, aamm, cnpj, mod, serie, nNF, tpEmis, cNF)` que calcula `cDV` (Módulo 11). O parâmetro `cNF` deve ser gerado externamente com `RandomNumberGenerator.GetBytes(4)` convertido para 8 dígitos decimais — nunca `Random.Shared`
- `QrCode` — URL final do QR Code; contém método estático `Gerar(chaveAcesso, tpAmb, csc, cIdToken, urlConsulta)` com hash SHA-1 segundo NT 2019.001 v1.50
- `Endereco` — `Logradouro`, `Numero`, `Complemento`, `Bairro`, `Municipio`, `CodigoMunicipio`, `Uf`, `Cep`, `CodigoPais`, `Telefone`
- `Produto` — `CodigoProduto`, `Descricao`, `Ncm`, `Cest`, `CfopSaida`, `UnidadeComercial`, `Quantidade`, `ValorUnitario`, `ValorDesconto`, `OrigemMercadoria`
- `Tributo` — `TipoIcms`, `CsosnOuCst`, `AliquotaIcms`, `BaseCalculoIcms`, `ValorIcms`, `CstPis`, `ValorPis`, `CstCofins`, `ValorCofins`
- `Pagamento` — `TipoPagamento`, `Valor`; método de validação do total de pagamentos vs valor da nota
- `ConfiguracaoFiscal` — value object do Tenant (mapeado via `OwnsOne`) contendo: `Crt` (RegimeTributario), `Serie` (string), `Ambiente` (AmbienteSefaz), `UfCodigo` (int). **Não** inclui `Csc`/`CIdToken` — estes ficam como campos diretos no Tenant por envolverem criptografia.
- `CertificadoDigital` — value object com `VencimentoEm` (DateTime) e `PfxBytes` (byte[])

#### Entidades (`Domain/Entities/`)

- `ClienteApp : Entity<ClienteAppId>`
  - Propriedades: `Name`, `ClientId`, `ClientSecretHash`, `WebhookUrl?`, `WebhookSecretCriptografado?` (byte[]), `IsActive`, `CreatedAt`
  - Factory: `static Result<ClienteApp> Criar(name, clientId, clientSecretHash)`
  - Método: `Desativar()`, `AtualizarWebhookSecret(webhookSecretCriptografado)`

- `Tenant : Entity<TenantId>`
  - Propriedades: `ClienteAppId`, `Cnpj`, `RazaoSocial`, `NomeFantasia?`, `ConfiguracaoFiscal` (value object), `Csc?` (byte[] — criptografado), `CIdToken?`, `CertificadoPfxCriptografado?` (byte[]), `CertificadoSenhaCriptografada?` (byte[]), `CertificadoVencimento?` (DateTime), `Endereco`, `IsActive`, `CreatedAt`
  - Factory: `static Result<Tenant> Criar(clienteAppId, cnpj, razaoSocial, configuracaoFiscal, endereco)`
  - Métodos: `AtualizarCertificado(pfxCriptografado, senhaCriptografada, vencimento)`, `AtualizarCsc(cscCriptografado, cIdToken)`, `Desativar()`

- `DocumentoFiscal : Entity<DocumentoFiscalId>`
  - Propriedades: `TenantId`, `IdempotencyKey`, `Tipo`, `ChaveAcesso`, `Numero`, `Serie`, `Status`, `XmlAssinado?`, `Protocolo?`, `QrCode?`, `MotivoRejeicao?`, `CreatedAt`, `AuthorizedAt?`, `_items` (backing field `List<ItemDocumento>`), `_pagamentos` (backing field `List<Pagamento>`)
  - Factory: `static Result<DocumentoFiscal> Criar(tenantId, idempotencyKey, tipo, numero, serie, itens, pagamentos)`
  - **State machine explícita com transições válidas:**
    - `Enfileirar()` — `Criado → Enfileirado`
    - `IniciarProcessamento()` — `Enfileirado → Processando`
    - `Autorizar(protocolo, xmlAssinado, qrCode, authorizedAt)` — `Processando → Autorizado`; publica `DocumentoFiscalAutorizadoEvent`
    - `Rejeitar(motivo)` — `Processando → Rejeitado`; publica `DocumentoFiscalRejeitadoEvent`
    - `Cancelar(timeProvider)` — `Autorizado → Cancelado` (valida `AuthorizedAt + 30min < timeProvider.GetUtcNow()`); publica `DocumentoFiscalCanceladoEvent`
    - `Falhar()` — `Processando → Falhou`; publica `DocumentoFiscalFalhouEvent`
    - `Denegar(motivo)` — `Processando → Denegado`; publica `DocumentoFiscalDenegadoEvent`
    - Transições inválidas retornam `Result.Failure(DocumentoFiscalErrors.TransicaoInvalida)`
  - Expõe `IReadOnlyList<ItemDocumento> Items` e `IReadOnlyList<Pagamento> Pagamentos` (backed por `_items` e `_pagamentos`)

- `ItemDocumento` — sem identidade própria (owned entity); propriedades: `Numero`, `Produto`, `Tributo`, `ValorTotal`

- `DeliveryAttempt : Entity<DeliveryAttemptId>`
  - Propriedades: `DocumentoFiscalId`, `TipoTentativa` (enum: `Envio`, `Consulta`, `Retry`), `AttemptedAt`, `Success`, `ResponseCode?`, `ResponseMessage?`, `ElapsedMs`

#### Domain Events (`Domain/Events/`)

- `DocumentoFiscalAutorizadoEvent` — payload: `DocumentoFiscalId`, `TenantId`, `ClienteAppId`, `ChaveAcesso`, `Protocolo`, `AuthorizedAt`
- `DocumentoFiscalRejeitadoEvent` — payload: `DocumentoFiscalId`, `TenantId`, `MotivoRejeicao`
- `DocumentoFiscalCanceladoEvent` — payload: `DocumentoFiscalId`, `TenantId`, `CanceladoAt`
- `DocumentoFiscalFalhouEvent` — payload: `DocumentoFiscalId`, `TenantId`, `FalhouAt`
- `DocumentoFiscalDenegadoEvent` — payload: `DocumentoFiscalId`, `TenantId`, `Cnpj`, `XMotivo`
- `TenantProvisionadoEvent` — payload: `TenantId`, `ClienteAppId`, `Cnpj`

#### Errors (`Domain/Errors/`)

- `ClienteAppErrors` — `NaoEncontrado`, `ClientIdJaExiste`, `Inativo`
- `TenantErrors` — `NaoEncontrado`, `CnpjInvalido`, `CnpjJaCadastrado`, `NaoPertenceAoClienteApp`, `SemCertificado`, `Inativo`
- `DocumentoFiscalErrors` — `NaoEncontrado`, `IdempotencyKeyJaUsada`, `StatusInvalidoParaOperacao`, `TransicaoInvalida`, `TotalPagamentosInvalido`, `ValorTotalInvalido`, `PrazoDeCancel amentouExpirado`

#### Interfaces de Repositório (`Domain/Interfaces/`)

- `IClienteAppRepository` — `GetByClientIdAsync`, `GetByIdAsync`, `AddAsync`
- `ITenantRepository` — `GetByIdAsync`, `GetByCnpjAsync`, `GetByClienteAppIdAsync`, `AddAsync`, `GetNextNumeracaoAsync(tenantId, serie)`
- `IDocumentoFiscalRepository` — `GetByIdAsync`, `GetByIdempotencyKeyAsync`, `AddAsync`, `UpdateAsync`, `GetProcessandoAntigoAsync(timeout)`
- `IUnitOfWork` — `SaveChangesAsync`

#### Interfaces de Application layer (`Application/Common/Interfaces/`)

- `IDocumentJobQueue` — `void EnqueueProcessing(DocumentoFiscalId id)` — substitui referência direta a `IBackgroundJobClient` do Hangfire nos handlers
- `ITenantCertificateProvider` — `Task<CertificadoDigital> GetCertificateAsync(TenantId tenantId)`
- `ICertificateEncryptionService` — `byte[] Encrypt(byte[] pfxBytes, string senha)`, `(byte[] pfxBytes, string senha) Decrypt(byte[] encrypted)`; sobrecargas para `string` (CSC e webhookSecret)
- `ITributacaoCalculator` — interface na Application layer para cálculo de tributos por CRT
- `INfceXmlBuilder` — interface na Application layer para montagem do XML NFC-e
- `IQrCodeGenerator` — interface na Application layer para geração do QR Code
- `ISefazClient` — `Task<SefazRetorno> SubmeterAutorizacaoAsync(DocumentoFiscal documento, Tenant tenant, CancellationToken ct)` e `Task<SefazConsultaRetorno> ConsultarNfeAsync(string chaveAcesso, Tenant tenant, CancellationToken ct)` — abstração do client SEFAZ
- `IWebhookDeliveryService` — `Task DeliverAsync(DocumentoFiscalId documentoId, ClienteAppId clienteAppId, CancellationToken ct)`
- `ITokenService` — `string GenerateToken(ClienteApp clienteApp)`
- `ICurrentUserContext` — expõe `ClienteAppId` e `TenantId`; populada pela implementação na camada Api

### Critério de Conclusão

- Todos os tipos acima compilam sem erros em `net10.0`
- `ChaveAcesso.Gerar(...)` produz string de 44 dígitos com cDV correto (validado contra valor do MOC 7.0 Seção 4.1.6)
- `QrCode.Gerar(...)` produz hash SHA-1 no formato esperado pelo SEFAZ (validado contra valor pré-computado externamente)
- `Cnpj` e `Cpf` rejeitam valores inválidos com `Result.Failure` e normalizam entrada com máscara
- Transições inválidas de `DocumentoFiscal` retornam `Result.Failure` — nunca lançam exceção
- `StatusDocumento` inclui `Falhou` e `Denegado`
- Zero referências a packages externos no projeto `Domain`

---

## Fase 2 — Infraestrutura: Banco de Dados

**Esforço:** M

**Objetivo:** Mapear o domínio para PostgreSQL via EF Core 10 e disponibilizar repositórios concretos. Dependência: Fase 1 concluída.

### Entregáveis

#### DomainEventsInterceptor (`Infrastructure/Persistence/`)

- `DomainEventsInterceptor : SaveChangesInterceptor`
  - Sobrescreve `SavingChangesAsync` — intercepta antes do commit
  - Itera `ChangeTracker.Entries<IDomainEventSource>()`
  - Serializa cada domain event como `OutboxMessage` com `EventType` (nome qualificado) e `Payload` (JSON via `System.Text.Json`)
  - Insere `OutboxMessage` na mesma transação atômica
  - Chama `entity.ClearDomainEvents()` após coleta
  - Registrado via `dbContextOptions.AddInterceptors(new DomainEventsInterceptor())`

#### ApplicationDbContext (`Infrastructure/Persistence/`)

- `ApplicationDbContext : DbContext`
  - `DbSet<ClienteApp>`, `DbSet<Tenant>`, `DbSet<DocumentoFiscal>`, `DbSet<DeliveryAttempt>`, `DbSet<OutboxMessage>`
  - Override de `OnModelCreating` com chamada a todas as configurações
  - Conversores de value types: `ClienteAppId`, `TenantId`, `DocumentoFiscalId`, `Cnpj`, `ChaveAcesso` etc.

#### Configurações EF (`Infrastructure/Persistence/Configurations/`)

- `ClienteAppConfiguration : IEntityTypeConfiguration<ClienteApp>`
  - Tabela: `cliente_apps`; índice único em `client_id`
  - `WebhookSecretCriptografado` como `bytea` (nullable)

- `TenantConfiguration : IEntityTypeConfiguration<Tenant>`
  - Tabela: `tenants`; índice único em `(cnpj, cliente_app_id)`; FK para `ClienteApp`
  - `builder.OwnsOne(t => t.Endereco, ...)` — colunas prefixadas `end_`
  - `builder.OwnsOne(t => t.ConfiguracaoFiscal, cfg => { cfg.Property(c => c.Crt)...; cfg.Property(c => c.Serie)...; cfg.Property(c => c.Ambiente)...; cfg.Property(c => c.UfCodigo)...; })`
  - Colunas `csc`, `certificado_pfx_criptografado`, `certificado_senha_criptografada` como `bytea` (nullable)
  - Coluna `certificado_vencimento` (DateTime nullable) para monitoramento de expiração

- `DocumentoFiscalConfiguration : IEntityTypeConfiguration<DocumentoFiscal>`
  - Tabela: `documentos_fiscais`; índice único composto `(tenant_id, idempotency_key)`; índice em `chave_acesso`
  - **Índice em `(status, created_at)`** para suportar query do `ReconciliacaoJobProcessor`
  - `builder.OwnsMany(d => d.Items, b => { b.HasField("_items"); b.ToTable("itens_documento"); ... })`
  - `builder.OwnsMany(d => d.Pagamentos, b => { b.HasField("_pagamentos"); b.ToTable("pagamentos_documento"); ... })`

- `DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>`
  - Tabela: `delivery_attempts`; FK para `DocumentoFiscal`

- `OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>`
  - Tabela: `outbox_messages`
  - Campos: `Id` (Guid), `EventType` (string), `Payload` (string JSON), `OccurredAt` (DateTime), `ProcessedAt?` (DateTime)
  - **Índice em `(processed_at, occurred_at)`** para query de mensagens pendentes com ordenação

#### Repositórios (`Infrastructure/Persistence/Repositories/`)

- `ClienteAppRepository : IClienteAppRepository`
- `TenantRepository : ITenantRepository`
  - `GetNextNumeracaoAsync` usa `SELECT nextval('seq_nfe_{tenantIdHex}_{serie}')` onde `tenantIdHex = tenantId.ToString("N")`
- `DocumentoFiscalRepository : IDocumentoFiscalRepository`
  - `GetProcessandoAntigoAsync(TimeSpan timeout)` — retorna documentos em `Processando` há mais de `timeout`
- `UnitOfWork : IUnitOfWork` — wrapper sobre `ApplicationDbContext.SaveChangesAsync`

#### Migrations

- Migration inicial: `Infrastructure/Persistence/Migrations/20260511_InitialCreate.cs`
  - Cria todas as tabelas, índices e tabela `outbox_messages`
  - **Não** cria sequences de numeração (criadas no `CreateTenantCommandHandler`)

#### Infraestrutura Docker

- `infra/docker-compose.yml`
  - Serviço `postgres`: imagem `postgres:17`, volume persistente, porta 5432
  - Serviço `redis`: imagem `redis:7-alpine` (reservado para cache futuro), porta 6379
  - Variáveis de ambiente configuráveis via `.env`
- `infra/.env.example` — template de variáveis necessárias

#### Registro de DI

- `Infrastructure/DependencyInjection.cs` — método `AddInfrastructure(IServiceCollection, IConfiguration)` que registra `ApplicationDbContext` (com `DomainEventsInterceptor`), repositórios, `IUnitOfWork`

### Critério de Conclusão

- `dotnet ef migrations add InitialCreate` executa sem erros
- `dotnet ef database update` cria todas as tabelas no PostgreSQL local
- Tabela `outbox_messages` criada com índice composto em `(processed_at, occurred_at)`
- Índice em `(status, created_at)` em `documentos_fiscais` criado
- `OwnsOne(ConfiguracaoFiscal)` e `OwnsOne(Endereco)` mapeados corretamente
- `OwnsMany` para `_items` e `_pagamentos` com `HasField` explícito
- `DomainEventsInterceptor` registrado no `ApplicationDbContext`

---

## Fase 3 — Application Layer: ClienteApp e Tenant

**Esforço:** M

**Objetivo:** Implementar o CQRS para gestão de ClienteApps e Tenants com validação e logging. Dependências: Fases 1 e 2.

### Entregáveis

#### Behaviors (`Application/Common/Behaviors/`)

- `ValidationBehavior<TRequest, TResponse>` — executa todos os `IValidator<TRequest>` registrados; retorna `Result.Failure` com erros de validação se houver falhas
  - Registrado via `services.AddMediator(options => options.AddOpenBehavior(typeof(ValidationBehavior<,>)))` — API do Mediator.SourceGenerator 3.0 (não via `typeof(IPipelineBehavior<,>)` genérico)
- `LoggingBehavior<TRequest, TResponse>` — loga **apenas** falhas e warnings (`Result.IsFailure`); não loga happy path (responsabilidade do tracing OpenTelemetry)
  - Registrado via `services.AddMediator(options => options.AddOpenBehavior(typeof(LoggingBehavior<,>)))`

#### HttpContextCurrentUserContext (`Api/Authentication/`)

- `HttpContextCurrentUserContext : ICurrentUserContext`
  - Registrado como `Scoped` em `Program.cs`: `services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()`
  - Lê `ClienteAppId` dos claims do JWT via `IHttpContextAccessor.HttpContext.User.FindFirst("sub")`
  - Lê `TenantId` de `HttpContext.Items["TenantContext"]` populado pelo `TenantValidationMiddleware`
  - Handlers recebem `ICurrentUserContext` injetado por construtor — sem acesso a `IHttpContextAccessor` na Application layer

#### Commands (`Application/Tenants/Commands/`)

- `CreateClienteAppCommand` + `CreateClienteAppCommandHandler`
  - Recebe: `Name`, `ClientId`, `ClientSecret`, `WebhookUrl?`
  - Gera `ClientSecretHash` com PBKDF2 (SHA-256, mínimo 600.000 iterações, OWASP 2024+)
  - Gera `WebhookSecret` com `RandomNumberGenerator` (32 bytes, 64 chars hex)
  - Criptografa `WebhookSecret` via `ICertificateEncryptionService`
  - Persiste via `IClienteAppRepository`
  - Retorna: `ClienteAppCreatedResponse` — inclui `WebhookSecret` em texto claro **uma única vez**

- `RotateClienteAppSecretCommand` + `RotateClienteAppSecretCommandHandler`
  - Recebe: `ClienteAppId` (via `X-Admin-Key` no endpoint)
  - Gera novo `ClientSecret`, persiste hash; invalida anterior
  - Retorna: `{ clientId, newClientSecret }` — retornado uma única vez

- `CreateClienteAppCommandValidator` — valida: `Name` obrigatório, `ClientId` único no formato `slug`

- `CreateTenantCommand` + `CreateTenantCommandHandler`
  - Recebe: `ClienteAppId`, `Cnpj`, `RazaoSocial`, `RegimeTributario`, `UfCodigo`, `Serie`, `Endereco`
  - Valida CNPJ (normaliza máscara), unicidade por ClienteApp
  - Constrói `ConfiguracaoFiscal` value object
  - Registra Tenant via factory `Tenant.Criar(...)`
  - Executa DDL de criação de sequence: `await dbContext.Database.ExecuteSqlRawAsync($"CREATE SEQUENCE IF NOT EXISTS seq_nfe_{tenantId:N}_{serieSanitizada} START 1 INCREMENT 1")` — `serie` validada com regex `^[0-9]{1,3}$` antes da interpolação
  - Publica `TenantProvisionadoEvent`
  - Retorna: `TenantResponse`

- `CreateTenantCommandValidator` — valida todos os campos obrigatórios, CNPJ formato, `Serie` com regex `^[0-9]{1,3}$`

- `UpdateTenantCertificateCommand` + `UpdateTenantCertificateCommandHandler`
  - Recebe: `TenantId`, `ClienteAppId` (do JWT via `ICurrentUserContext`), `PfxBytes`, `Senha`, `Vencimento` (DateTime)
  - Valida que o Tenant pertence ao ClienteApp
  - Criptografa PFX e senha via `ICertificateEncryptionService`
  - Atualiza Tenant com `CertificadoVencimento`

- `UpdateTenantCscCommand` + `UpdateTenantCscCommandHandler`
  - Recebe: `TenantId`, `ClienteAppId`, `Csc`, `CIdToken`
  - Valida pertencimento; criptografa CSC; atualiza Tenant

#### Queries (`Application/Tenants/Queries/`)

- `GetTenantQuery` + `GetTenantQueryHandler` — Recebe `Cnpj`, `ClienteAppId`; retorna `TenantResponse`
- `ListTenantsQuery` + `ListTenantsQueryHandler` — Recebe `ClienteAppId`, `Page`, `PageSize`; retorna `PagedResult<TenantResponse>`
- `GetCertificadoStatusQuery` + `GetCertificadoStatusQueryHandler` — Recebe `TenantId`, `ClienteAppId`; retorna `{ VencimentoEm, DiasRestantes }`

#### ResultExtensions (`Application/Common/`)

- `ResultExtensions.ToHttpResult(this Result result)` — mapeia `Error.Code` para HTTP status code:
  - `*Errors.NaoEncontrado` → 404
  - `*Errors.NaoPertenceAoClienteApp` / `Inativo` → 403
  - `*Errors.IdempotencyKeyJaUsada` → 409
  - `*Errors.TransicaoInvalida` / validação → 422
  - Outros → 500

#### DTOs (`Application/Common/Models/`)

- `ClienteAppCreatedResponse` — `Id`, `Name`, `ClientId`, `WebhookUrl`, `WebhookSecret` (texto claro — apenas na criação), `IsActive`, `CreatedAt`
- `ClienteAppResponse` — `Id`, `Name`, `ClientId`, `WebhookUrl`, `IsActive`, `CreatedAt` (sem `WebhookSecret`)
- `TenantResponse` — `Id`, `Cnpj`, `RazaoSocial`, `RegimeTributario`, `Ambiente`, `UfCodigo`, `Serie`, `TemCertificado`, `CertificadoVencimento?`, `IsActive`
- `PagedResult<T>` — `Items`, `TotalCount`, `Page`, `PageSize`, `TotalPages`

### Critério de Conclusão

- Handlers compilam com Mediator.SourceGenerator gerando código de despacho
- `ValidationBehavior` e `LoggingBehavior` registrados via `AddMediator(options => options.AddOpenBehavior(...))`
- `CreateTenantCommandHandler` cria sequence PostgreSQL após persistir o Tenant
- `WebhookSecret` retornado uma única vez na criação; não exposto em queries subsequentes
- Testes unitários dos handlers passam usando NSubstitute para repositórios

---

## Fase 4 — Autenticação JWT

**Esforço:** P

**Objetivo:** Implementar o fluxo Client Credentials com JWT RS256 auto-emitido. Dependência: Fase 3.

### Entregáveis

#### JwtSettings (`Application/Common/Models/`)

- `JwtSettings` — `PrivateKeyPem` (string), `PublicKeyPems` (string[]) — lista para rotação
- Validado com `ValidateOnStart()` — falha no startup se ausente ou inválido

#### TokenService (`Infrastructure/Services/`)

- `TokenService : ITokenService`
  - Carrega chave privada RSA: `var rsa = RSA.Create(); rsa.ImportFromPem(settings.PrivateKeyPem);`
  - Gera JWT com claims: `sub` = `clienteAppId`, `client_id` = `clientId`, `iat`, `exp`
  - Assina com **RS256** usando `RsaSecurityKey` carregada
  - `exp` configurável, padrão 1 hora

#### JwtBearerConfiguration (`Api/Authentication/`)

- Configura `AddJwtBearer` com RS256
- `TokenValidationParameters.IssuerSigningKeys` = lista de `RsaSecurityKey` de **todas** as chaves em `JWT__PublicKeyPems__*` — suporta rotação zero-downtime
  - Carregamento: `foreach` sobre `settings.PublicKeyPems`, cria `RSA.Create()`, chama `ImportFromPem`, adiciona à lista

#### TenantValidationMiddleware (`Api/Middleware/`)

- Extrai `client_id` do JWT
- Lê `X-Tenant-Id` (**UUID do Tenant**, não CNPJ) do header
- Valida que o Tenant com aquele UUID pertence ao ClienteApp do JWT
- Armazena `TenantContext` no `HttpContext.Items["TenantContext"]`

- `TenantContext` — record com `TenantId`, `ClienteAppId`, `RegimeTributario`, `Ambiente`

#### Endpoint de token (`Api/Endpoints/`)

- `AuthEndpoints.Map(IEndpointRouteBuilder)`:
  - `POST /auth/token` — rate limiter "auth" (10 req/min por IP)
    - Valida credenciais via `IClienteAppRepository`
    - Verifica hash PBKDF2
    - Retorna: `{ access_token, token_type: "Bearer", expires_in }`
    - Erros: `401 Unauthorized` se credenciais inválidas

#### Rate Limiting

- `AddRateLimiter` com:
  - "auth": 10 req/min por IP para `/auth/token`
  - "api": 100 req/min por `client_id` — partição via `httpContext.User.FindFirst("client_id")?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString()`
  - "admin": 5 req/min por IP para `POST /api/v1/clientes`
  - Resposta 429 com header `Retry-After: 60`

### Critério de Conclusão

- `POST /auth/token` com credenciais válidas retorna JWT RS256
- JWT inválido resulta em `401` em qualquer endpoint protegido
- `X-Tenant-Id` de Tenant de outro ClienteApp resulta em `403 Forbidden`
- Startup falha se `JWT__PrivateKeyPems__0` não estiver configurado (`ValidateOnStart`)
- Rate limiting ativo no endpoint de token com `Retry-After` no 429
- `IssuerSigningKeys` aceita múltiplas chaves (lista)

---

## Fase 5 — Application Layer: Emissão de Documentos

**Esforço:** M

**Objetivo:** Implementar o pipeline CQRS de emissão de NFC-e, desde o recebimento do comando até o enfileiramento. Dependências: Fases 3 e 4.

### Entregáveis

#### Commands (`Application/Documents/Commands/IssueDocument/`)

- `IssueDocumentCommand`
  - Campos: `TenantId`, `ClienteAppId`, `IdempotencyKey`, `Tipo`, `Itens`, `Pagamentos`, `Consumidor?`, `IndPresenca?` (int; padrão 1)

- `IssueDocumentCommandHandler` — pipeline ordenado para consistência:
  1. Verifica idempotência — `409` se status final, `200` com documento existente se `Processando`
  2. Carrega Tenant e valida pertencimento via `ICurrentUserContext`
  3. Obtém próximo número via `ITenantRepository.GetNextNumeracaoAsync`
  4. Gera `cNF` com **`RandomNumberGenerator.GetBytes(4)`** convertido para 8 dígitos decimais
  5. Monta `ChaveAcesso.Gerar(...)` com Módulo 11
  6. Cria `DocumentoFiscal` com status `Criado`
  7. Persiste via `IDocumentoFiscalRepository`
  8. **Chama `documento.Enfileirar()` e persiste novamente** (status `Enfileirado`) na mesma transação via `IUnitOfWork.SaveChangesAsync`
  9. **Enfileira job via `IDocumentJobQueue.EnqueueProcessing(documentoId)`** — após commit, nunca antes
  10. Retorna `IssueDocumentResponse`
  - Propaga `CancellationToken` para todos os métodos async

  > **Nota de consistência:** Os passos 7 e 8 (persistir `Criado` + persistir `Enfileirado`) ocorrem na mesma transação antes de qualquer enqueue. Se o enqueue (passo 9) falhar, o documento fica em `Enfileirado` — o `ReconciliacaoJobProcessor` pode estender cobertura para reprocessar `Enfileirado` antigos.

- `IssueDocumentCommandValidator` (FluentValidation)
  - Pelo menos 1 item obrigatório
  - Valor total dos pagamentos deve igualar somatório dos itens (tolerância R$ 0,01)
  - CPF do consumidor obrigatório se valor total **>** R$ 10.000,00 (condição estrita `>`, não `>=`)
  - NCM com exatamente 8 dígitos
  - Quantidade e valor unitário > 0
  - `IndPresenca` — se informado, deve ser 1, 3, 4 ou 9; **valor 2 é explicitamente rejeitado**

#### Event Handlers (`Application/Documents/EventHandlers/`)

- `DocumentoFiscalAutorizadoEventHandler : INotificationHandler<DocumentoFiscalAutorizadoEvent>`
  - Chama `IWebhookDeliveryService.DeliverAsync(documentoId, clienteAppId, ct)`

- `DocumentoFiscalDenegadoEventHandler : INotificationHandler<DocumentoFiscalDenegadoEvent>`
  - Loga nível `Critical` com `TenantId`, `Cnpj` e `XMotivo`
  - Opcionalmente notifica canal operacional

#### Queries (`Application/Documents/Queries/`)

- `GetDocumentStatusQuery` + `GetDocumentStatusQueryHandler`
  - Valida que o documento pertence ao ClienteApp via Tenant
  - Retorna `DocumentoStatusResponse` — **sem `xmlAssinado`**

#### DTOs

- `IssueDocumentResponse` — `DocumentoId`, `Status`, `ChaveAcesso`, `PollUrl`, `CreatedAt`
- `DocumentoStatusResponse` — `DocumentoId`, `Status`, `ChaveAcesso?`, `QrCode?`, `Protocolo?`, `MotivoRejeicao?`, `AuthorizedAt?` (sem `XmlAssinado`)

### Critério de Conclusão

- Handler persiste `Enfileirado` antes de enfileirar job
- Segundo comando com mesmo `IdempotencyKey` retorna o mesmo `DocumentoId` sem criar novo documento
- Job é enfileirado via `IDocumentJobQueue` após commit
- Validator rejeita: sem itens, pagamentos não fecham total, CPF ausente acima R$ 10.000 (não R$ 10.000,01), `indPres=2`
- `DocumentoStatusResponse` não expõe `xmlAssinado`

---

## Fase 6a — Infraestrutura: Criptografia, Assinatura XML e Tributação

**Esforço:** M (pode ser executada em paralelo à Fase 2)

**Objetivo:** Implementar os componentes técnicos sem dependência de banco. Dependência: Fase 1 concluída.

### Entregáveis

#### CertificateEncryptionService (`Infrastructure/Fiscal/Certificates/`)

- `CertificateEncryptionService : ICertificateEncryptionService`
  - `Encrypt(byte[] data)` — AES-256-GCM; **nonce gerado com `RandomNumberGenerator.GetBytes(12)` a cada chamada**; produz `byte[]` com nonce (12b) + tag (16b) + ciphertext concatenados
  - `Decrypt(byte[] encrypted)` — separa nonce, tag e ciphertext; decriptografa
  - Sobrecargas `string Encrypt(string text)` e `string Decrypt(byte[] encrypted)` para CSC e webhookSecret
  - Chave AES lida de `CERT__EncryptionKey` (32 bytes, Base64)

#### XmlSigner (`Infrastructure/Fiscal/`)

- `XmlSigner`
  - `XmlDocument Assinar(XmlDocument xmlDoc, X509Certificate2 certificado)`
  - **Canonicalização: `XmlDsigC14NTransform` com URL `http://www.w3.org/TR/2001/REC-xml-c14n-20010315`** — não ExcC14N
  - Digest: SHA-1 (`SignedXml.XmlDsigSHA1Url`)
  - Signature: RSA-SHA1 (`SignedXml.XmlDsigRSASHA1Url`)
  - Reference URI aponta para o elemento `infNFe` com prefixo `#NFe{chaveAcesso}`

#### NfceXmlBuilder (`Infrastructure/Fiscal/XmlBuilder/`)

- `NfceXmlBuilder : INfceXmlBuilder`
  - `XmlDocument Construir(DocumentoFiscal documento, Tenant tenant)`
  - **Todos os elementos obrigatórios:**
    - `ide`: `mod=65`, `tpNF=1`, `idDest=1`, `tpImp=4`, `finNFe=1`, `indFinal=1`, `indPres={valor}`, `procEmi=3`, `verProc="VisuFiscalHub 1.0"`, `cIdToken={6 dígitos}`
    - `dhEmi`: serializado com offset do fuso da UF do Tenant via mapa estático completo das 27 UFs (ver decisions.md seção 5.1)
    - `emit`: CNPJ, razão social, endereço, CRT do Tenant
    - `det` (itens): tributação por CRT — `<ICMSSN400>` para CRT 1, `<ICMSSN900>` para CRT 2, CST para CRT 3
    - `<dest>`: omitido quando `Consumidor` é null
    - `total`: `vNF` + `vTotTrib` (via `IBPTService`)
    - `pag`: formas de pagamento
    - `infNFeSupl`: `<qrCode>` com URL completa + `<urlFe>`
  - Gera XML que passa validação XSD nfe_v4.00.xsd
  - Extensão para NT 2025.002: `AdicionarCamposReformaTributaria(XmlDocument, DocumentoFiscal)` — retorna XML inalterado até ativação

#### TributacaoCalculator (`Infrastructure/Fiscal/`)

- `TributacaoCalculator : ITributacaoCalculator`
  - `Tributo CalcularParaCrt1(Produto produto)` — CSOSN 400, PIS/COFINS CST 07 (sem valores numéricos de ICMS, `ValorIcms = 0`, `BaseCalculoIcms = 0`)
  - `Tributo CalcularParaCrt1Csosn500(Produto produto)` — CSOSN 500 (ICMS cobrado anteriormente por ST)
  - `Tributo CalcularParaCrt1Csosn102(Produto produto)` — CSOSN 102
  - `Tributo CalcularParaCrt2(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins)` — CSOSN 900 com base de cálculo e alíquota normais; PIS/COFINS com CST 01
  - `Tributo CalcularParaCrt3(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins)` — CST com base de cálculo

#### IBPTService (`Infrastructure/Fiscal/`)

- `IBPTService`
  - CSV como **embedded resource** no assembly `VisuFiscalHub.Infrastructure`
  - `decimal ObterAliquotaAproximada(string ncm, string uf)` — retorna 0,00 com `Log.Warning` se NCM inválido ou ausente (não lança exceção)
  - Loga versão e data de referência da tabela no startup
  - `IBPTHealthCheck` — emite alerta se tabela com mais de 7 meses

#### QrCodeGenerator (`Infrastructure/Fiscal/`)

- `QrCodeGenerator : IQrCodeGenerator`
  - `string Gerar(ChaveAcesso chaveAcesso, AmbienteSefaz ambiente, string csc, string cIdToken, string urlConsultaSefaz)`
  - Fórmula correta: `cHashQRCode = SHA1(chaveAcesso + "|2|" + (int)ambiente + "|" + csc)` onde `|2|` é a versão do QR Code
  - Monta URL: `{urlConsultaSefaz}?p={chaveAcesso}|2|{(int)ambiente}|{cHashQRCode}`
  - CSC nunca na URL final

#### UfFusoHorario (`Infrastructure/Fiscal/`)

- `UfFusoHorario` — classe estática com `static readonly Dictionary<int, TimeSpan> Mapa` para todas as 27 UFs brasileiras (offsets fixos conforme decisions.md seção 5.1)

### Critério de Conclusão

- `XmlSigner.Assinar(...)` produz XML com `<Transform Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315">`
- `NfceXmlBuilder.Construir(...)` gera XML com `procEmi=3`, `verProc`, `cIdToken`, sem `<dest>` quando consumidor é nulo, e **passa validação XSD** (`nfe_v4.00.xsd` incluso em `tests/Schemas/`)
- `TributacaoCalculator` cobre CRT 1 (400/500/102), CRT 2 (900 com base cálculo) e CRT 3
- `QrCodeGenerator` produz URL cujo hash é verificável com valor pré-computado (ver decisions.md seção 5.3)
- `UfFusoHorario.Mapa` contém todos os 27 códigos IBGE de UF

---

## Fase 6b — Infraestrutura: Certificados com Banco e OutboxRelayJob

**Esforço:** P

**Objetivo:** Componentes que dependem de banco e do outbox processador. Dependência: Fases 2 e 6a.

### Entregáveis

#### CertificateCache + TenantCertificateProvider (`Infrastructure/Fiscal/Certificates/`)

- `CertificateCache`
  - `IMemoryCache` com `SemaphoreSlim` por `TenantId`
  - `GetOrLoadAsync(TenantId)` — retorna `byte[]` decriptografado (não `X509Certificate2`)
  - Carrega com `X509CertificateLoader.LoadPkcs12(bytes, pwd, X509KeyStorageFlags.EphemeralKeySet)` — apenas `EphemeralKeySet`
  - TTL: 30 minutos (configurável)

- `TenantCertificateProvider : ITenantCertificateProvider`
  - `GetCertificateAsync(TenantId)` — usa cache ou carrega do banco

#### WebhookDeliveryService (`Infrastructure/Webhook/`)

- `WebhookDeliveryService : IWebhookDeliveryService`
  - `DeliverAsync(documentoId, clienteAppId, ct)`:
    1. Carrega `ClienteApp` e decriptografa `WebhookSecretCriptografado`
    2. Carrega `DocumentoFiscal` para montar payload
    3. Valida SSRF: rejeita URLs com hosts RFC 1918, loopback, link-local
    4. `HttpClientHandler.AllowAutoRedirect = false`
    5. Assina payload com HMAC-SHA256 usando webhookSecret decriptografado
    6. `POST webhookUrl` com header `X-Hub-Signature-256: sha256=<hex>`
    7. Registra `DeliveryAttempt`
    8. Enfileira retry via Hangfire: 3 tentativas (30s, 5min, 30min)
  - Registrado via `IHttpClientFactory`

#### OutboxRelayJob (`Infrastructure/Scheduling/`)

- `OutboxRelayJob`
  - Job periódico Hangfire: a cada 30 segundos
  - `SELECT id, event_type, payload FROM outbox_messages WHERE processed_at IS NULL ORDER BY occurred_at LIMIT 50 FOR UPDATE SKIP LOCKED`
  - Para cada mensagem: deserializa `Payload` JSON para o tipo em `EventType`; publica via `IMediator.Publish`; marca `processed_at = now()`
  - Idempotente: `SKIP LOCKED` previne processamento duplicado em múltiplas instâncias

### Critério de Conclusão

- `CertificateCache` usa apenas `EphemeralKeySet`
- `WebhookDeliveryService` bloqueia URLs RFC 1918 e não segue redirects
- `OutboxRelayJob` processa mensagens pendentes e marca `processed_at`

---

## Fase 7 — Integração SEFAZ

**Esforço:** G

**Objetivo:** Implementar o envio do XML ao SEFAZ, processamento do retorno e retry via Hangfire. Dependência: Fases 6a e 6b.

### Entregáveis

#### SefazEndpointResolver (`Infrastructure/Fiscal/Sefaz/`)

- `SefazEndpointResolver`
  - Enum `GrupoAutorizador` — `SVRS`, `SefazSP`, `SefazMG`, `SefazRS`, `SefazPR`, `SefazBA`, `SefazMT`, `SandboxAM`
  - Dicionário estático `ufCodigo → GrupoAutorizador` cobrindo todas as 27 UFs
  - Grupo SVRS: estados sem infraestrutura própria (SE, RJ, SC e outros ~13 estados)
  - URLs por `(GrupoAutorizador, TipoDocumento, AmbienteSefaz)`
  - `ResolverUrl(ufCodigo, tipoDocumento, ambiente)` — URL de autorização
  - `ResolverUrlConsultaQr(ufCodigo, ambiente)` — URL de consulta QR Code
  - `ResolverUrlEvento(ufCodigo, ambiente)` — URL para `NfeRecepcaoEvento4` (cancelamento — preparado para Fase 6B)

#### SefazHttpClient (`Infrastructure/Fiscal/Sefaz/`)

- `SefazHttpClient`
  - Registrado via `services.AddHttpClient<SefazHttpClient>()` com `ConfigurePrimaryHttpMessageHandler` — **nunca** cria `HttpClientHandler` por chamada
  - Recebe `X509Certificate2` do Tenant para mTLS
  - `EnviarSoapAsync(string url, string soapEnvelope, CancellationToken ct)` → `string`
  - Timeout via `CancellationTokenSource.CreateLinkedTokenSource(ct, CancellationTokenSource.CreateLinkedTokenSource(TimeSpan.FromSeconds(30)).Token)` — separa timeout do HttpClient do token de cancelamento do Hangfire

#### NfceAutorizacaoService (`Infrastructure/Fiscal/Sefaz/`)

- `NfceAutorizacaoService : ISefazClient`
  - `SubmeterAutorizacaoAsync(documento, tenant, ct)` — carrega certificado → assina XML → monta SOAP → chama SefazHttpClient → parseia retorno
  - `ConsultarNfeAsync(chaveAcesso, tenant, ct)` — consulta `nfeConsultaNFe`; retorna `SefazConsultaRetorno`
  - **Parsers testáveis unitariamente:** `ParseRetornoAutorizacao(string soapResponse)`, `ParseRetornoConsulta(string soapResponse)` — métodos internos que aceitam string raw (testáveis sem rede)
  - Monta envelope SOAP 1.2 com namespace correto para NFC-e 4.0
  - Parseia `cStat`, `xMotivo`, `nProt`, `XmlProtocolo` da resposta

- `SefazRetorno` — record: `bool Autorizado`, `string CStat`, `string XMotivo`, `string? NProt`, `string? XmlAutorizado`
- `SefazConsultaRetorno` — record: `bool Encontrado`, `bool Autorizado`, `string CStat`, `string? NProt`, `string? XmlProtocolo`

#### HangfireDocumentJobQueue (`Infrastructure/Scheduling/`)

- `HangfireDocumentJobQueue : IDocumentJobQueue`
  - Injeta `IBackgroundJobClient`
  - `EnqueueProcessing(documentoId)` — chama `BackgroundJob.Enqueue(() => processor.ProcessarDocumentoAsync(documentoId.Value, CancellationToken.None))`
  - Classe separada do `HangfireJobProcessor` — responsabilidades distintas (SRP)

#### HangfireJobProcessor (`Infrastructure/Scheduling/`)

- `HangfireJobProcessor`
  - `[AutomaticRetry(Attempts = 5, DelaysInSeconds = [30, 120, 600, 1800, 3600])]`
  - `ProcessarDocumentoAsync(Guid documentoId, CancellationToken ct)` — recebe **apenas o ID** (sem dados sensíveis)
    1. Carrega `DocumentoFiscal` e `Tenant` do banco
    2. Chama `IniciarProcessamento()` — `Enfileirado → Processando`
    3. Chama `NfceAutorizacaoService.SubmeterAutorizacaoAsync`
    4. Se `cStat=100`: chama `Autorizar(...)`, publica `DocumentoFiscalAutorizadoEvent`
    5. Se `cStat=110` (denegado): chama `Denegar(xMotivo)`, publica `DocumentoFiscalDenegadoEvent`; **não** lança exceção
    6. Se `cStat=204` (duplicidade): chama `Rejeitar("Duplicidade: nota já autorizada")`, **não** lança exceção
    7. Se rejeição definitiva (4xx cStat): chama `Rejeitar(xMotivo)`, **não** lança exceção
    8. Se timeout/ConnectionRefused: chama `ConsultarNfeAsync` — se confirmado como não processado, lança exceção para Hangfire retentar; se `cStat=100` descoberto na consulta, executa passo 4 com `TipoTentativa=Consulta`
    9. Registra `DeliveryAttempt` com `TipoTentativa` correto
    10. Persiste via `IUnitOfWork`

#### ReconciliacaoJobProcessor (`Infrastructure/Scheduling/`)

- `ReconciliacaoJobProcessor`
  - Job periódico Hangfire: a cada 5 minutos
  - Busca documentos com `status = Processando` há mais de 10 min via `GetProcessandoAntigoAsync`
  - Para cada documento:
    1. Chama `ConsultarNfeAsync` (via `ISefazClient`)
    2. Se `cStat=100`: chama `Autorizar(...)`, publica `DocumentoFiscalAutorizadoEvent`
    3. Se não encontrado/rejeição definitiva: chama `Falhar()`, publica `DocumentoFiscalFalhouEvent` (gap na numeração é aceito pelo SEFAZ)
    4. Persiste via `IUnitOfWork`
  - **Dependências:** `ISefazClient` concreto (Fase 7) + repositórios (Fase 2) — nunca executado em paralelo com Fase 2

#### DI Registration

- `AddSefazInfrastructure(IServiceCollection)` registra todos os serviços das Fases 6a, 6b e 7

### Critério de Conclusão

- `HangfireDocumentJobQueue` e `HangfireJobProcessor` são classes distintas
- `ISefazClient` expõe `SubmeterAutorizacaoAsync` **e** `ConsultarNfeAsync`
- `ReconciliacaoJobProcessor` usa `ConsultarNfeAsync` antes de transicionar para `Falhou`
- `cStat=110` transiciona para `Denegado` (não `Rejeitado`)
- `SefazEndpointResolver` cobre todos os grupos autorizadores (SVRS + SP + MG + outros)
- Emissão no sandbox AM retorna `cStat=100`

---

## Fase 8 — API Endpoints

**Esforço:** P

**Objetivo:** Expor todos os endpoints via ASP.NET Core Minimal APIs com documentação Scalar. Dependências: Fases 4 e 5.

### Entregáveis

#### Endpoints (`Api/Endpoints/`)

Todos os grupos de endpoints implementados como classes estáticas com método `Map(IEndpointRouteBuilder)`. Usar **`TypedResults`** em todos os endpoints. Erros retornam **`ProblemDetails`** via `GlobalExceptionHandler`.

---

**`POST /auth/token`**
- Autenticação: nenhuma (rate limiter "auth")
- Response 200: `{ "access_token": "JWT RS256", "token_type": "Bearer", "expires_in": 3600 }`
- Response 401: `{ "error": "invalid_client" }`
- Response 429: `Retry-After: 60`

---

**`POST /api/v1/clientes`** — criar ClienteApp
- Autenticação: `X-Admin-Key` — comparação time-safe via `CryptographicOperations.FixedTimeEquals`; rate limiter "admin" (5/min por IP)
- Response 201: `ClienteAppCreatedResponse` (inclui `webhookSecret` em texto claro — **única vez**)
- Response 409: ClientId já existe

---

**`POST /api/v1/clientes/{id}/rotate-secret`** — rotacionar client_secret
- Autenticação: `X-Admin-Key`
- Response 200: `{ clientId, newClientSecret }` — retornado uma única vez
- Response 404: ClienteApp não encontrado

---

**`POST /api/v1/clientes/{id}/rotate-webhook-secret`** — rotacionar webhookSecret
- Autenticação: `X-Admin-Key`
- Response 200: `{ clientId, newWebhookSecret }` — retornado uma única vez

---

**`POST /api/v1/tenants`** — criar Tenant
- Autenticação: Bearer JWT
- Response 201: `TenantResponse`
- Response 422: CNPJ inválido ou já cadastrado

---

**`PUT /api/v1/tenants/{id}/certificado`** — upload de certificado PFX
- Autenticação: Bearer JWT
- `[RequestSizeLimit(50 * 1024)]` — PFX máximo 50KB
- Request: `multipart/form-data` com `pfx` (file), `senha` (string), `vencimento` (date)
- Response 204: sem corpo

---

**`PUT /api/v1/tenants/{id}/csc`** — atualizar CSC
- Autenticação: Bearer JWT
- Response 204

---

**`GET /api/v1/tenants/{id}`** — obter Tenant
- Response 200: `TenantResponse`

---

**`GET /api/v1/tenants/{id}/certificado/status`** — status do certificado
- Response 200: `{ "vencimentoEm": "datetime", "diasRestantes": int }`

---

**`POST /api/v1/documentos/nfce`** — emitir NFC-e
- Autenticação: Bearer JWT (rate limiter "api")
- Headers: `X-Tenant-Id: {uuid}`, `X-Idempotency-Key: {uuid}`
- Response 202: `IssueDocumentResponse`
- Response 409: idempotency key já usada com status final
- Response 422: validação de negócio falhou

---

**`GET /api/v1/documentos/{id}/status`** — polling de status
- Response 200: `DocumentoStatusResponse` (sem `xmlAssinado`)

---

**`GET /api/v1/documentos/{id}/xml`** — obter XML completo
- Autenticação: Bearer JWT
- Response 200: `{ xmlAssinado }` — endpoint separado para acesso ao XML completo
- Response 404 / 403 adequados

---

**`POST /api/v1/documentos/{id}/cancelar`** — cancelar NFC-e
- Response 202: job de cancelamento enfileirado
- **Nota:** Implementado na Fase 6B

---

**`GET /health`**, **`GET /health/live`**, **`GET /health/ready`** — health checks

---

#### Program.cs (`Api/`)

- `public partial class Program { }` no final do arquivo — obrigatório para `WebApplicationFactory<Program>` nos testes de integração
- Configuração completa:
  - Serilog com middleware de enrichment para `TenantId` e `ClienteAppId` via `ICurrentUserContext`
  - JWT Bearer RS256 com `IssuerSigningKeys` (lista)
  - `AddRateLimiter` com políticas "auth", "api", "admin"
  - `AddProblemDetails()` + `AddExceptionHandler<GlobalExceptionHandler>()`
  - `IOptions<JwtSettings>` com `ValidateOnStart()`
  - `AddOpenTelemetry()` com `AddHangfireInstrumentation()`
  - `services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()`
  - Scalar UI, Health checks, `TenantValidationMiddleware`, Hangfire Dashboard

### Critério de Conclusão

- `Program.cs` termina com `public partial class Program { }`
- 429 retorna `Retry-After: 60`
- `POST /api/v1/clientes` usa `CryptographicOperations.FixedTimeEquals` para a chave admin
- Endpoints de rotação de secret/webhookSecret implementados
- `GET /api/v1/documentos/{id}/xml` separado do endpoint de status

---

## Fase 9 — Docker e Deploy

**Esforço:** P

**Objetivo:** Containerizar a aplicação para execução completa com `docker compose up`. Dependência: Fase 8.

### Entregáveis

#### Dockerfile (`/Dockerfile`)

```
Stage 1 — build: mcr.microsoft.com/dotnet/sdk:10.0
  - dotnet restore; dotnet publish -c Release -o /app/publish

Stage 2 — runtime: mcr.microsoft.com/dotnet/aspnet:10.0
  - Copiar ca-certificates da cadeia ICP-Brasil se necessário
  - EXPOSE 8080
  - ENTRYPOINT ["dotnet", "VisuFiscalHub.Api.dll"]
```

#### docker-compose.yml completo

Serviços `hub`, `postgres`, `redis`. Variáveis de ambiente do `hub`:
```
JWT__PrivateKeyPem / JWT__PublicKeyPems__0
CERT__EncryptionKey
AdminKey__Value
IBPT__TabelaVersao / IBPT__DataReferencia
```

#### infra/.env.example

```
POSTGRES_PASSWORD=dev_password_change_me
JWT_PRIVATE_KEY_PEM=-----BEGIN RSA PRIVATE KEY-----\n...\n-----END RSA PRIVATE KEY-----
JWT_PUBLIC_KEY_PEM=-----BEGIN PUBLIC KEY-----\n...\n-----END PUBLIC KEY-----
CERT_ENCRYPTION_KEY=base64_encoded_32_byte_key_here
ADMIN_KEY=change_me_generate_random_hex_key
IBPT_TABELA_VERSAO=2026-04
IBPT_DATA_REFERENCIA=2026-04-01
```

### Critério de Conclusão

- `docker compose up` sobe sem erros
- `GET /health` retorna `Healthy`
- Hangfire Dashboard acessível com autenticação

---

## Fase 10 — Testes

**Esforço:** G

**Objetivo:** Garantir cobertura suficiente para dar confiança no pipeline fiscal. Dependências: Fases 1 a 8.

### Estrutura dos Projetos de Teste

| Projeto | Framework | Objetivo |
|---|---|---|
| `VisuFiscalHub.UnitTests` | xUnit + Shouldly + NSubstitute | Testes unitários isolados com mocks |
| `VisuFiscalHub.IntegrationTests` | xUnit + WebApplicationFactory + Testcontainers.PostgreSql | Testes ponta a ponta com banco real |

**EF Core InMemory está proibido** para testes de repositório.

### Configuração dos Projetos

#### VisuFiscalHub.UnitTests.csproj — adicionar:
```xml
<PackageReference Include="coverlet.msbuild" Version="..." />
<PackageReference Include="xunit.skippable.fact" Version="..." />
```

#### VisuFiscalHub.IntegrationTests — CustomWebApplicationFactory:
```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureServices(services => {
        services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(_container.GetConnectionString()));
        services.RemoveAll<ISefazClient>();
        services.AddScoped<ISefazClient, FakeSefazClient>();
    });
}
// InitializeAsync: await dbContext.Database.MigrateAsync()
// Seed: criar ClienteApp + Tenant via ApplicationDbContext diretamente
```

#### .runsettings (enforça metas de cobertura):
```xml
<Threshold>
  <Line Assembly="VisuFiscalHub.Domain">95</Line>
  <Line Assembly="VisuFiscalHub.Application">90</Line>
  <Line Assembly="VisuFiscalHub.Infrastructure">80</Line>
</Threshold>
```

#### tests/Schemas/
- `nfe_v4.00.xsd` e dependências — schema oficial para validação de XML nos testes

### Entregáveis

#### Testes de Domínio (`VisuFiscalHub.UnitTests/Domain/`)

- `ChaveAcessoTests`
  - `Gerar_DeveProduirStringDe44Digitos()`
  - `Gerar_cDV_DeveCalcularModulo11Corretamente()` — `[Theory, InlineData]` com valores concretos:
    - Resto 0 → cDV = 0 (construir 43 dígitos que somem para resto 0)
    - Resto 1 → cDV = 0
    - Resto 2 → cDV = 9
    - Resto 9 → cDV = 2
    - **Chave real MOC 7.0:** 43 dígitos `3509061420016714006512500100000180010000009` → cDV esperado `7`
  - `Gerar_QuandoCnpjInvalido_DeveRetornarErro()`

- `CnpjTests`
  - `Criar_QuandoCnpjValido_DeveRetornarSucesso()`
  - `Criar_QuandoCnpjComMascara_DeveNormalizarENormalizar()` — `"11.222.333/0001-81"` e `"11222333000181"` produzem o mesmo `Cnpj`
  - `Criar_QuandoDigitosVerificadoresErrados_DeveRetornarErro()`
  - `Criar_QuandoApenasUmDigitoRepetido_DeveRetornarErro()`

- `CpfTests` — análogos ao `CnpjTests`

- `DocumentoFiscalTests`
  - Transições válidas (todas as listadas na state machine)
  - Transições inválidas: `Autorizar_QuandoJaAutorizado_DeveRetornarErro()`, `Rejeitar_QuandoAutorizado_DeveRetornarErro()`, `Cancelar_QuandoCriado_DeveRetornarErro()`, etc.
  - `Autorizar_DevePublicarDocumentoFiscalAutorizadoEvent()`
  - `Rejeitar_DeveGravarMotivo()`
  - **Testes de cancelamento com TimeProvider:**
    - `Cancelar_QuandoDentro30Minutos_DeveTransicionarParaCancelado()` — `TimeProvider.Fixed(authorizedAt.AddMinutes(29))`
    - `Cancelar_QuandoFora30Minutos_DeveRetornarErro()` — `TimeProvider.Fixed(authorizedAt.AddMinutes(31))`
    - `Cancelar_QuandoExatamente30Minutos_DeveRetornarErro()` — boundary: `authorizedAt.AddMinutes(30)` **não pode** cancelar (condição estrita `<`)
  - `Falhar_QuandoProcessando_DeveTransicionarParaFalhou()`
  - `Denegar_QuandoProcessando_DeveTransicionarParaDenegado()`

- `QrCodeTests`
  - `Gerar_NaoDeveConterCscNaUrl()`
  - `Gerar_DeveProduirUrlComHashCorreto()` — `[InlineData]` com hash SHA-1 **pré-computado externamente** (calcular com `echo -n "35090614200167140065125001000001800100000097|2|2|0123456789" | sha1sum` antes de escrever o código; hardcodar o hex resultante no `[InlineData]`)

#### Testes de Aplicação (`VisuFiscalHub.UnitTests/Application/`)

- `IssueDocumentCommandHandlerTests`
  - `Handle_QuandoComandoValido_DeveEnfileirarJob()`
  - `Handle_QuandoIdempotencyKeyJaUsadaStatusFinal_DeveRetornar409()`
  - `Handle_IdempotencyKey_QuandoStatusProcessando_DeveRetornar200ComDocumentoExistente()`
  - `Handle_QuandoTenantNaoPertenceAoClienteApp_DeveRetornarErro()`

- `IssueDocumentCommandValidatorTests`
  - `Validar_QuandoValorAcimaDe10000SemCpf_DeveRejeitar()` — R$ 10.000,01
  - `Validar_QuandoValorAcimaDe10000ComCpf_DevePermitir()`
  - `Validar_QuandoValorExatamente10000SemCpf_DevePermitir()` — boundary: R$ 10.000,00 com `>`, não `>=`
  - `Validar_QuandoPagamentosNaoFechamTotal_DeveRejeitar()`
  - `Validar_QuandoNcmComMenosDe8Digitos_DeveRejeitar()`
  - `Validar_QuandoIndPresencaProibido_DeveRejeitar()` — `indPres=2`

- `CreateTenantCommandHandlerTests`
  - `Handle_QuandoCnpjValido_DeveCriarTenantECriarSequence()`
  - `Handle_QuandoCnpjDuplicado_DeveRetornarErro()`

- `ValidationBehaviorTests`
  - `Pipeline_QuandoCommandoInvalido_DeveRejeitarAntesDoHandler()`

#### Testes de Infraestrutura (`VisuFiscalHub.UnitTests/Infrastructure/`)

- `XmlSignerTests`
  - `Assinar_DeveProduirXmlComBloco_Signature()`
  - `Assinar_XmlAssinado_DevePassarEmVerificacaoManual()` — via `SignedXml.CheckSignature`
  - `Assinar_DeveConterTransformC14NComUrlCorreta()`
  - `Assinar_DeveConterReferenceURIComPrefixoNFe()`

- `NfceXmlBuilderTests`
  - `Construir_ParaCrt1_DeveUsarCsosn400()`
  - `Construir_ParaCrt2_DeveUsarCsosn900ComBaseCalculo()` — verifica `<ICMSSN900>` e campos numéricos preenchidos
  - `Construir_ParaCrt3_DeveUsarCstIcms()`
  - `Construir_TotalVNF_DeveCorresponderSomatorioItens()`
  - `Construir_QrCodeNaoDeveConterCsc()`
  - `Construir_DeveConterProcEmiIgual3()`
  - `Construir_DeveConterVerProc()`
  - `Construir_DeveConterCIdToken()`
  - `Construir_QuandoConsumidorNulo_NaoDeveConterTagDest()`
  - **`Construir_XmlGerado_DevePassarValidacaoXSD_NfCe40()`** — carrega `nfe_v4.00.xsd` de `tests/Schemas/`; qualquer `ValidationEventHandler` com `XmlSeverityType.Error` causa falha

- `CertificateEncryptionServiceTests`
  - `Encrypt_Decrypt_RoundTrip_DeveRetornarBytesOriginais()`
  - `Encrypt_DeveChamarRandomNumberGeneratorParaNonce()` — verifica nonces distintos em chamadas consecutivas

- `TributacaoCalculatorTests` — **com valores numéricos concretos:**
  - `CalcularParaCrt1_DeveProduzirCsosn400_ValorIcmsZero()` — `ValorIcms = 0`, `BaseCalculoIcms = 0`
  - `CalcularParaCrt1_DeveProduzirCsosn500()`
  - `CalcularParaCrt1_DeveProduzirCsosn102()`
  - `CalcularParaCrt2_DeveProduirCsosn900ComAliquota12Porcento()` — produto R$ 100,00, alíquota 12%, `ValorIcms = 12,00`, `BaseCalculoIcms = 100,00`
  - `CalcularParaCrt3_DeveProduzirCstIcmsComBaseCalculo()` — produto R$ 100,00, alíquota 12%, `ValorIcms = 12,00`

- `ReconciliacaoJobProcessorTests`
  - `Processar_QuandoDocumentoTravadoEmProcessando_DeveConsultarSefaz()` — verifica que `ISefazClient.ConsultarNfeAsync` é chamado
  - `Processar_QuandoSefazConfirmaAutorizado_DeveTransicionarParaAutorizado()`
  - `Processar_QuandoSefazNaoEncontra_DeveTransicionarParaFalhou()`
  - `Processar_QuandoNenhumDocumentoTravado_NaoDeveChamarSefaz()`

- `NfceAutorizacaoServiceParserTests` — **unitários puros com strings de resposta SOAP hardcoded:**
  - `ParsearRetorno_cStat100_DeveRetornarAutorizado()`
  - `ParsearRetorno_cStat204_DeveRetornarDefinitivoNaoAutorizado()` — duplicidade; não retentar
  - `ParsearRetorno_cStat110_DeveRetornarDenegado()`
  - `ParsearRetorno_cStat4xx_DeveRetornarRejeicaoDefinitiva()`
  - `ParsearRetorno_cStat3xx_DeveRetornarErroTemporario()`
  - `ParsearRetorno_XmlMalformado_DeveRetornarErroSemExcecao()`

- `IBPTServiceTests`
  - `ObterAliquota_QuandoNcmValido_DeveRetornarAliquota()`
  - `ObterAliquota_QuandoNcmInvalido_DeveRetornarZeroSemExcecao()`
  - `ObterAliquota_QuandoNcmAusenteNaTabela_DeveRetornarZeroSemExcecao()`

- `OutboxRelayJobTests`
  - `Processar_QuandoMensagemPendente_DevePublicarEventoEMarcarProcessado()`
  - `Processar_QuandoNenhumaMensagem_NaoDevePublicarNada()`

- `WebhookDeliveryServiceTests`
  - `Deliver_DeveAssinarPayloadComHmacSha256()`
  - `Deliver_DeveBloquearUrlRfc1918()` — `10.0.0.1`, `192.168.1.1`, `172.16.0.1`
  - `Deliver_NaoDeveBloquearUrlPublica()`
  - `Deliver_DeveCalcularAssinaturaCorretamente()` — verifica `X-Hub-Signature-256` com valor esperado computado externamente

#### Testes de Integração (`VisuFiscalHub.IntegrationTests/`)

- `AuthFlowTests`
  - `PostToken_ComCredenciaisValidas_DeveRetornarJwtRS256()`
  - `PostToken_ComCredenciaisInvalidas_DeveRetornar401()`
  - `PostToken_AcimaDoRateLimit_DeveRetornar429ComRetryAfter()`

- `EmissaoNfceTests`
  - `PostNfce_FluxoCompleto_DeveRetornar202EEnfileirarJob()` — `ISefazClient` substituído por `FakeSefazClient`
  - `PostNfce_TenantDeOutroClienteApp_DeveRetornar403()`

- `IdempotenciaTests`
  - `PostNfce_MesmaIdempotencyKeyStatusAutorizado_DeveRetornar409()`
  - `PostNfce_MesmaIdempotencyKeyStatusProcessando_DeveRetornar200ComDocumentoExistente()`

- `TenantIsolamentoTests`
  - `GetStatus_DocumentoDeOutroTenant_DeveRetornar403()`

- `HealthCheckTests`
  - `GetHealth_QuandoBancoDisponivel_DeveRetornarHealthy()`

#### Smoke Tests (`VisuFiscalHub.IntegrationTests/Smoke/`)

```csharp
[SkippableFact]
public async Task EmitirNfce_SandboxAmazonas_DeveRetornarCstat100()
{
    Skip.If(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SEFAZ_SANDBOX_CERT")));
    // CSC = "0123456789", cIdToken = "000001"
}
```

### Metas de Cobertura

| Camada | Meta |
|---|---|
| Domain | 95% |
| Application | 90% |
| Infrastructure/Fiscal (XmlBuilder, XmlSigner, TributacaoCalculator) | 85% |
| Infrastructure/Sefaz (incluindo parsers de cStat) | **80%** |
| Api (coberto pelos integration tests) | 80% |

### Critério de Conclusão

- `dotnet test --project VisuFiscalHub.UnitTests` passa 100%
- `dotnet test --project VisuFiscalHub.IntegrationTests --filter "Category!=Smoke"` passa 100%
- Metas de cobertura enforçadas via `.runsettings` — build falha se não atingidas
- `UnitTest1.cs` placeholder removido
- Schema XSD `nfe_v4.00.xsd` presente em `tests/Schemas/`

---

## Ordem de Execução

```
┌─────────────────────────────────────────────────────────────────────┐
│                    SEQUENCIAMENTO DE FASES v3.0                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│   [Fase 1]  Domain Layer          ◄── ponto de partida             │
│       │                                                             │
│       ├──────────────────────────────────┐                          │
│       │                                  │                          │
│   [Fase 2]  DB + EF Core            [Fase 6a] Criptografia,        │
│       │     (DomainEventsInterceptor)     │       XML, Tributação   │
│       │                                  │       (sem banco)        │
│   [Fase 3]  App: ClienteApp              │                          │
│             e Tenant                     │                          │
│       │       (cria sequences)           │                          │
│   [Fase 4]  JWT Auth                     │                          │
│       │                                  │                          │
│   [Fase 5]  App: Emissão           [Fase 6b] Certificados +        │
│       │       + Event Handlers      │       OutboxRelayJob         │
│       │       (depende Fase 2 e 6a)  │       (depende Fase 2)      │
│       └──────────────┬───────────────┘                              │
│                      │                                              │
│                  [Fase 7]  Integração SEFAZ                         │
│                      │     (ReconciliacaoJobProcessor               │
│                      │      depende Fase 7 concluída)               │
│                  [Fase 8]  API Endpoints                            │
│                      │     (public partial class Program)           │
│                  [Fase 9]  Docker e Deploy                          │
│                      │                                              │
│                  [Fase 10] Testes (paralelo contínuo)               │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

Legenda de esforço:
  P = Pequeno  (1–2 dias)
  M = Médio    (3–5 dias)
  G = Grande   (5–8 dias)

Resumo por fase:
  Fase 1   — Domain Layer                  P
  Fase 2   — Banco de Dados                M
  Fase 3   — App: ClienteApp / Tenant      M
  Fase 4   — Autenticação JWT              P
  Fase 5   — App: Emissão                  M
  Fase 6a  — Criptografia e XML            M
  Fase 6b  — Certificados + Outbox         P
  Fase 7   — Integração SEFAZ              G
  Fase 8   — API Endpoints                 P
  Fase 9   — Docker e Deploy               P
  Fase 10  — Testes                        G
```

---

## Notas Transversais

### Convenções de Nomenclatura

- Tabelas PostgreSQL: `snake_case` (ex: `documentos_fiscais`, `cliente_apps`)
- Classes C#: `PascalCase`
- Rotas de API: `kebab-case` (ex: `/api/v1/documentos/nfce`)
- Variáveis de ambiente: `SECAO__Chave` (dois underscores para hierarquia)
- Nomes de sequence PostgreSQL: `seq_nfe_{tenantId:N}_{serie}` — UUID sem hífens (`ToString("N")`)

### Convenções de Implementação

- Todos os handlers recebem `CancellationToken` e o propagam para **todos** os métodos async
- `TimeProvider` injetado em todos os handlers e serviços que usam data/hora — nunca `DateTime.UtcNow` direto
- `TypedResults` (não `Results`) em todos os endpoints Minimal API
- `ProblemDetails` para todos os erros de API; mapeamento via `ResultExtensions.ToHttpResult`
- `IOptions<T>` com `ValidateOnStart()` para todas as configurações críticas
- Jobs Hangfire: **apenas IDs** como argumentos — dados sensíveis carregados do banco na execução

### Segurança — Itens Críticos

- Chave AES para certificados (`CERT__EncryptionKey`) **nunca** entra no banco ou em código-fonte
- JWT assinado com RS256 — chave privada exclusiva do hub em variável de ambiente
- `JWT__PublicKeyPems__*` suporta lista de chaves para rotação zero-downtime
- `X-Tenant-Id` sempre contém UUID interno do Tenant — nunca CNPJ
- CSC e `webhookSecret` criptografados com AES-256-GCM — nunca em texto claro no banco
- `ClientSecretHash` usa PBKDF2 com SHA-256 e mínimo 600.000 iterações
- `cNF` da chave de acesso gerado com `RandomNumberGenerator` — nunca `Random.Shared`
- Toda query de dados de negócio filtra por `ClienteAppId` extraído do JWT
- CPF do consumidor armazenado apenas no XML assinado — não em coluna separada em texto claro
- `WebhookDeliveryService`: `AllowAutoRedirect = false`; valida SSRF em cada request
- `X-Admin-Key`: comparação via `CryptographicOperations.FixedTimeEquals`
- `SefazHttpClient`: via `IHttpClientFactory` — sem criação manual de `HttpClientHandler`
- `X509KeyStorageFlags.EphemeralKeySet` apenas — sem combinação com `MachineKeySet`

### Extensibilidade para Reforma Tributária (NT 2025.002)

- Campos IBS, CBS e IS adicionados ao `NfceXmlBuilder` como extensão — sem alteração no domínio
- `TipoIcms` enum já prevê expansão
- Monitorar publicação de schemas SEFAZ atualizados

### Itens Fora de Escopo desta Implementação

Conforme `docs/decisions.md` seção 7: cancelamento (Fase 6B), contingência offline, inutilização de numeração, CC-e, DANFE em PDF, NFS-e, NF-e, certificado A3, RLS PostgreSQL, anonimização LGPD pós-5-anos.

---

*Mantido pelo time VisuFiscalHub. Atualizar ao concluir cada fase e ao incorporar decisões que modifiquem escopo ou sequência.*
