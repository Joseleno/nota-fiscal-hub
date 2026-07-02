# Fase 2 — Infraestrutura: Banco de Dados

**Versão:** 1.0
**Data:** 2026-05-12
**Dependência obrigatória:** Fase 1 (Domain Layer) concluída e compilando sem erros.

---

## 1. Visão Geral da Fase

### Objetivo

Mapear o modelo de domínio para PostgreSQL via EF Core 10, implementar os repositórios concretos que satisfazem as interfaces do Domain layer e disponibilizar a infraestrutura Docker para desenvolvimento local.

### Dependências

- **Fase 1 concluída:** todos os tipos do Domain compilam sem erros (entidades, value objects, identificadores, enums, interfaces de repositório).
- **Packages já configurados no `VisuFiscalHub.Infrastructure.csproj`:**
  - `Microsoft.EntityFrameworkCore` 10.0.x
  - `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.x
  - `Microsoft.EntityFrameworkCore.Design` 10.0.x
  - `Hangfire.PostgreSql` 1.21.x

### Critério de Conclusão

- `dotnet ef migrations add InitialCreate` executa sem erros a partir do projeto `VisuFiscalHub.Infrastructure`.
- `dotnet ef database update` cria todas as tabelas no PostgreSQL local.
- Tabela `outbox_messages` criada com índice composto em `(processed_at, occurred_at)`.
- Índice em `(status, created_at)` em `documentos_fiscais` criado.
- `OwnsOne(ConfiguracaoFiscal)` e `OwnsOne(Endereco)` mapeados corretamente com prefixos de coluna.
- `OwnsMany` para `_items` e `_pagamentos` com `HasField` explícito.
- `DomainEventsInterceptor` registrado no `ApplicationDbContext` via `AddInterceptors`.
- `docker compose up` sobe os serviços `postgres` e `redis` sem erros.

---

## 2. Árvore de Arquivos

```
src/VisuFiscalHub.Infrastructure/
│
├── Persistence/
│   ├── DomainEventsInterceptor.cs
│   ├── ApplicationDbContext.cs
│   ├── UnitOfWork.cs
│   │
│   ├── Configurations/
│   │   ├── ClienteAppConfiguration.cs
│   │   ├── TenantConfiguration.cs
│   │   ├── DocumentoFiscalConfiguration.cs
│   │   ├── DeliveryAttemptConfiguration.cs
│   │   └── OutboxMessageConfiguration.cs
│   │
│   ├── Repositories/
│   │   ├── ClienteAppRepository.cs
│   │   ├── TenantRepository.cs
│   │   └── DocumentoFiscalRepository.cs
│   │
│   └── Migrations/
│       └── (gerado pelo EF Core — não criar manualmente)
│
└── DependencyInjection.cs

infra/
├── docker-compose.yml
└── .env.example
```

**Total de arquivos `.cs` a criar manualmente: 10**
(As migrations são geradas via `dotnet ef migrations add`.)

---

## 3. Especificação por Arquivo

---

### 3.1 `Infrastructure/Persistence/DomainEventsInterceptor.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using VisuFiscalHub.Domain.Common;   // IDomainEventSource, IDomainEvent
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class DomainEventsInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default);
}
```

**Notas de implementação críticas:**

1. **Herança:** `SaveChangesInterceptor` (não `IDbCommandInterceptor`). Sobrescrever `SavingChangesAsync`, não `SavedChangesAsync` — deve interceptar **antes** do commit, dentro da mesma transação.

2. **Coleta de eventos:** Iterar `eventData.Context!.ChangeTracker.Entries<IDomainEventSource>()`. Para cada entry, acessar `entry.Entity.DomainEvents` e coletar todos os `IDomainEvent`.

3. **Serialização:** Usar `System.Text.Json.JsonSerializer.Serialize(domainEvent, domainEvent.GetType())`. O `EventType` deve ser o nome qualificado completo: `domainEvent.GetType().AssemblyQualifiedName!`.

4. **Criação de OutboxMessage:** Para cada evento coletado, criar `new OutboxMessage { Id = Guid.NewGuid(), EventType = ..., Payload = ..., OccurredAt = DateTime.UtcNow }` e adicionar via `eventData.Context.Set<OutboxMessage>().Add(outboxMessage)`.

5. **Limpeza:** Chamar `entry.Entity.ClearDomainEvents()` **após** a coleta de todos os eventos da entidade, antes de persistir.

6. **Transação atômica:** A inserção de `OutboxMessage` ocorre dentro da mesma chamada `SaveChangesAsync`. NÃO chamar `SaveChangesAsync` novamente dentro do interceptor — isso causaria recursão infinita. Os `OutboxMessage` adicionados via `context.Set<OutboxMessage>().Add(...)` serão incluídos automaticamente no `INSERT` corrente.

7. **Retorno:** Chamar `await base.SavingChangesAsync(eventData, result, cancellationToken)` e retornar o resultado.

**Exemplo de estrutura do corpo:**
```csharp
// 1. Coletar eventos de todas as entidades rastreadas
var entities = eventData.Context!.ChangeTracker
    .Entries<IDomainEventSource>()
    .Select(e => e.Entity)
    .Where(e => e.DomainEvents.Any())
    .ToList();

var outboxMessages = entities
    .SelectMany(e => e.DomainEvents)
    .Select(domainEvent => new OutboxMessage
    {
        Id = Guid.NewGuid(),
        EventType = domainEvent.GetType().AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredAt = DateTime.UtcNow
    })
    .ToList();

// 2. Limpar eventos das entidades
entities.ForEach(e => e.ClearDomainEvents());

// 3. Inserir OutboxMessages na mesma transação (sem Save adicional)
if (outboxMessages.Any())
    eventData.Context.Set<OutboxMessage>().AddRange(outboxMessages);

return await base.SavingChangesAsync(eventData, result, cancellationToken);
```

---

### 3.2 `Infrastructure/Persistence/ApplicationDbContext.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Common;      // OutboxMessage
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Persistence.Configurations;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options);

    public DbSet<ClienteApp> ClienteApps { get; set; }
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<DocumentoFiscal> DocumentosFiscais { get; set; }
    public DbSet<DeliveryAttempt> DeliveryAttempts { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder);
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder);
}
```

**Notas de implementação críticas:**

1. **`OnModelCreating`:** Aplicar todas as configurações via `modelBuilder.ApplyConfiguration(new ClienteAppConfiguration())` etc. Aplicar também `modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly)` é alternativa válida.

2. **`ConfigureConventions` — conversores de value types (obrigatórios):**
   Todos os strongly-typed IDs e value objects devem ter conversores registrados. Isso evita erros de "Cannot translate" em queries.

   ```csharp
   protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
   {
       // Strongly-typed IDs
       configurationBuilder.Properties<ClienteAppId>()
           .HaveConversion<ClienteAppId, Guid>();
       configurationBuilder.Properties<TenantId>()
           .HaveConversion<TenantId, Guid>();
       configurationBuilder.Properties<DocumentoFiscalId>()
           .HaveConversion<DocumentoFiscalId, Guid>();
       configurationBuilder.Properties<DeliveryAttemptId>()
           .HaveConversion<DeliveryAttemptId, Guid>();

       // Value objects armazenados como string
       configurationBuilder.Properties<Cnpj>()
           .HaveConversion<CnpjValueConverter>();
       configurationBuilder.Properties<ChaveAcesso>()
           .HaveConversion<ChaveAcessoValueConverter>();
   }
   ```

   Os conversores `CnpjValueConverter` e `ChaveAcessoValueConverter` devem ser classes internas ou no mesmo namespace, herdando de `ValueConverter<TModel, TProvider>`.

3. **`OutboxMessage` como entidade de infraestrutura:** `OutboxMessage` é uma classe de infraestrutura, não do domínio. Deve ser definida em `VisuFiscalHub.Infrastructure.Persistence` (ou `VisuFiscalHub.Domain.Common` se preferido para acesso do interceptor via `IDomainEventSource`). A decisão adotada nesta spec é: `OutboxMessage` em `VisuFiscalHub.Domain.Common` para que o `DomainEventsInterceptor` consiga referenciar sem depender de Infrastructure em Domain. O Domain pode ter essa entidade de infraestrutura como DTO de dados, sem comportamento de domínio.

   **Alternativa preferida:** `OutboxMessage` como classe simples em `VisuFiscalHub.Infrastructure.Persistence` (POCO). O `DomainEventsInterceptor` usa `eventData.Context.Set<OutboxMessage>()` que requer que a classe esteja no modelo do DbContext — não há dependência cruzada.

4. **Sem override de `SaveChangesAsync`:** O interceptor já cobre o caso. Não duplicar lógica.

---

### 3.3 `Infrastructure/Persistence/UnitOfWork.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence`

**Usings necessários:**
```csharp
using VisuFiscalHub.Domain.Interfaces;  // IUnitOfWork
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    public UnitOfWork(ApplicationDbContext context);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

**Notas de implementação críticas:**

1. Delega diretamente para `_context.SaveChangesAsync(cancellationToken)`.
2. O `DomainEventsInterceptor` é executado automaticamente por ser registrado no `DbContextOptions` — não requer lógica explícita no `UnitOfWork`.
3. Não expõe repositórios diretamente — cada repositório é injetado separadamente nos handlers.

---

### 3.4 `Infrastructure/Persistence/Configurations/ClienteAppConfiguration.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Configurations`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class ClienteAppConfiguration : IEntityTypeConfiguration<ClienteApp>
{
    public void Configure(EntityTypeBuilder<ClienteApp> builder);
}
```

**Notas de implementação críticas:**

```csharp
builder.ToTable("cliente_apps");

builder.HasKey(c => c.Id);
builder.Property(c => c.Id)
    .HasColumnName("id")
    .HasConversion(id => id.Value, value => new ClienteAppId(value));

builder.Property(c => c.Name)
    .HasColumnName("name")
    .HasMaxLength(100)
    .IsRequired();

builder.Property(c => c.ClientId)
    .HasColumnName("client_id")
    .HasMaxLength(100)
    .IsRequired();

builder.HasIndex(c => c.ClientId)
    .IsUnique()
    .HasDatabaseName("ix_cliente_apps_client_id");

builder.Property(c => c.ClientSecretHash)
    .HasColumnName("client_secret_hash")
    .HasMaxLength(500)
    .IsRequired();

builder.Property(c => c.WebhookUrl)
    .HasColumnName("webhook_url")
    .HasMaxLength(500);

// bytea nullable para o webhook secret criptografado
builder.Property(c => c.WebhookSecretCriptografado)
    .HasColumnName("webhook_secret_criptografado")
    .HasColumnType("bytea");

builder.Property(c => c.IsActive)
    .HasColumnName("is_active")
    .IsRequired();

builder.Property(c => c.CreatedAt)
    .HasColumnName("created_at")
    .IsRequired();
```

---

### 3.5 `Infrastructure/Persistence/Configurations/TenantConfiguration.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Configurations`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Domain.Enums;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder);
}
```

**Notas de implementação críticas:**

```csharp
builder.ToTable("tenants");

builder.HasKey(t => t.Id);
builder.Property(t => t.Id)
    .HasColumnName("id")
    .HasConversion(id => id.Value, value => new TenantId(value));

builder.Property(t => t.ClienteAppId)
    .HasColumnName("cliente_app_id")
    .IsRequired();

// FK para ClienteApp
builder.HasOne<ClienteApp>()
    .WithMany()
    .HasForeignKey(t => t.ClienteAppId)
    .HasConstraintName("fk_tenants_cliente_app_id")
    .OnDelete(DeleteBehavior.Restrict);

// CNPJ: conversor de Cnpj -> string
builder.Property(t => t.Cnpj)
    .HasColumnName("cnpj")
    .HasMaxLength(14)
    .IsRequired()
    .HasConversion(cnpj => cnpj.Valor, valor => Cnpj.Criar(valor).Value);

// Índice único: (cnpj, cliente_app_id) — um CNPJ por ClienteApp
builder.HasIndex(t => new { t.Cnpj, t.ClienteAppId })
    .IsUnique()
    .HasDatabaseName("ix_tenants_cnpj_cliente_app_id");

builder.Property(t => t.RazaoSocial)
    .HasColumnName("razao_social")
    .HasMaxLength(300)
    .IsRequired();

builder.Property(t => t.NomeFantasia)
    .HasColumnName("nome_fantasia")
    .HasMaxLength(300);

// OwnsOne: Endereco — colunas prefixadas end_
builder.OwnsOne(t => t.Endereco, end =>
{
    end.Property(e => e.Logradouro).HasColumnName("end_logradouro").HasMaxLength(200).IsRequired();
    end.Property(e => e.Numero).HasColumnName("end_numero").HasMaxLength(10).IsRequired();
    end.Property(e => e.Complemento).HasColumnName("end_complemento").HasMaxLength(100);
    end.Property(e => e.Bairro).HasColumnName("end_bairro").HasMaxLength(100).IsRequired();
    end.Property(e => e.Municipio).HasColumnName("end_municipio").HasMaxLength(100).IsRequired();
    end.Property(e => e.CodigoMunicipio).HasColumnName("end_codigo_municipio").HasMaxLength(7).IsRequired();
    end.Property(e => e.Uf).HasColumnName("end_uf").HasMaxLength(2).IsRequired();
    end.Property(e => e.Cep).HasColumnName("end_cep").HasMaxLength(8).IsRequired();
    end.Property(e => e.CodigoPais).HasColumnName("end_codigo_pais").HasMaxLength(4);
    end.Property(e => e.Telefone).HasColumnName("end_telefone").HasMaxLength(20);
});

// OwnsOne: ConfiguracaoFiscal — colunas: crt, serie, ambiente, uf_codigo
builder.OwnsOne(t => t.ConfiguracaoFiscal, cfg =>
{
    cfg.Property(c => c.Crt)
        .HasColumnName("crt")
        .HasConversion<int>()
        .IsRequired();
    cfg.Property(c => c.Serie)
        .HasColumnName("serie")
        .HasMaxLength(3)
        .IsRequired();
    cfg.Property(c => c.Ambiente)
        .HasColumnName("ambiente")
        .HasConversion<int>()
        .IsRequired();
    cfg.Property(c => c.UfCodigo)
        .HasColumnName("uf_codigo")
        .IsRequired();
});

// Campos bytea nullable para criptografia
builder.Property(t => t.Csc)
    .HasColumnName("csc")
    .HasColumnType("bytea");

builder.Property(t => t.CIdToken)
    .HasColumnName("c_id_token")
    .HasMaxLength(6);

builder.Property(t => t.CertificadoPfxCriptografado)
    .HasColumnName("certificado_pfx_criptografado")
    .HasColumnType("bytea");

builder.Property(t => t.CertificadoSenhaCriptografada)
    .HasColumnName("certificado_senha_criptografada")
    .HasColumnType("bytea");

builder.Property(t => t.CertificadoVencimento)
    .HasColumnName("certificado_vencimento");

builder.Property(t => t.IsActive)
    .HasColumnName("is_active")
    .IsRequired();

builder.Property(t => t.CreatedAt)
    .HasColumnName("created_at")
    .IsRequired();
```

---

### 3.6 `Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Configurations`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Domain.Enums;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class DocumentoFiscalConfiguration : IEntityTypeConfiguration<DocumentoFiscal>
{
    public void Configure(EntityTypeBuilder<DocumentoFiscal> builder);
}
```

**Notas de implementação críticas:**

```csharp
builder.ToTable("documentos_fiscais");

builder.HasKey(d => d.Id);
builder.Property(d => d.Id)
    .HasColumnName("id")
    .HasConversion(id => id.Value, value => new DocumentoFiscalId(value));

builder.Property(d => d.TenantId)
    .HasColumnName("tenant_id")
    .IsRequired();

builder.HasOne<Tenant>()
    .WithMany()
    .HasForeignKey(d => d.TenantId)
    .HasConstraintName("fk_documentos_fiscais_tenant_id")
    .OnDelete(DeleteBehavior.Restrict);

builder.Property(d => d.IdempotencyKey)
    .HasColumnName("idempotency_key")
    .HasMaxLength(100)
    .IsRequired();

// Índice único composto: (tenant_id, idempotency_key)
builder.HasIndex(d => new { d.TenantId, d.IdempotencyKey })
    .IsUnique()
    .HasDatabaseName("ix_documentos_fiscais_tenant_idempotency");

builder.Property(d => d.Tipo)
    .HasColumnName("tipo")
    .HasConversion<int>()
    .IsRequired();

// ChaveAcesso: conversor de ChaveAcesso -> string
builder.Property(d => d.ChaveAcesso)
    .HasColumnName("chave_acesso")
    .HasMaxLength(44)
    .IsRequired()
    .HasConversion(ca => ca.Valor, valor => new ChaveAcesso(valor));

builder.HasIndex(d => d.ChaveAcesso)
    .HasDatabaseName("ix_documentos_fiscais_chave_acesso");

builder.Property(d => d.Numero)
    .HasColumnName("numero")
    .IsRequired();

builder.Property(d => d.Serie)
    .HasColumnName("serie")
    .HasMaxLength(3)
    .IsRequired();

builder.Property(d => d.Status)
    .HasColumnName("status")
    .HasConversion<int>()
    .IsRequired();

// Índice composto (status, created_at) — suporta query do ReconciliacaoJobProcessor
builder.HasIndex(d => new { d.Status, d.CreatedAt })
    .HasDatabaseName("ix_documentos_fiscais_status_created_at");

builder.Property(d => d.XmlAssinado)
    .HasColumnName("xml_assinado")
    .HasColumnType("text");

builder.Property(d => d.Protocolo)
    .HasColumnName("protocolo")
    .HasMaxLength(50);

builder.Property(d => d.QrCode)
    .HasColumnName("qr_code")
    .HasMaxLength(1000);

builder.Property(d => d.MotivoRejeicao)
    .HasColumnName("motivo_rejeicao")
    .HasMaxLength(500);

builder.Property(d => d.CreatedAt)
    .HasColumnName("created_at")
    .IsRequired();

builder.Property(d => d.AuthorizedAt)
    .HasColumnName("authorized_at");

// OwnsMany: Items — DEVE usar HasField("_items") explícito
// Os itens são owned entities armazenados em tabela separada
builder.OwnsMany(d => d.Items, items =>
{
    items.ToTable("itens_documento");
    // HasField declara o backing field privado na entidade DocumentoFiscal
    items.HasField("_items");

    items.WithOwner().HasForeignKey("documento_fiscal_id");
    items.Property<Guid>("documento_fiscal_id").HasColumnName("documento_fiscal_id");

    items.Property(i => i.Numero)
        .HasColumnName("numero")
        .IsRequired();

    items.Property(i => i.ValorTotal)
        .HasColumnName("valor_total")
        .HasPrecision(18, 2)
        .IsRequired();

    // Produto: OwnsOne dentro de OwnsMany
    items.OwnsOne(i => i.Produto, prod =>
    {
        prod.Property(p => p.CodigoProduto).HasColumnName("produto_codigo").HasMaxLength(60).IsRequired();
        prod.Property(p => p.Descricao).HasColumnName("produto_descricao").HasMaxLength(120).IsRequired();
        prod.Property(p => p.Ncm).HasColumnName("produto_ncm").HasMaxLength(8).IsRequired();
        prod.Property(p => p.Cest).HasColumnName("produto_cest").HasMaxLength(7);
        prod.Property(p => p.CfopSaida).HasColumnName("produto_cfop").HasMaxLength(4).IsRequired();
        prod.Property(p => p.UnidadeComercial).HasColumnName("produto_unidade").HasMaxLength(6).IsRequired();
        prod.Property(p => p.Quantidade).HasColumnName("produto_quantidade").HasPrecision(15, 4).IsRequired();
        prod.Property(p => p.ValorUnitario).HasColumnName("produto_valor_unitario").HasPrecision(21, 10).IsRequired();
        prod.Property(p => p.ValorDesconto).HasColumnName("produto_valor_desconto").HasPrecision(15, 2);
        prod.Property(p => p.OrigemMercadoria).HasColumnName("produto_origem").HasConversion<int>().IsRequired();
    });

    // Tributo: OwnsOne dentro de OwnsMany
    items.OwnsOne(i => i.Tributo, trib =>
    {
        trib.Property(t => t.TipoIcms).HasColumnName("trib_tipo_icms").HasConversion<int>().IsRequired();
        trib.Property(t => t.CsosnOuCst).HasColumnName("trib_csosn_ou_cst").HasMaxLength(10).IsRequired();
        trib.Property(t => t.AliquotaIcms).HasColumnName("trib_aliquota_icms").HasPrecision(7, 4);
        trib.Property(t => t.BaseCalculoIcms).HasColumnName("trib_base_calculo_icms").HasPrecision(15, 2);
        trib.Property(t => t.ValorIcms).HasColumnName("trib_valor_icms").HasPrecision(15, 2);
        trib.Property(t => t.CstPis).HasColumnName("trib_cst_pis").HasConversion<int>().IsRequired();
        trib.Property(t => t.ValorPis).HasColumnName("trib_valor_pis").HasPrecision(15, 2);
        trib.Property(t => t.CstCofins).HasColumnName("trib_cst_cofins").HasConversion<int>().IsRequired();
        trib.Property(t => t.ValorCofins).HasColumnName("trib_valor_cofins").HasPrecision(15, 2);
    });
});

// OwnsMany: Pagamentos — DEVE usar HasField("_pagamentos") explícito
builder.OwnsMany(d => d.Pagamentos, pags =>
{
    pags.ToTable("pagamentos_documento");
    pags.HasField("_pagamentos");

    pags.WithOwner().HasForeignKey("documento_fiscal_id");
    pags.Property<Guid>("documento_fiscal_id").HasColumnName("documento_fiscal_id");

    pags.Property(p => p.TipoPagamento)
        .HasColumnName("tipo_pagamento")
        .HasConversion<int>()
        .IsRequired();

    pags.Property(p => p.Valor)
        .HasColumnName("valor")
        .HasPrecision(15, 2)
        .IsRequired();
});
```

**Alerta crítico sobre `HasField`:** Sem `HasField("_items")` e `HasField("_pagamentos")` explícitos, o EF Core não consegue localizar os backing fields privados da entidade `DocumentoFiscal`. A convenção automática tentaria `Items` público como getter, mas a propriedade expõe `IReadOnlyList` — incompatível com EF Core para owned collections. O `HasField` garante que EF Core usa os campos privados `_items` e `_pagamentos` para leitura e escrita.

---

### 3.7 `Infrastructure/Persistence/Configurations/DeliveryAttemptConfiguration.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Configurations`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder);
}
```

**Notas de implementação críticas:**

```csharp
builder.ToTable("delivery_attempts");

builder.HasKey(d => d.Id);
builder.Property(d => d.Id)
    .HasColumnName("id")
    .HasConversion(id => id.Value, value => new DeliveryAttemptId(value));

builder.Property(d => d.DocumentoFiscalId)
    .HasColumnName("documento_fiscal_id")
    .IsRequired();

builder.HasOne<DocumentoFiscal>()
    .WithMany()
    .HasForeignKey(d => d.DocumentoFiscalId)
    .HasConstraintName("fk_delivery_attempts_documento_fiscal_id")
    .OnDelete(DeleteBehavior.Cascade);

builder.Property(d => d.TipoTentativa)
    .HasColumnName("tipo_tentativa")
    .HasConversion<int>()
    .IsRequired();

builder.Property(d => d.AttemptedAt)
    .HasColumnName("attempted_at")
    .IsRequired();

builder.Property(d => d.Success)
    .HasColumnName("success")
    .IsRequired();

builder.Property(d => d.ResponseCode)
    .HasColumnName("response_code")
    .HasMaxLength(10);

builder.Property(d => d.ResponseMessage)
    .HasColumnName("response_message")
    .HasMaxLength(500);

builder.Property(d => d.ElapsedMs)
    .HasColumnName("elapsed_ms")
    .IsRequired();

builder.HasIndex(d => d.DocumentoFiscalId)
    .HasDatabaseName("ix_delivery_attempts_documento_fiscal_id");
```

---

### 3.8 `Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Configurations`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VisuFiscalHub.Infrastructure.Persistence;  // OutboxMessage
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder);
}
```

**Notas de implementação críticas:**

```csharp
builder.ToTable("outbox_messages");

builder.HasKey(o => o.Id);
builder.Property(o => o.Id).HasColumnName("id");

builder.Property(o => o.EventType)
    .HasColumnName("event_type")
    .HasMaxLength(500)
    .IsRequired();

builder.Property(o => o.Payload)
    .HasColumnName("payload")
    .HasColumnType("text")
    .IsRequired();

builder.Property(o => o.OccurredAt)
    .HasColumnName("occurred_at")
    .IsRequired();

builder.Property(o => o.ProcessedAt)
    .HasColumnName("processed_at");

// Índice composto (processed_at, occurred_at):
// - Suporta a query do OutboxRelayJob: WHERE processed_at IS NULL ORDER BY occurred_at
// - processed_at nullable: NULLs ficam no início no índice PostgreSQL (NULLS FIRST padrão)
builder.HasIndex(o => new { o.ProcessedAt, o.OccurredAt })
    .HasDatabaseName("ix_outbox_messages_processed_at_occurred_at");
```

**Definição da classe `OutboxMessage`** (POCO de infraestrutura, definir no mesmo namespace `VisuFiscalHub.Infrastructure.Persistence`):

```csharp
namespace VisuFiscalHub.Infrastructure.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
    public DateTime? ProcessedAt { get; set; }
}
```

Esta classe pode ser definida em arquivo próprio `OutboxMessage.cs` dentro de `Infrastructure/Persistence/` ou embutida no mesmo arquivo da configuração. Recomenda-se arquivo próprio.

---

### 3.9 `Infrastructure/Persistence/Repositories/ClienteAppRepository.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Repositories`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Repositories;

public sealed class ClienteAppRepository : IClienteAppRepository
{
    private readonly ApplicationDbContext _context;

    public ClienteAppRepository(ApplicationDbContext context);

    public Task<ClienteApp?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);
    public Task<ClienteApp?> GetByIdAsync(ClienteAppId id, CancellationToken cancellationToken = default);
    public Task AddAsync(ClienteApp clienteApp, CancellationToken cancellationToken = default);
}
```

**Notas de implementação críticas:**

- `GetByClientIdAsync` e `GetByIdAsync`: usar `AsNoTracking()` apenas quando usado em queries read-only. Quando o resultado precisar ser modificado e salvo (comandos), NÃO usar `AsNoTracking()`. Para os handlers de comando (que modificam e salvam), omitir `AsNoTracking()`. Para os handlers de query (que apenas leem), adicionar `AsNoTracking()`.
- `AddAsync`: chamar `await _context.ClienteApps.AddAsync(clienteApp, cancellationToken)`. O commit é responsabilidade do `IUnitOfWork`.

---

### 3.10 `Infrastructure/Persistence/Repositories/TenantRepository.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Repositories`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.ValueObjects;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Repositories;

public sealed class TenantRepository : ITenantRepository
{
    private readonly ApplicationDbContext _context;

    public TenantRepository(ApplicationDbContext context);

    public Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default);
    public Task<Tenant?> GetByCnpjAsync(Cnpj cnpj, ClienteAppId clienteAppId, CancellationToken cancellationToken = default);
    public Task<List<Tenant>> GetByClienteAppIdAsync(ClienteAppId clienteAppId, CancellationToken cancellationToken = default);
    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
    public Task<long> GetNextNumeracaoAsync(TenantId tenantId, string serie, CancellationToken cancellationToken = default);
}
```

**Notas de implementação críticas — `GetNextNumeracaoAsync` (CRÍTICO):**

```csharp
public async Task<long> GetNextNumeracaoAsync(
    TenantId tenantId,
    string serie,
    CancellationToken cancellationToken = default)
{
    // tenantId.ToString("N") = UUID sem hífens (32 chars hex)
    // Ex: "550e8400e29b41d4a716446655440000" (sem { } e sem -)
    var tenantIdHex = tenantId.Value.ToString("N");

    // A série é validada com regex ^[0-9]{1,3}$ no CommandHandler antes de chegar aqui.
    // A sequence foi criada no CreateTenantCommandHandler com o mesmo padrão de nome.
    var sequenceName = $"seq_nfe_{tenantIdHex}_{serie}";

    // Executar SELECT nextval diretamente — retorna o próximo número atômico
    var result = await _context.Database
        .SqlQueryRaw<long>($"SELECT nextval('{sequenceName}')")
        .FirstAsync(cancellationToken);

    return result;
}
```

**Atenção:** `ToString("N")` produz UUID sem hífens (32 caracteres hexadecimais). Exemplo: `550e8400e29b41d4a716446655440000`. O formato padrão `ToString()` ou `ToString("D")` inclui hífens e geraria nome de sequence inválido para PostgreSQL. Esta é uma regra invariável alinhada com a documentação em `decisions.md` seção RN-05.

**`GetByIdAsync` para comandos** (carrega owned entities):
```csharp
public async Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken cancellationToken = default)
{
    return await _context.Tenants
        .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
}
```

EF Core carrega automaticamente os owned entities (`Endereco`, `ConfiguracaoFiscal`) ao carregar o `Tenant` principal — não requer `Include` para `OwnsOne`.

---

### 3.11 `Infrastructure/Persistence/Repositories/DocumentoFiscalRepository.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Persistence.Repositories`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Domain.Enums;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure.Persistence.Repositories;

public sealed class DocumentoFiscalRepository : IDocumentoFiscalRepository
{
    private readonly ApplicationDbContext _context;

    public DocumentoFiscalRepository(ApplicationDbContext context);

    public Task<DocumentoFiscal?> GetByIdAsync(DocumentoFiscalId id, CancellationToken cancellationToken = default);
    public Task<DocumentoFiscal?> GetByIdempotencyKeyAsync(string idempotencyKey, TenantId tenantId, CancellationToken cancellationToken = default);
    public Task AddAsync(DocumentoFiscal documento, CancellationToken cancellationToken = default);
    public Task UpdateAsync(DocumentoFiscal documento, CancellationToken cancellationToken = default);
    public Task<List<DocumentoFiscal>> GetProcessandoAntigoAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

**Notas de implementação críticas:**

- `GetByIdAsync`: Carrega owned collections (`Items`, `Pagamentos`) automaticamente via EF Core para owned entities. Se necessário explicitamente via navigation, verificar se EF Core 10 carrega automaticamente `OwnsMany` — em versões anteriores era necessário, em EF Core 10 o comportamento padrão carrega owned entities mas colections owned em tabelas separadas podem exigir `include` implícito.

- `GetByIdempotencyKeyAsync`:
  ```csharp
  return await _context.DocumentosFiscais
      .FirstOrDefaultAsync(d =>
          d.IdempotencyKey == idempotencyKey &&
          d.TenantId == tenantId,
          cancellationToken);
  ```

- `GetProcessandoAntigoAsync` — usada pelo `ReconciliacaoJobProcessor`:
  ```csharp
  var limite = DateTime.UtcNow.Subtract(timeout);
  return await _context.DocumentosFiscais
      .Where(d => d.Status == StatusDocumento.Processando && d.CreatedAt < limite)
      .ToListAsync(cancellationToken);
  ```
  O índice `(status, created_at)` em `documentos_fiscais` torna esta query eficiente.

- `UpdateAsync`: Para entidades rastreadas pelo contexto, simplesmente marcar como modified. Como o repositório não usa `AsNoTracking()`, a entidade retornada por `GetByIdAsync` já está rastreada — `SaveChangesAsync` detecta as mudanças automaticamente. O método `UpdateAsync` pode ser implementado como:
  ```csharp
  public Task UpdateAsync(DocumentoFiscal documento, CancellationToken cancellationToken = default)
  {
      _context.DocumentosFiscais.Update(documento);
      return Task.CompletedTask;
  }
  ```

---

### 3.12 `Infrastructure/DependencyInjection.cs`

**Namespace:** `VisuFiscalHub.Infrastructure`

**Usings necessários:**
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Persistence;
using VisuFiscalHub.Infrastructure.Persistence.Repositories;
```

**Assinatura completa:**
```csharp
namespace VisuFiscalHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration);
}
```

**Notas de implementação críticas:**

```csharp
public static IServiceCollection AddInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // 1. Registrar ApplicationDbContext com DomainEventsInterceptor
    services.AddDbContext<ApplicationDbContext>((sp, options) =>
    {
        options.UseNpgsql(
            configuration.GetConnectionString("DefaultConnection"),
            npgsql => npgsql.MigrationsAssembly(
                typeof(ApplicationDbContext).Assembly.FullName));

        // DomainEventsInterceptor registrado aqui — instância singleton
        options.AddInterceptors(new DomainEventsInterceptor());
    });

    // 2. Repositórios como Scoped (ciclo de vida do request)
    services.AddScoped<IClienteAppRepository, ClienteAppRepository>();
    services.AddScoped<ITenantRepository, TenantRepository>();
    services.AddScoped<IDocumentoFiscalRepository, DocumentoFiscalRepository>();
    services.AddScoped<IUnitOfWork, UnitOfWork>();

    return services;
}
```

**Ponto crítico:** `new DomainEventsInterceptor()` é instanciado diretamente no `AddInterceptors`. Isso é intencional — o interceptor não possui dependências injetadas nesta fase (Fase 2). Nas fases subsequentes, se o interceptor precisar de dependências, deve ser registrado via `services.AddSingleton<DomainEventsInterceptor>()` e resolvido via `sp.GetRequiredService<DomainEventsInterceptor>()`.

---

## 4. Schema do Banco de Dados

### 4.1 Tabela: `cliente_apps`

| Coluna | Tipo PostgreSQL | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `name` | `varchar(100)` | NOT NULL |
| `client_id` | `varchar(100)` | NOT NULL, UNIQUE |
| `client_secret_hash` | `varchar(500)` | NOT NULL |
| `webhook_url` | `varchar(500)` | NULL |
| `webhook_secret_criptografado` | `bytea` | NULL |
| `is_active` | `boolean` | NOT NULL |
| `created_at` | `timestamp with time zone` | NOT NULL |

**Índices:**
- `ix_cliente_apps_client_id` — UNIQUE em `client_id`

---

### 4.2 Tabela: `tenants`

| Coluna | Tipo PostgreSQL | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `cliente_app_id` | `uuid` | NOT NULL, FK → `cliente_apps(id)` |
| `cnpj` | `varchar(14)` | NOT NULL |
| `razao_social` | `varchar(300)` | NOT NULL |
| `nome_fantasia` | `varchar(300)` | NULL |
| `end_logradouro` | `varchar(200)` | NOT NULL |
| `end_numero` | `varchar(10)` | NOT NULL |
| `end_complemento` | `varchar(100)` | NULL |
| `end_bairro` | `varchar(100)` | NOT NULL |
| `end_municipio` | `varchar(100)` | NOT NULL |
| `end_codigo_municipio` | `varchar(7)` | NOT NULL |
| `end_uf` | `varchar(2)` | NOT NULL |
| `end_cep` | `varchar(8)` | NOT NULL |
| `end_codigo_pais` | `varchar(4)` | NULL |
| `end_telefone` | `varchar(20)` | NULL |
| `crt` | `integer` | NOT NULL |
| `serie` | `varchar(3)` | NOT NULL |
| `ambiente` | `integer` | NOT NULL |
| `uf_codigo` | `integer` | NOT NULL |
| `csc` | `bytea` | NULL |
| `c_id_token` | `varchar(6)` | NULL |
| `certificado_pfx_criptografado` | `bytea` | NULL |
| `certificado_senha_criptografada` | `bytea` | NULL |
| `certificado_vencimento` | `timestamp with time zone` | NULL |
| `is_active` | `boolean` | NOT NULL |
| `created_at` | `timestamp with time zone` | NOT NULL |

**Índices:**
- `ix_tenants_cnpj_cliente_app_id` — UNIQUE em `(cnpj, cliente_app_id)`
- `fk_tenants_cliente_app_id` — FK constraint para `cliente_apps(id)` com `ON DELETE RESTRICT`

---

### 4.3 Tabela: `documentos_fiscais`

| Coluna | Tipo PostgreSQL | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | NOT NULL, FK → `tenants(id)` |
| `idempotency_key` | `varchar(100)` | NOT NULL |
| `tipo` | `integer` | NOT NULL |
| `chave_acesso` | `varchar(44)` | NOT NULL |
| `numero` | `bigint` | NOT NULL |
| `serie` | `varchar(3)` | NOT NULL |
| `status` | `integer` | NOT NULL |
| `xml_assinado` | `text` | NULL |
| `protocolo` | `varchar(50)` | NULL |
| `qr_code` | `varchar(1000)` | NULL |
| `motivo_rejeicao` | `varchar(500)` | NULL |
| `created_at` | `timestamp with time zone` | NOT NULL |
| `authorized_at` | `timestamp with time zone` | NULL |

**Índices:**
- `ix_documentos_fiscais_tenant_idempotency` — UNIQUE em `(tenant_id, idempotency_key)`
- `ix_documentos_fiscais_chave_acesso` — em `chave_acesso`
- `ix_documentos_fiscais_status_created_at` — em `(status, created_at)` — para `ReconciliacaoJobProcessor`
- `fk_documentos_fiscais_tenant_id` — FK constraint para `tenants(id)` com `ON DELETE RESTRICT`

**Valores de `status` (enum `StatusDocumento`):**
| Valor int | Nome |
|---|---|
| 0 | Criado |
| 1 | Enfileirado |
| 2 | Processando |
| 3 | Autorizado |
| 4 | Rejeitado |
| 5 | Cancelado |
| 6 | Falhou |
| 7 | Denegado |

---

### 4.4 Tabela: `itens_documento`

| Coluna | Tipo PostgreSQL | Constraints |
|---|---|---|
| `id` | `integer` (gerado pelo EF) | PK (surrogate) |
| `documento_fiscal_id` | `uuid` | NOT NULL, FK → `documentos_fiscais(id)` |
| `numero` | `integer` | NOT NULL |
| `valor_total` | `numeric(18,2)` | NOT NULL |
| `produto_codigo` | `varchar(60)` | NOT NULL |
| `produto_descricao` | `varchar(120)` | NOT NULL |
| `produto_ncm` | `varchar(8)` | NOT NULL |
| `produto_cest` | `varchar(7)` | NULL |
| `produto_cfop` | `varchar(4)` | NOT NULL |
| `produto_unidade` | `varchar(6)` | NOT NULL |
| `produto_quantidade` | `numeric(15,4)` | NOT NULL |
| `produto_valor_unitario` | `numeric(21,10)` | NOT NULL |
| `produto_valor_desconto` | `numeric(15,2)` | NULL |
| `produto_origem` | `integer` | NOT NULL |
| `trib_tipo_icms` | `integer` | NOT NULL |
| `trib_csosn_ou_cst` | `varchar(10)` | NOT NULL |
| `trib_aliquota_icms` | `numeric(7,4)` | NULL |
| `trib_base_calculo_icms` | `numeric(15,2)` | NULL |
| `trib_valor_icms` | `numeric(15,2)` | NULL |
| `trib_cst_pis` | `integer` | NOT NULL |
| `trib_valor_pis` | `numeric(15,2)` | NULL |
| `trib_cst_cofins` | `integer` | NOT NULL |
| `trib_valor_cofins` | `numeric(15,2)` | NULL |

---

### 4.5 Tabela: `pagamentos_documento`

| Coluna | Tipo PostgreSQL | Constraints |
|---|---|---|
| `id` | `integer` (gerado pelo EF) | PK (surrogate) |
| `documento_fiscal_id` | `uuid` | NOT NULL, FK → `documentos_fiscais(id)` |
| `tipo_pagamento` | `integer` | NOT NULL |
| `valor` | `numeric(15,2)` | NOT NULL |

---

### 4.6 Tabela: `delivery_attempts`

| Coluna | Tipo PostgreSQL | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `documento_fiscal_id` | `uuid` | NOT NULL, FK → `documentos_fiscais(id)` |
| `tipo_tentativa` | `integer` | NOT NULL |
| `attempted_at` | `timestamp with time zone` | NOT NULL |
| `success` | `boolean` | NOT NULL |
| `response_code` | `varchar(10)` | NULL |
| `response_message` | `varchar(500)` | NULL |
| `elapsed_ms` | `bigint` | NOT NULL |

**Índices:**
- `ix_delivery_attempts_documento_fiscal_id` — em `documento_fiscal_id`
- `fk_delivery_attempts_documento_fiscal_id` — FK constraint para `documentos_fiscais(id)` com `ON DELETE CASCADE`

---

### 4.7 Tabela: `outbox_messages`

| Coluna | Tipo PostgreSQL | Constraints |
|---|---|---|
| `id` | `uuid` | PK |
| `event_type` | `varchar(500)` | NOT NULL |
| `payload` | `text` | NOT NULL |
| `occurred_at` | `timestamp with time zone` | NOT NULL |
| `processed_at` | `timestamp with time zone` | NULL |

**Índices:**
- `ix_outbox_messages_processed_at_occurred_at` — em `(processed_at, occurred_at)` — suporta query `WHERE processed_at IS NULL ORDER BY occurred_at`

**Nota sobre NULL em índice PostgreSQL:** PostgreSQL trata `NULL` como distinto de qualquer valor em índices compostos. A query `WHERE processed_at IS NULL` usa o índice composto porque `processed_at` é a primeira coluna — o índice parcial implícito de NULLs permite varredura eficiente dos registros não processados.

---

### 4.8 Sequences de Numeração (criadas dinamicamente — NÃO na migration)

As sequences de numeração **NÃO** são criadas pela migration. Elas são criadas no `CreateTenantCommandHandler` (Fase 3) via:

```sql
CREATE SEQUENCE IF NOT EXISTS seq_nfe_{tenantId:N}_{serie}
  START 1
  INCREMENT 1;
```

Padrão do nome: `seq_nfe_` + UUID sem hífens (32 chars hex) + `_` + série (1-3 dígitos).

Exemplo: `seq_nfe_550e8400e29b41d4a716446655440000_001`

---

## 5. Diagrama de Dependências entre Entidades

```
┌─────────────────────────────────────────────────────────────────┐
│                  VisuFiscalHub — Modelo de Dados                 │
└─────────────────────────────────────────────────────────────────┘

  ┌──────────────────────────┐
  │      ClienteApp          │
  │  (Aggregate Root)        │
  ├──────────────────────────┤
  │ id: ClienteAppId (uuid)  │
  │ name: string             │
  │ client_id: string        │◄──── índice único
  │ client_secret_hash       │
  │ webhook_url?             │
  │ webhook_secret?: bytea   │
  │ is_active: bool          │
  │ created_at               │
  └──────────────────────────┘
               │ 1
               │ HasMany (sem navigation property)
               │ N
  ┌──────────────────────────────────────────────────────────────┐
  │                         Tenant                               │
  │                  (Aggregate Root)                            │
  ├──────────────────────────────────────────────────────────────┤
  │ id: TenantId (uuid)                                          │
  │ cliente_app_id: uuid (FK)                                    │
  │ cnpj: varchar(14) ──────────── índice único (cnpj+cliente)  │
  │ razao_social                                                 │
  │ nome_fantasia?                                               │
  │ ╔════════════════════╗   ← OwnsOne (colunas end_*)           │
  │ ║    Endereco         ║                                       │
  │ ║  logradouro         ║                                       │
  │ ║  numero             ║                                       │
  │ ║  bairro, municipio  ║                                       │
  │ ║  uf, cep            ║                                       │
  │ ╚════════════════════╝                                       │
  │ ╔════════════════════╗   ← OwnsOne (colunas crt/serie/etc)  │
  │ ║ ConfiguracaoFiscal  ║                                       │
  │ ║  crt: int           ║                                       │
  │ ║  serie: varchar     ║                                       │
  │ ║  ambiente: int      ║                                       │
  │ ║  uf_codigo: int     ║                                       │
  │ ╚════════════════════╝                                       │
  │ csc?: bytea (criptografado AES-GCM)                          │
  │ c_id_token?: varchar(6)                                      │
  │ certificado_pfx?: bytea (criptografado AES-GCM)              │
  │ certificado_senha?: bytea (criptografado AES-GCM)            │
  │ certificado_vencimento?: datetime                            │
  │ is_active, created_at                                        │
  └──────────────────────────────────────────────────────────────┘
               │ 1
               │ HasMany (sem navigation property)
               │ N
  ┌──────────────────────────────────────────────────────────────┐
  │                    DocumentoFiscal                           │
  │                  (Aggregate Root)                            │
  ├──────────────────────────────────────────────────────────────┤
  │ id: DocumentoFiscalId (uuid)                                 │
  │ tenant_id: uuid (FK)                                         │
  │ idempotency_key ──────────── índice único (tenant+key)       │
  │ tipo: int                                                    │
  │ chave_acesso: varchar(44) ── índice                          │
  │ numero: bigint                                               │
  │ serie: varchar(3)                                            │
  │ status: int ──────────────── índice (status, created_at)    │
  │ xml_assinado?: text                                          │
  │ protocolo?, qr_code?, motivo_rejeicao?                       │
  │ created_at, authorized_at?                                   │
  │                                                              │
  │   ╔═══════════════════════════════════╗                      │
  │   ║  _items: List<ItemDocumento>      ║ ← OwnsMany           │
  │   ║  → tabela: itens_documento        ║   HasField("_items") │
  │   ║    [numero, valor_total,          ║                      │
  │   ║     produto.*, tributo.*]         ║                      │
  │   ╚═══════════════════════════════════╝                      │
  │                                                              │
  │   ╔═══════════════════════════════════╗                      │
  │   ║  _pagamentos: List<Pagamento>     ║ ← OwnsMany           │
  │   ║  → tabela: pagamentos_documento   ║   HasField("_pag")   │
  │   ║    [tipo_pagamento, valor]        ║                      │
  │   ╚═══════════════════════════════════╝                      │
  └──────────────────────────────────────────────────────────────┘
               │ 1
               │ HasMany
               │ N
  ┌──────────────────────────┐
  │     DeliveryAttempt      │
  │      (Entity)            │
  ├──────────────────────────┤
  │ id: DeliveryAttemptId    │
  │ documento_fiscal_id (FK) │
  │ tipo_tentativa: int      │
  │ attempted_at             │
  │ success: bool            │
  │ response_code?, message? │
  │ elapsed_ms: bigint       │
  └──────────────────────────┘

  ┌──────────────────────────────┐
  │       OutboxMessage          │
  │  (Infraestrutura — POCO)     │
  ├──────────────────────────────┤
  │ id: uuid                     │
  │ event_type: varchar(500)     │
  │ payload: text (JSON)         │
  │ occurred_at: datetime        │
  │ processed_at?: datetime ─── índice (processed_at, occurred_at)
  └──────────────────────────────┘

  ┌─────────────────────────────────────────────────┐
  │         Sequences (criadas dinamicamente)        │
  ├─────────────────────────────────────────────────┤
  │ seq_nfe_{tenantId:N}_{serie}                     │
  │ Ex: seq_nfe_550e8400e29b41d4a716446655440000_001 │
  │ Criadas por: CreateTenantCommandHandler (Fase 3) │
  │ Consumidas por: TenantRepository.GetNextNumera.. │
  └─────────────────────────────────────────────────┘
```

---

## 6. docker-compose.yml e `.env.example`

### `infra/docker-compose.yml`

```yaml
services:
  postgres:
    image: postgres:17
    container_name: visu_fiscal_hub_postgres
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-visufiscalhub}
      POSTGRES_USER: ${POSTGRES_USER:-visufiscal}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Definir POSTGRES_PASSWORD no .env}
    ports:
      - "${POSTGRES_PORT:-5432}:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER:-visufiscal} -d ${POSTGRES_DB:-visufiscalhub}"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

  redis:
    image: redis:7-alpine
    container_name: visu_fiscal_hub_redis
    ports:
      - "${REDIS_PORT:-6379}:6379"
    volumes:
      - redis_data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
    restart: unless-stopped

volumes:
  postgres_data:
  redis_data:
```

### `infra/.env.example`

```dotenv
# PostgreSQL
POSTGRES_DB=visufiscalhub
POSTGRES_USER=visufiscal
POSTGRES_PASSWORD=troque_esta_senha_em_producao
POSTGRES_PORT=5432

# Redis
REDIS_PORT=6379

# Connection string para a aplicação (appsettings.Development.json ou variável de ambiente)
# ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=visufiscalhub;Username=visufiscal;Password=troque_esta_senha_em_producao
```

**Nota:** O arquivo `.env` nunca deve ser commitado. Adicionar ao `.gitignore`:
```
infra/.env
```

---

## 7. Configuração da Connection String

O `appsettings.Development.json` deve conter:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=visufiscalhub;Username=visufiscal;Password=troque_esta_senha_em_producao"
  }
}
```

Para produção, a connection string é fornecida via variável de ambiente:
```
ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=visufiscalhub;...
```

O `AddInfrastructure` lê via `configuration.GetConnectionString("DefaultConnection")`.

---

## 8. Migrations

A migration é gerada via ferramenta EF Core — **não criada manualmente**.

**Pré-requisito:** O projeto `VisuFiscalHub.Infrastructure.csproj` já possui `Microsoft.EntityFrameworkCore.Design` como `PrivateAssets=all`.

**Comando para gerar a migration inicial:**
```bash
cd src/VisuFiscalHub.Infrastructure
dotnet ef migrations add InitialCreate \
  --startup-project ../VisuFiscalHub.Api/VisuFiscalHub.Api.csproj \
  --context ApplicationDbContext
```

**Ou a partir da raiz da solução:**
```bash
dotnet ef migrations add InitialCreate \
  --project src/VisuFiscalHub.Infrastructure \
  --startup-project src/VisuFiscalHub.Api \
  --context ApplicationDbContext
```

**Para aplicar ao banco local:**
```bash
dotnet ef database update \
  --project src/VisuFiscalHub.Infrastructure \
  --startup-project src/VisuFiscalHub.Api
```

**A migration gerada deve conter:**
- Criação de todas as 7 tabelas listadas na seção 4.
- Todos os índices e constraints FK.
- **NÃO deve criar** nenhuma sequence de numeração (`seq_nfe_*`).

---

## 9. Checklist de Conclusão

### Banco de Dados
- [ ] Migration `InitialCreate` gerada sem erros de compilação
- [ ] `dotnet ef database update` executa sem erros no PostgreSQL local
- [ ] Tabela `cliente_apps` criada com índice único em `client_id`
- [ ] Tabela `tenants` criada com índice único em `(cnpj, cliente_app_id)`
- [ ] Colunas `csc`, `certificado_pfx_criptografado`, `certificado_senha_criptografada` como `bytea` nullable
- [ ] Coluna `webhook_secret_criptografado` em `cliente_apps` como `bytea` nullable
- [ ] `OwnsOne(Endereco)` mapeado com colunas prefixadas `end_`
- [ ] `OwnsOne(ConfiguracaoFiscal)` mapeado com colunas `crt`, `serie`, `ambiente`, `uf_codigo`
- [ ] Tabela `documentos_fiscais` com índice único em `(tenant_id, idempotency_key)`
- [ ] Índice `(status, created_at)` em `documentos_fiscais`
- [ ] Tabelas `itens_documento` e `pagamentos_documento` criadas (OwnsMany)
- [ ] Tabela `delivery_attempts` criada com FK para `documentos_fiscais`
- [ ] Tabela `outbox_messages` criada com índice `(processed_at, occurred_at)`
- [ ] Nenhuma sequence `seq_nfe_*` criada pela migration

### EF Core / Código
- [ ] `DomainEventsInterceptor` sobrescreve `SavingChangesAsync` (não `SavedChangesAsync`)
- [ ] `DomainEventsInterceptor` não chama `SaveChangesAsync` internamente
- [ ] `OutboxMessage` adicionados via `context.Set<OutboxMessage>().AddRange(...)` na mesma transação
- [ ] `ClearDomainEvents()` chamado após coleta
- [ ] `ApplicationDbContext` registra todas as configurações em `OnModelCreating`
- [ ] Conversores de value types em `ConfigureConventions` para todos os strongly-typed IDs e value objects
- [ ] `OwnsMany(d => d.Items, ...)` com `HasField("_items")` explícito
- [ ] `OwnsMany(d => d.Pagamentos, ...)` com `HasField("_pagamentos")` explícito
- [ ] `TenantRepository.GetNextNumeracaoAsync` usa `tenantId.Value.ToString("N")` (sem hífens)
- [ ] `DependencyInjection.AddInfrastructure` registra interceptor via `AddInterceptors(new DomainEventsInterceptor())`
- [ ] Repositórios registrados como `Scoped`
- [ ] `IUnitOfWork` registrado como `Scoped`

### Docker
- [ ] `infra/docker-compose.yml` sobe `postgres:17` e `redis:7-alpine`
- [ ] `infra/.env.example` contém todas as variáveis necessárias
- [ ] `infra/.env` adicionado ao `.gitignore`
- [ ] `docker compose up` executa sem erros
- [ ] `GET /health/ready` retorna `Healthy` com banco conectado (após Fase 8)

### Clean Architecture
- [ ] `VisuFiscalHub.Domain` não referencia nenhum package externo
- [ ] `DomainEventsInterceptor` e `ApplicationDbContext` residem apenas em Infrastructure
- [ ] Nenhuma referência a `Microsoft.EntityFrameworkCore` no projeto Application ou Domain
- [ ] Repositórios satisfazem interfaces definidas em `VisuFiscalHub.Domain.Interfaces`
- [ ] `IUnitOfWork` satisfaz interface definida em `VisuFiscalHub.Domain.Interfaces`

---

*Spec criada em 2026-05-12. Contrato de implementação para a Fase 2 do VisuFiscalHub.*
