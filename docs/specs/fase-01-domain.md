# Spec Técnica — Fase 1: Domain Layer

**Versão:** 1.0  
**Data:** 2026-05-12  
**Responsável:** Time VisuFiscalHub  
**Referências:** `docs/decisions.md` (seções 1–5), `docs/implementation-plan.md` (Fase 1)

---

## 1. Visão Geral da Fase

### Objetivo

Construir o núcleo imutável do domínio sem nenhuma dependência de pacote externo. Esta fase produz todos os tipos que servem de contrato entre Domain, Application e Infrastructure — primitivos, entidades, value objects, enums, domain events, erros e interfaces.

### Pré-requisitos

- Nenhuma fase anterior é necessária.
- O projeto `VisuFiscalHub.Domain.csproj` já existe com `<TargetFramework>net10.0</TargetFramework>` e sem referências a NuGet packages externos.

### Critério de Conclusão

- [ ] Todos os arquivos listados na seção 2 existem e compilam sem erros em `net10.0`
- [ ] Nenhuma referência a NuGet package externo no arquivo `.csproj` do Domain
- [ ] `ChaveAcesso.Gerar(...)` produz string de exatamente 44 dígitos
- [ ] `ChaveAcesso.Gerar(...)` com os 43 dígitos `3509061420016714006512500100000180010000009` produz cDV = `7` (MOC 7.0 Seção 4.1.6)
- [ ] `QrCode.Gerar(...)` concatena `chaveAcesso + "|2|" + tpAmb + "|" + csc` para SHA-1 (o `|2|` é versão QR Code, não `tpAmb`)
- [ ] `Cnpj` rejeita CPFs-todos-iguais (111.111.111/1111-11) e aceita entrada com ou sem máscara
- [ ] `Cpf` rejeita todos-dígitos-iguais e aceita entrada com ou sem máscara
- [ ] `DocumentoFiscal` state machine: transições inválidas retornam `Result.Failure` — nunca lançam exceção
- [ ] `StatusDocumento` enum contém `Falhou` e `Denegado`
- [ ] `Cancelar` retorna erro para `authorizedAt + 30min == utcNow` (boundary estrito: `<`, não `<=`)

---

## 2. Árvore de Arquivos

Todos os caminhos são relativos a `src/VisuFiscalHub.Domain/`.

```
src/VisuFiscalHub.Domain/
│
├── Common/
│   ├── Error.cs
│   ├── Result.cs
│   ├── IDomainEvent.cs
│   ├── IDomainEventSource.cs
│   ├── Entity.cs
│   └── StronglyTypedId.cs
│
├── Identifiers/
│   ├── ClienteAppId.cs
│   ├── TenantId.cs
│   ├── DocumentoFiscalId.cs
│   └── DeliveryAttemptId.cs
│
├── Enums/
│   ├── TipoDocumento.cs
│   ├── StatusDocumento.cs
│   ├── RegimeTributario.cs
│   ├── TipoEmissao.cs
│   ├── AmbienteSefaz.cs
│   ├── TipoPagamento.cs
│   ├── ModalidadeFrete.cs
│   ├── TipoIcms.cs
│   ├── OrigemMercadoria.cs
│   ├── CSOSN.cs
│   ├── CstIcms.cs
│   ├── CstPisCofins.cs
│   └── TipoTentativa.cs
│
├── ValueObjects/
│   ├── Cnpj.cs
│   ├── Cpf.cs
│   ├── ChaveAcesso.cs
│   ├── QrCode.cs
│   ├── Endereco.cs
│   ├── Produto.cs
│   ├── Tributo.cs
│   ├── Pagamento.cs
│   ├── ConfiguracaoFiscal.cs
│   └── CertificadoDigital.cs
│
├── Entities/
│   ├── ClienteApp.cs
│   ├── Tenant.cs
│   ├── DocumentoFiscal.cs
│   ├── ItemDocumento.cs
│   └── DeliveryAttempt.cs
│
├── Events/
│   ├── DocumentoFiscalAutorizadoEvent.cs
│   ├── DocumentoFiscalRejeitadoEvent.cs
│   ├── DocumentoFiscalCanceladoEvent.cs
│   ├── DocumentoFiscalFalhouEvent.cs
│   ├── DocumentoFiscalDenegadoEvent.cs
│   └── TenantProvisionadoEvent.cs
│
├── Errors/
│   ├── ClienteAppErrors.cs
│   ├── TenantErrors.cs
│   └── DocumentoFiscalErrors.cs
│
└── Interfaces/
    ├── IClienteAppRepository.cs
    ├── ITenantRepository.cs
    ├── IDocumentoFiscalRepository.cs
    └── IUnitOfWork.cs
```

**Nota:** As interfaces da Application layer (`ISefazClient`, `ICurrentUserContext`, `IDocumentJobQueue`, etc.) residem em `src/VisuFiscalHub.Application/Common/Interfaces/` — fora do escopo do Domain project, mas documentadas na seção 3.14 por serem interdependentes com os tipos do Domain.

---

## 3. Especificação de Cada Arquivo

---

### 3.1 `Common/Error.cs`

**Namespace:** `VisuFiscalHub.Domain.Common`

**Usings:** nenhum

```csharp
namespace VisuFiscalHub.Domain.Common;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
```

**Invariantes:**
- `Code` e `Message` são imutáveis após criação (record).
- `Error.None` representa ausência de erro — usado em `Result` bem-sucedido.
- Nunca herda de `Exception`. Nunca é lançado como exceção.

**Notas de implementação:**
- `sealed record` garante comparação por valor. Dois `Error` com mesmo `Code` e `Message` são iguais.
- `Error.None` é `static readonly` — singleton por design.

---

### 3.2 `Common/Result.cs`

**Namespace:** `VisuFiscalHub.Domain.Common`

**Usings:** nenhum

```csharp
namespace VisuFiscalHub.Domain.Common;

public class Result
{
    protected Result(bool isSuccess, Error error);

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success();
    public static Result Failure(Error error);
    public static Result<T> Success<T>(T value);
    public static Result<T> Failure<T>(Error error);
}

public class Result<T> : Result
{
    private Result(T value, bool isSuccess, Error error);

    public T Value { get; }   // lança InvalidOperationException se IsFailure

    public static Result<T> Success(T value);
    public static new Result<T> Failure(Error error);
}
```

**Invariantes:**
- `Result.Success()` → `IsSuccess = true`, `Error = Error.None`
- `Result.Failure(error)` → `IsSuccess = false`, `Error = error` (error não pode ser `Error.None`)
- `Result<T>.Value` acessado em `IsFailure` lança `InvalidOperationException` — acesso indevido a resultado de falha.
- `Result<T>` herda de `Result` para permitir atribuição polimórfica onde necessário.

**Notas de implementação:**
- O construtor protegido de `Result` valida: se `isSuccess == true`, `error` deve ser `Error.None`; se `isSuccess == false`, `error` não pode ser `Error.None`. Lançar `ArgumentException` na construção incorreta (invariante de construção, não de domínio).
- `Result<T>.Value` deve usar `_value!` com backing field para suportar `Nullable` habilitado. Definir backing field como `T?` e acessar com `!` no getter, após checar `IsSuccess`.
- Métodos estáticos de fábrica em `Result` para `Success<T>` e `Failure<T>` evitam que o chamador precise conhecer o tipo concreto.

---

### 3.3 `Common/IDomainEvent.cs`

**Namespace:** `VisuFiscalHub.Domain.Common`

**Usings:** nenhum

```csharp
namespace VisuFiscalHub.Domain.Common;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
}
```

**Invariantes:**
- Todos os domain events devem implementar esta interface.
- `EventId` garante unicidade para processamento idempotente no `OutboxRelayJob`.
- `OccurredAt` é o timestamp UTC de quando o evento ocorreu (não quando foi persistido).

**Notas de implementação:**
- Interface mínima. Implementações são records — ver seção 3 (Events/).
- `OccurredAt` deve ser inicializado com `DateTime.UtcNow` na criação do record. Como o Domain não pode usar `TimeProvider`, a data/hora é capturada no momento da criação do evento (não da entidade). Handlers de domain events não devem alterar `OccurredAt`.

---

### 3.4 `Common/IDomainEventSource.cs`

**Namespace:** `VisuFiscalHub.Domain.Common`

**Usings:** `System.Collections.Generic`

```csharp
namespace VisuFiscalHub.Domain.Common;

public interface IDomainEventSource
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
```

**Invariantes:**
- `DomainEvents` nunca é nulo — retorna lista vazia quando não há eventos.
- `ClearDomainEvents()` remove todos os eventos da lista interna. Chamado pelo `DomainEventsInterceptor` após serializar os eventos para `OutboxMessage`.

---

### 3.5 `Common/Entity.cs`

**Namespace:** `VisuFiscalHub.Domain.Common`

**Usings:** `System.Collections.Generic`

```csharp
namespace VisuFiscalHub.Domain.Common;

public abstract class Entity<TId> : IDomainEventSource
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity(TId id);
    protected Entity();  // para EF Core

    public TId Id { get; protected set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents();

    protected void AddDomainEvent(IDomainEvent domainEvent);

    public override bool Equals(object? obj);
    public override int GetHashCode();
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right);
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right);
}
```

**Invariantes:**
- Comparação por identidade (`Id`), nunca por referência.
- Dois `Entity<TId>` com o mesmo `Id` e mesmo tipo são iguais, independentemente do estado das propriedades.
- `_domainEvents` é encapsulado — acesso externo apenas por `DomainEvents` (read-only) e `AddDomainEvent` (protegido).

**Notas de implementação:**
- `Equals` deve verificar: `obj is Entity<TId> other && GetType() == other.GetType() && Id.Equals(other.Id)`.
- `GetHashCode` retorna `HashCode.Combine(GetType(), Id)` — inclui `GetType()` para diferenciar entidades de tipos diferentes com mesmo Id.
- Construtor sem parâmetros `protected Entity()` é necessário para o EF Core criar instâncias por reflexão. Não deve ser usado pelo código de aplicação.
- `TId : notnull` evita `Id` nulo em tempo de compilação.
- `_domainEvents = []` usa collection expression do C# 12 (disponível em .NET 10).

---

### 3.6 `Common/StronglyTypedId.cs`

**Namespace:** `VisuFiscalHub.Domain.Common`

**Usings:** nenhum

```csharp
namespace VisuFiscalHub.Domain.Common;

public abstract record struct StronglyTypedId<T>(T Value)
    where T : notnull;
```

**Invariantes:**
- `record struct` — imutável, comparação por valor, alocado na stack.
- Cada identificador concreto herda deste base e adiciona conversão implícita.

**Notas de implementação:**
- Usar `abstract record struct` permite que os identificadores concretos sejam record structs que herdam o comportamento de equality por valor.
- Conversões implícitas (`implicit operator`) são definidas nas subclasses, não aqui — não é possível definir `implicit operator` em classe base para o tipo da subclasse.
- Atenção: EF Core requer value converters para mapear `StronglyTypedId<Guid>` — configurados na Fase 2 (`Infrastructure/Persistence/Configurations/`).

---

### 3.7 `Identifiers/ClienteAppId.cs`, `TenantId.cs`, `DocumentoFiscalId.cs`, `DeliveryAttemptId.cs`

**Namespace:** `VisuFiscalHub.Domain.Identifiers`

**Usings:** `VisuFiscalHub.Domain.Common`

Padrão comum para todos os quatro identificadores (exemplo com `ClienteAppId`):

```csharp
namespace VisuFiscalHub.Domain.Identifiers;

public readonly record struct ClienteAppId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static ClienteAppId New() => new(Guid.NewGuid());
    public static ClienteAppId From(Guid value) => new(value);

    public static implicit operator Guid(ClienteAppId id) => id.Value;
    public static explicit operator ClienteAppId(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
```

Replicar para `TenantId`, `DocumentoFiscalId` e `DeliveryAttemptId` substituindo o nome.

**Invariantes:**
- `Guid.Empty` é tecnicamente válido como identificador mas nunca deve representar entidade real — a criação de entidades usa `ClienteAppId.New()` ou `ClienteAppId.From(guid)` recebido de fora.
- `readonly record struct` garante imutabilidade e comparação por valor sem alocação no heap.

**Notas de implementação:**
- `implicit operator Guid` permite passar `ClienteAppId` onde `Guid` é esperado sem cast explícito.
- `explicit operator ClienteAppId` exige cast explícito na direção `Guid → ClienteAppId` — previne conversões acidentais entre IDs de tipos diferentes.
- O método `New()` usa `Guid.NewGuid()` diretamente — uuid versão 4. Para `DocumentoFiscalId`, o handler de emissão passa o Guid gerado externamente via `From()`, pois o ID é retornado na resposta 202 antes de qualquer persistência.

---

### 3.8 `Enums/TipoDocumento.cs`

**Namespace:** `VisuFiscalHub.Domain.Enums`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum TipoDocumento
{
    NfCe = 65,
    NFe = 55,
    NFSe = 99
}
```

**Notas:** O valor numérico é o código do modelo fiscal. `NfCe = 65` é o modelo NFC-e; `NFe = 55` é NF-e. `NFSe = 99` é valor convencional (NFS-e não tem modelo federal padronizado). Fase 1 implementa apenas `NfCe`.

---

### 3.9 `Enums/StatusDocumento.cs`

**Namespace:** `VisuFiscalHub.Domain.Enums`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum StatusDocumento
{
    Criado = 1,
    Enfileirado = 2,
    Processando = 3,
    Autorizado = 4,
    Rejeitado = 5,
    Cancelado = 6,
    Falhou = 7,
    Denegado = 8
}
```

**Invariantes:**
- `Falhou` (7) e `Denegado` (8) são obrigatórios — ver `decisions.md` seção DA-11.
- `Falhou` = timeout de reconciliação após SEFAZ indisponível.
- `Denegado` = cStat=110 (CNPJ irregular junto ao SEFAZ) — distinto de `Rejeitado` (erro técnico no documento).

---

### 3.10 `Enums/RegimeTributario.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum RegimeTributario
{
    SimplesNacional = 1,
    SimplesNacionalExcesso = 2,
    RegimeNormal = 3
}
```

**Notas:** Valores correspondem ao campo `CRT` do XML NFC-e. Ver `decisions.md` seção RN-02 para regras tributárias por CRT.

---

### 3.11 `Enums/TipoEmissao.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum TipoEmissao
{
    Normal = 1,
    Contingencia = 9
}
```

**Notas:** `tpEmis` do XML. NFC-e aceita apenas 1 ou 9 (rejeição 714 para outros valores). Contingência offline (`9`) está fora de escopo do MVP, mas o enum é definido para validação do campo.

---

### 3.12 `Enums/AmbienteSefaz.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum AmbienteSefaz
{
    Producao = 1,
    Homologacao = 2
}
```

**Notas:** Valor `(int)AmbienteSefaz` é usado diretamente no cálculo do QR Code — `SHA1(chave + "|2|" + (int)ambiente + "|" + csc)`. Crítico: o cast inteiro deve produzir `1` ou `2`.

---

### 3.13 `Enums/TipoPagamento.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum TipoPagamento
{
    Dinheiro = 1,
    Cheque = 2,
    CartaoCredito = 3,
    CartaoDebito = 4,
    CreditoLoja = 5,
    ValeAlimentacao = 10,
    ValeRefeicao = 11,
    ValePresente = 12,
    ValeCombustivel = 13,
    PixDinamico = 17,
    PixEstatico = 20,
    Outros = 99
}
```

**Notas:** Valores conforme IT 2024.002. O XML serializa como dois dígitos com zero à esquerda (`01`, `02`, etc.) — a serialização é responsabilidade do `NfceXmlBuilder`, não do enum. O enum usa valores inteiros puros.

---

### 3.14 `Enums/ModalidadeFrete.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum ModalidadeFrete
{
    EmitenteCIF = 0,
    DestinatarioFOB = 1,
    TerceiroCIF = 2,
    TerceiroFOB = 3,
    ProprioRemetente = 4,
    ProprioDestinatario = 5,
    SemFrete = 9
}
```

**Notas:** Para NFC-e de varejo (consumidor final), o valor padrão é `SemFrete = 9`. Este enum é definido para completude do modelo.

---

### 3.15 `Enums/TipoIcms.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum TipoIcms
{
    CSOSN,
    CST
}
```

**Notas:** Discrimina se o `Tributo` usa CSOSN (Simples Nacional) ou CST (Regime Normal/Excesso). Usado pelo `NfceXmlBuilder` para decidir qual bloco XML gerar.

---

### 3.16 `Enums/OrigemMercadoria.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum OrigemMercadoria
{
    Nacional = 0,
    EstrangeiraImportacaoDireta = 1,
    EstrangeiraAdquiridaInterna = 2,
    NacionalConteudoImportacaoSuperior40 = 3,
    NacionalProcessosBasicos = 4,
    NacionalConteudoImportacaoInferior40 = 5,
    EstrangeiraImportacaoDiretaSemSimilar = 6,
    EstrangeiraAdquiridaInternaSemSimilar = 7,
    NacionalConteudoImportacao40A70 = 8
}
```

---

### 3.17 `Enums/CSOSN.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum CSOSN
{
    Csosn102 = 102,
    Csosn300 = 300,
    Csosn400 = 400,
    Csosn500 = 500,
    Csosn900 = 900
}
```

**Notas:** CRT 1 usa predominantemente `400`. CRT 2 usa `900` (com destaque de ICMS). Ver `decisions.md` seção 5.4.

---

### 3.18 `Enums/CstIcms.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum CstIcms
{
    Cst00 = 0,
    Cst10 = 10,
    Cst20 = 20,
    Cst30 = 30,
    Cst40 = 40,
    Cst41 = 41,
    Cst50 = 50,
    Cst51 = 51,
    Cst60 = 60,
    Cst70 = 70,
    Cst90 = 90
}
```

---

### 3.19 `Enums/CstPisCofins.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum CstPisCofins
{
    Cst01 = 1,
    Cst02 = 2,
    Cst03 = 3,
    Cst04 = 4,
    Cst05 = 5,
    Cst06 = 6,
    Cst07 = 7,   // PISNT / COFINSNT — Simples Nacional
    Cst08 = 8,
    Cst09 = 9,
    Cst49 = 49,
    Cst50 = 50,
    Cst51 = 51,
    Cst99 = 99
}
```

**Notas:** CRT 1 usa `Cst07` (sem cobrança de PIS/COFINS). CRT 2 e 3 usam `Cst01` (com alíquota). A serialização é "07" (dois dígitos) — responsabilidade do builder XML.

---

### 3.20 `Enums/TipoTentativa.cs`

```csharp
namespace VisuFiscalHub.Domain.Enums;

public enum TipoTentativa
{
    Envio,
    Consulta,
    Retry
}
```

**Notas:** Registrado em `DeliveryAttempt`. `Consulta` é quando o `HangfireJobProcessor` chama `ConsultarNfeAsync` após timeout — fluxo distinto do `Envio` direto. Ver `decisions.md` seção DA-06.

---

### 3.21 `ValueObjects/Cnpj.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Cnpj
{
    public string Valor { get; }  // sempre 14 dígitos, sem máscara

    private Cnpj(string valor);

    public static Result<Cnpj> Criar(string cnpj);

    private static string Normalizar(string cnpj);
    private static bool ValidarDigitosVerificadores(string cnpj);

    public override string ToString() => Valor;
}
```

**Invariantes:**
- `Valor` contém exatamente 14 dígitos numéricos, sem pontos, barra ou traço.
- `Criar` aceita entrada com ou sem máscara: `"11.222.333/0001-81"` e `"11222333000181"` produzem o mesmo `Cnpj`.
- `Criar` rejeita: comprimento diferente de 14 dígitos após normalização, dígitos verificadores incorretos, todos os dígitos iguais (ex: `"00000000000000"`, `"11111111111111"`).
- `Criar` retorna `Result.Failure(TenantErrors.CnpjInvalido)` para entrada inválida.

**Algoritmo de validação dos dígitos verificadores:**
1. Primeiro DV: soma ponderada dos 12 primeiros dígitos × pesos cíclicos `5,4,3,2,9,8,7,6,5,4,3,2` (da esquerda para direita). `resto = soma % 11`. Se `resto < 2 → DV = 0`; senão `DV = 11 - resto`. Comparar com posição 12 (índice 12).
2. Segundo DV: soma ponderada dos 13 primeiros dígitos × pesos cíclicos `6,5,4,3,2,9,8,7,6,5,4,3,2`. Mesma regra de `resto`. Comparar com posição 13 (índice 13).

**Notas de implementação:**
- `Normalizar` remove os caracteres `.`, `/`, `-` antes da validação.
- A validação de "todos iguais" deve cobrir todos os 14 padrões possíveis (`00000...` até `99999...`).
- `sealed record` garante comparação por valor via `Valor`. Dois `Cnpj` com o mesmo `Valor` são iguais sem override manual.

---

### 3.22 `ValueObjects/Cpf.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Cpf
{
    public string Valor { get; }  // sempre 11 dígitos, sem máscara

    private Cpf(string valor);

    public static Result<Cpf> Criar(string cpf);

    private static string Normalizar(string cpf);
    private static bool ValidarDigitosVerificadores(string cpf);

    public override string ToString() => Valor;
}
```

**Invariantes:**
- `Valor` contém exatamente 11 dígitos numéricos, sem pontos ou traço.
- Aceita entrada com ou sem máscara: `"123.456.789-09"` e `"12345678909"`.
- Rejeita todos os dígitos iguais (11 padrões: `"00000000000"` a `"99999999999"`).
- Rejeita dígitos verificadores incorretos.
- Retorna `Result.Failure(DocumentoFiscalErrors.CpfInvalido)` para entrada inválida.

**Algoritmo de validação:**
1. Primeiro DV: soma dos 9 primeiros × pesos `10,9,8,...,2`. `resto = (soma * 10) % 11`. Se `resto == 10 → DV = 0`; senão `DV = resto`. Comparar com índice 9.
2. Segundo DV: soma dos 10 primeiros × pesos `11,10,...,2`. Mesma regra.

**Notas:** CNPJ no destinatário é **proibido** para NFC-e (vigente desde novembro/2025). O `Cpf` é o único tipo de identificação do consumidor aceito — ver `decisions.md` seção RN-03. O CPF não é armazenado em coluna separada; aparece apenas no XML assinado.

---

### 3.23 `ValueObjects/ChaveAcesso.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Enums`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record ChaveAcesso
{
    public string Valor { get; }  // 44 dígitos

    private ChaveAcesso(string valor);

    /// <summary>
    /// Gera a chave de acesso de 44 dígitos calculando cDV via Módulo 11.
    /// </summary>
    /// <param name="cUF">Código IBGE da UF (2 dígitos, ex: 28 para SE)</param>
    /// <param name="aamm">Ano e mês de emissão (4 dígitos, ex: "2605")</param>
    /// <param name="cnpj">CNPJ sem máscara (14 dígitos)</param>
    /// <param name="mod">Modelo do documento (2 dígitos, ex: 65 para NFC-e)</param>
    /// <param name="serie">Série da nota (3 dígitos, zero-padded)</param>
    /// <param name="nNF">Número sequencial da nota (9 dígitos, zero-padded)</param>
    /// <param name="tpEmis">Tipo de emissão (1 dígito)</param>
    /// <param name="cNF">8 dígitos aleatórios gerados externamente via RandomNumberGenerator</param>
    public static Result<ChaveAcesso> Gerar(
        int cUF,
        string aamm,
        string cnpj,
        int mod,
        string serie,
        string nNF,
        TipoEmissao tpEmis,
        string cNF);

    private static int CalcularCDV(string quarentaTresDígitos);

    public static Result<ChaveAcesso> From(string chave44Digitos);

    public override string ToString() => Valor;
}
```

**Invariantes:**
- `Valor` tem exatamente 44 dígitos numéricos.
- Os primeiros 43 dígitos formam a chave sem o dígito verificador.
- O 44° dígito (`cDV`) é calculado por Módulo 11 sobre os 43 primeiros.
- `cNF` deve ser passado como 8 dígitos decimais gerados externamente com `RandomNumberGenerator.GetBytes(4)` — **nunca** `Random.Shared`. A responsabilidade de segurança criptográfica fica no chamador (handler), não no value object.

**Algoritmo cDV (Módulo 11) — referência obrigatória:**
```
Entrada: string de 43 dígitos
Pesos: ciclo de 2 a 9 da direita para a esquerda (dígito mais à direita × 2, próximo × 3, ...)
Soma = Σ (dígito[i] × peso[i])
Resto = Soma % 11
cDV = (resto < 2) ? 0 : (11 - resto)
```

**Valor de teste (MOC 7.0 Seção 4.1.6) — hardcodar no teste:**
- 43 dígitos: `3509061420016714006512500100000180010000009`
- cDV esperado: `7`
- Chave completa: `35090614200167140065125001000001800100000097`

**Notas de implementação:**
- `Gerar` monta os 43 dígitos concatenando os campos formatados (zero-padded). A formatação deve usar `PadLeft`:
  - `cUF`: `cUF.ToString().PadLeft(2, '0')`
  - `serie`: `serie.PadLeft(3, '0')`
  - `nNF`: `nNF.PadLeft(9, '0')`
  - `tpEmis`: `((int)tpEmis).ToString()`
  - `cNF`: `cNF.PadLeft(8, '0')`
- `From` é usado quando a chave já está formada (ex: ao carregar do banco) — valida comprimento e que todos são dígitos numéricos.
- A validação de cDV não é refeita em `From` — confia-se nos dados do banco.

---

### 3.24 `ValueObjects/QrCode.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Enums`, `System.Security.Cryptography`, `System.Text`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record QrCode
{
    public string UrlCompleta { get; }  // URL final com parâmetros, sem o CSC

    private QrCode(string urlCompleta);

    /// <summary>
    /// Gera a URL do QR Code conforme NT 2019.001 v1.50.
    /// Fórmula do hash: SHA1(chaveAcesso + "|2|" + (int)ambiente + "|" + csc)
    /// URL: {urlConsultaSefaz}?p={chaveAcesso}|2|{(int)ambiente}|{cHashQRCode}
    /// O "|2|" é a versão do QR Code (literal fixo) — NÃO é tpAmb.
    /// O CSC nunca aparece na URL final.
    /// </summary>
    public static Result<QrCode> Gerar(
        ChaveAcesso chaveAcesso,
        AmbienteSefaz ambiente,
        string csc,
        string urlConsultaSefaz);

    public override string ToString() => UrlCompleta;
}
```

**Invariantes:**
- A string passada ao SHA-1 tem a forma: `{44dígitos}|2|{1ou2}|{csc}` — onde `|2|` é literal.
- O `csc` aparece apenas na string de entrada do SHA-1 — nunca na URL final.
- `urlConsultaSefaz` deve terminar sem `/` — a URL final é `{urlConsultaSefaz}?p=...`.
- `csc` não pode ser nulo ou vazio (retorna `Result.Failure`).

**Fórmula completa:**
```
stringParaHash = chaveAcesso.Valor + "|2|" + ((int)ambiente).ToString() + "|" + csc
cHashQRCode = SHA1(UTF8(stringParaHash)) como hex lowercase
urlCompleta = urlConsultaSefaz + "?p=" + chaveAcesso.Valor + "|2|" + ((int)ambiente).ToString() + "|" + cHashQRCode
```

**Valor de teste (sandbox AM — hardcodar no `[InlineData]` do teste):**
```
chaveAcesso = "35090614200167140065125001000001800100000097"
tpAmb = 2 (Homologacao)
csc = "0123456789"
stringParaHash = "35090614200167140065125001000001800100000097|2|2|0123456789"
cHashQRCode = <calcular com: echo -n "35090614200167140065125001000001800100000097|2|2|0123456789" | sha1sum>
```

**Notas de implementação:**
- SHA-1 com `SHA1.HashData(Encoding.UTF8.GetBytes(stringParaHash))` — API estática disponível em .NET 10.
- Converter bytes para hex com `Convert.ToHexStringLower(hash)` (disponível a partir do .NET 9).
- Embora SHA-1 seja fraco para uso geral, é **obrigatório** pela NT 2019.001 do SEFAZ — ver `decisions.md` seção DA-04.

---

### 3.25 `ValueObjects/Endereco.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Endereco(
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    int CodigoMunicipio,
    string Uf,
    string Cep,
    string CodigoPais,
    string? Telefone)
{
    public static Result<Endereco> Criar(
        string logradouro,
        string numero,
        string? complemento,
        string bairro,
        string municipio,
        int codigoMunicipio,
        string uf,
        string cep,
        string codigoPais = "1058",
        string? telefone = null);
}
```

**Invariantes:**
- `CodigoPais` padrão é `"1058"` (Brasil).
- `Cep` deve ser exatamente 8 dígitos numéricos (sem traço). `Criar` normaliza removendo `-`.
- `Uf` deve ser 2 letras maiúsculas (ex: `"SE"`, `"SP"`).
- `CodigoMunicipio` deve ser 7 dígitos (código IBGE do município). Validar `>= 1000000 && <= 9999999`.

**Notas de implementação:**
- Mapeado via `OwnsOne` no EF Core (Fase 2). As propriedades do record se tornam colunas com prefixo `end_` na tabela `tenants`.
- `Criar` retorna `Result.Failure(TenantErrors.EnderecoInvalido)` para campos obrigatórios ausentes ou inválidos.

---

### 3.26 `ValueObjects/Produto.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Enums`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Produto(
    string CodigoProduto,
    string Descricao,
    string Ncm,
    string? Cest,
    string CfopSaida,
    string UnidadeComercial,
    decimal Quantidade,
    decimal ValorUnitario,
    decimal ValorDesconto,
    OrigemMercadoria OrigemMercadoria)
{
    public decimal ValorBruto => Quantidade * ValorUnitario;
    public decimal ValorLiquido => ValorBruto - ValorDesconto;

    public static Result<Produto> Criar(
        string codigoProduto,
        string descricao,
        string ncm,
        string? cest,
        string cfopSaida,
        string unidadeComercial,
        decimal quantidade,
        decimal valorUnitario,
        decimal valorDesconto,
        OrigemMercadoria origemMercadoria);
}
```

**Invariantes:**
- `Ncm` deve ter exatamente 8 dígitos numéricos.
- `Quantidade > 0` e `ValorUnitario > 0`.
- `ValorDesconto >= 0` e `ValorDesconto < ValorBruto` (desconto não pode exceder o valor bruto do item).
- `CfopSaida` deve ser 4 dígitos numéricos (ex: `"5102"`).

---

### 3.27 `ValueObjects/Tributo.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Enums`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Tributo(
    TipoIcms TipoIcms,
    int CsosnOuCst,       // valor numérico do CSOSN ou CST selecionado
    decimal AliquotaIcms,
    decimal BaseCalculoIcms,
    decimal ValorIcms,
    CstPisCofins CstPis,
    decimal BaseCalculoPis,
    decimal AliquotaPis,
    decimal ValorPis,
    CstPisCofins CstCofins,
    decimal BaseCalculoCofins,
    decimal AliquotaCofins,
    decimal ValorCofins);
```

**Invariantes:**
- Para `TipoIcms = CSOSN` (CRT 1/2): `CsosnOuCst` é um valor de `CSOSN`. Para CRT 1 com CSOSN 400/102: `AliquotaIcms = 0`, `BaseCalculoIcms = 0`, `ValorIcms = 0`.
- Para CRT 1: `CstPis = Cst07`, `ValorPis = 0`, `ValorCofins = 0`.
- Todos os valores decimais são `>= 0`.

---

### 3.28 `ValueObjects/Pagamento.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Enums`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record Pagamento(TipoPagamento TipoPagamento, decimal Valor)
{
    public static Result<Pagamento> Criar(TipoPagamento tipoPagamento, decimal valor);

    public static Result ValidarTotalPagamentos(
        IEnumerable<Pagamento> pagamentos,
        decimal valorTotalNota);
}
```

**Invariantes:**
- `Valor > 0`.
- `ValidarTotalPagamentos` verifica que a soma dos valores dos pagamentos iguala `valorTotalNota` com tolerância de R$ 0,01.

---

### 3.29 `ValueObjects/ConfiguracaoFiscal.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Enums`

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record ConfiguracaoFiscal(
    RegimeTributario Crt,
    string Serie,
    AmbienteSefaz Ambiente,
    int UfCodigo)
{
    public static Result<ConfiguracaoFiscal> Criar(
        RegimeTributario crt,
        string serie,
        AmbienteSefaz ambiente,
        int ufCodigo);
}
```

**Invariantes:**
- `Serie` deve corresponder ao regex `^[0-9]{1,3}$` — validação obrigatória para prevenir SQL injection no DDL de criação de sequences (ver `decisions.md` seção RN-05).
- `UfCodigo` deve ser um código IBGE válido de UF brasileira (lista de 27 valores).
- **Não inclui** `Csc` nem `CIdToken` — estes ficam como campos diretos no `Tenant` porque envolvem criptografia na camada Infrastructure (ver `decisions.md` seção 3 — nota sobre `ConfiguracaoFiscal`).

**Notas de implementação:**
- Mapeado via `OwnsOne` no EF Core (Fase 2). Colunas físicas: `crt`, `serie`, `ambiente`, `uf_codigo` na tabela `tenants`.

---

### 3.30 `ValueObjects/CertificadoDigital.cs`

**Namespace:** `VisuFiscalHub.Domain.ValueObjects`

**Usings:** nenhum

```csharp
namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record CertificadoDigital(DateTime VencimentoEm, byte[] PfxBytes);
```

**Notas:** Value object retornado por `ITenantCertificateProvider`. `PfxBytes` contém o PFX **decriptografado** — não persistido, apenas em memória durante o uso. Descartado com `using` após criar `X509Certificate2`. Ver `decisions.md` seção DA-05.

---

### 3.31 `Entities/ClienteApp.cs`

**Namespace:** `VisuFiscalHub.Domain.Entities`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Identifiers`

```csharp
namespace VisuFiscalHub.Domain.Entities;

public sealed class ClienteApp : Entity<ClienteAppId>
{
    private ClienteApp();  // EF Core

    private ClienteApp(
        ClienteAppId id,
        string name,
        string clientId,
        string clientSecretHash,
        string? webhookUrl,
        byte[]? webhookSecretCriptografado,
        DateTime createdAt);

    public string Name { get; private set; }
    public string ClientId { get; private set; }
    public string ClientSecretHash { get; private set; }
    public string? WebhookUrl { get; private set; }
    public byte[]? WebhookSecretCriptografado { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Result<ClienteApp> Criar(
        string name,
        string clientId,
        string clientSecretHash,
        string? webhookUrl = null,
        byte[]? webhookSecretCriptografado = null);

    public void Desativar();

    public void AtualizarClientSecretHash(string novoHash);

    public void AtualizarWebhookSecret(byte[] webhookSecretCriptografado);
}
```

**Invariantes:**
- `Name` não pode ser nulo ou vazio.
- `ClientId` não pode ser nulo ou vazio; unicidade é garantida por constraint de banco (não pelo domínio).
- `ClientSecretHash` contém o hash PBKDF2-SHA256 do secret — nunca o valor em texto claro.
- `WebhookUrl` se presente deve usar esquema `https` — validação feita no handler/validator da Application layer.
- `WebhookSecretCriptografado` é `byte[]` criptografado com AES-256-GCM. `null` quando o `WebhookUrl` não foi configurado.
- `IsActive` começa como `true` na criação.
- `CreatedAt` é UTC.

**Notas de implementação:**
- O `Id` é gerado na factory (`ClienteAppId.New()`).
- A geração e hash do `client_secret`, geração do `webhookSecret` e criptografia são responsabilidade do `CreateClienteAppCommandHandler` — não do domain.
- `Criar` usa factory method estático para garantir invariantes na construção.

---

### 3.32 `Entities/Tenant.cs`

**Namespace:** `VisuFiscalHub.Domain.Entities`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Identifiers`, `VisuFiscalHub.Domain.ValueObjects`, `VisuFiscalHub.Domain.Events`

```csharp
namespace VisuFiscalHub.Domain.Entities;

public sealed class Tenant : Entity<TenantId>
{
    private Tenant();  // EF Core

    private Tenant(
        TenantId id,
        ClienteAppId clienteAppId,
        Cnpj cnpj,
        string razaoSocial,
        string? nomeFantasia,
        ConfiguracaoFiscal configuracaoFiscal,
        Endereco endereco,
        DateTime createdAt);

    public ClienteAppId ClienteAppId { get; private set; }
    public Cnpj Cnpj { get; private set; }
    public string RazaoSocial { get; private set; }
    public string? NomeFantasia { get; private set; }
    public ConfiguracaoFiscal ConfiguracaoFiscal { get; private set; }
    public Endereco Endereco { get; private set; }
    public byte[]? Csc { get; private set; }                           // AES-GCM criptografado
    public string? CIdToken { get; private set; }                      // 6 dígitos com zeros
    public byte[]? CertificadoPfxCriptografado { get; private set; }  // AES-GCM criptografado
    public byte[]? CertificadoSenhaCriptografada { get; private set; } // AES-GCM criptografado
    public DateTime? CertificadoVencimento { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static Result<Tenant> Criar(
        ClienteAppId clienteAppId,
        Cnpj cnpj,
        string razaoSocial,
        string? nomeFantasia,
        ConfiguracaoFiscal configuracaoFiscal,
        Endereco endereco);

    public void AtualizarCertificado(
        byte[] pfxCriptografado,
        byte[] senhaCriptografada,
        DateTime vencimento);

    public void AtualizarCsc(byte[] cscCriptografado, string cIdToken);

    public void Desativar();
}
```

**Invariantes:**
- `RazaoSocial` não pode ser nulo ou vazio.
- `CIdToken` quando presente deve ter exatamente 6 caracteres numéricos (ex: `"000001"`).
- `CertificadoVencimento` não pode ser no passado ao chamar `AtualizarCertificado` — validação feita no handler, não no domain (o domain não tem acesso a `TimeProvider`).
- `Criar` publica `TenantProvisionadoEvent` via `AddDomainEvent`.
- `Csc`, `CertificadoPfxCriptografado` e `CertificadoSenhaCriptografada` são `byte[]` — nunca texto claro. A criptografia é feita pelo `CertificateEncryptionService` na Infrastructure antes de chamar os métodos do domain.

**Notas de implementação:**
- `Criar` gera `TenantId.New()` e publica `TenantProvisionadoEvent(Id, ClienteAppId, Cnpj)`.
- O `Cnpj` value object garante validação — `Criar` recebe um `Cnpj` já validado, não uma string raw.

---

### 3.33 `Entities/DocumentoFiscal.cs`

**Namespace:** `VisuFiscalHub.Domain.Entities`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Enums`, `VisuFiscalHub.Domain.Identifiers`, `VisuFiscalHub.Domain.ValueObjects`, `VisuFiscalHub.Domain.Events`

```csharp
namespace VisuFiscalHub.Domain.Entities;

public sealed class DocumentoFiscal : Entity<DocumentoFiscalId>
{
    private readonly List<ItemDocumento> _items = [];
    private readonly List<Pagamento> _pagamentos = [];

    private DocumentoFiscal();  // EF Core

    private DocumentoFiscal(
        DocumentoFiscalId id,
        TenantId tenantId,
        string idempotencyKey,
        TipoDocumento tipo,
        ChaveAcesso chaveAcesso,
        long numero,
        string serie,
        List<ItemDocumento> items,
        List<Pagamento> pagamentos,
        DateTime createdAt);

    public TenantId TenantId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public TipoDocumento Tipo { get; private set; }
    public ChaveAcesso ChaveAcesso { get; private set; }
    public long Numero { get; private set; }
    public string Serie { get; private set; }
    public StatusDocumento Status { get; private set; }
    public string? XmlAssinado { get; private set; }
    public string? Protocolo { get; private set; }
    public QrCode? QrCode { get; private set; }
    public string? MotivoRejeicao { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? AuthorizedAt { get; private set; }

    public IReadOnlyList<ItemDocumento> Items => _items.AsReadOnly();
    public IReadOnlyList<Pagamento> Pagamentos => _pagamentos.AsReadOnly();

    public static Result<DocumentoFiscal> Criar(
        DocumentoFiscalId id,
        TenantId tenantId,
        string idempotencyKey,
        TipoDocumento tipo,
        ChaveAcesso chaveAcesso,
        long numero,
        string serie,
        IEnumerable<ItemDocumento> items,
        IEnumerable<Pagamento> pagamentos);

    // State machine — todas retornam Result, nunca lançam exceção
    public Result Enfileirar();
    public Result IniciarProcessamento();
    public Result Autorizar(string protocolo, string xmlAssinado, QrCode qrCode, DateTime authorizedAt);
    public Result Rejeitar(string motivo);
    public Result Cancelar(TimeProvider timeProvider);
    public Result Falhar();
    public Result Denegar(string motivo);
}
```

**State Machine — Transições Válidas:**
```
Criado       → Enfileirar()          → Enfileirado
Enfileirado  → IniciarProcessamento() → Processando
Processando  → Autorizar(...)        → Autorizado    (publica DocumentoFiscalAutorizadoEvent)
Processando  → Rejeitar(motivo)      → Rejeitado     (publica DocumentoFiscalRejeitadoEvent)
Processando  → Falhar()              → Falhou        (publica DocumentoFiscalFalhouEvent)
Processando  → Denegar(motivo)       → Denegado      (publica DocumentoFiscalDenegadoEvent)
Autorizado   → Cancelar(tp)          → Cancelado     (publica DocumentoFiscalCanceladoEvent, se dentro prazo)
```

**Regra de cancelamento — boundary estrito:**
```
Condição para PERMITIR cancelamento: AuthorizedAt + 30min > timeProvider.GetUtcNow()
Equivalente: timeProvider.GetUtcNow() < AuthorizedAt + 30min
```
Portanto: `authorizedAt.AddMinutes(30) == utcNow` → **NÃO** pode cancelar (boundary `<` estrito).

**Invariantes:**
- Toda transição inválida (status de origem errado) retorna `Result.Failure(DocumentoFiscalErrors.TransicaoInvalida)`.
- `Autorizar` não pode ser chamado com `protocolo` nulo ou vazio — retorna `Result.Failure`.
- `Cancelar` retorna `Result.Failure(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado)` se fora do prazo.
- `_items` e `_pagamentos` usam backing fields com nomes idênticos para o `HasField` do EF Core.
- `XmlAssinado` é obrigatório em `Autorizar` — o CPF do consumidor está no XML (não em coluna separada).

**Notas de implementação:**
- `DocumentoFiscalId` é passado como parâmetro para `Criar` — o handler gera o ID antes de criar o documento, pois o ID é retornado na resposta 202 antes da persistência.
- O construtor privado sem parâmetros usa `private DocumentoFiscal()` — necessário para EF Core. Neste construtor, inicializar `_items = []` e `_pagamentos = []` para evitar NullReferenceException.
- `timeProvider.GetUtcNow()` retorna `DateTimeOffset`. Comparar `AuthorizedAt` (DateTime UTC) com `dateTimeOffset.UtcDateTime` para consistência.

---

### 3.34 `Entities/ItemDocumento.cs`

**Namespace:** `VisuFiscalHub.Domain.Entities`

**Usings:** `VisuFiscalHub.Domain.ValueObjects`

```csharp
namespace VisuFiscalHub.Domain.Entities;

public sealed class ItemDocumento
{
    private ItemDocumento();  // EF Core

    public ItemDocumento(int numero, Produto produto, Tributo tributo);

    public int Numero { get; private set; }
    public Produto Produto { get; private set; }
    public Tributo Tributo { get; private set; }
    public decimal ValorTotal => Produto.ValorLiquido;
}
```

**Invariantes:**
- Não tem identidade própria — é owned entity do `DocumentoFiscal`.
- `Numero` é o número sequencial do item na nota (1, 2, 3...).
- `ValorTotal` é calculado; não deve ser persistido diretamente — EF Core mapeará `Produto.ValorLiquido` se necessário.

**Notas de implementação:**
- `ItemDocumento` não herda de `Entity<TId>` — não tem `Id` próprio. É owned entity mapeada via `OwnsMany` no EF Core.
- Não tem domain events — é parte do aggregate `DocumentoFiscal`.

---

### 3.35 `Entities/DeliveryAttempt.cs`

**Namespace:** `VisuFiscalHub.Domain.Entities`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Enums`, `VisuFiscalHub.Domain.Identifiers`

```csharp
namespace VisuFiscalHub.Domain.Entities;

public sealed class DeliveryAttempt : Entity<DeliveryAttemptId>
{
    private DeliveryAttempt();  // EF Core

    public DeliveryAttempt(
        DeliveryAttemptId id,
        DocumentoFiscalId documentoFiscalId,
        TipoTentativa tipoTentativa,
        DateTime attemptedAt,
        bool success,
        string? responseCode,
        string? responseMessage,
        long elapsedMs);

    public DocumentoFiscalId DocumentoFiscalId { get; private set; }
    public TipoTentativa TipoTentativa { get; private set; }
    public DateTime AttemptedAt { get; private set; }
    public bool Success { get; private set; }
    public string? ResponseCode { get; private set; }
    public string? ResponseMessage { get; private set; }
    public long ElapsedMs { get; private set; }
}
```

**Invariantes:**
- `DocumentoFiscalId` é a FK para o documento fiscal ao qual esta tentativa pertence.
- `AttemptedAt` é UTC.
- `ElapsedMs` é o tempo de resposta em milissegundos (para diagnóstico de performance).

---

### 3.36 `Events/DocumentoFiscalAutorizadoEvent.cs`

**Namespace:** `VisuFiscalHub.Domain.Events`

**Usings:** `VisuFiscalHub.Domain.Common`, `VisuFiscalHub.Domain.Identifiers`

```csharp
namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalAutorizadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    string ChaveAcesso,
    string Protocolo,
    DateTime AuthorizedAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

**Notas:** `ClienteAppId` é incluído para que o `DocumentoFiscalAutorizadoEventHandler` possa chamar `IWebhookDeliveryService` sem consulta adicional ao banco. O handler de webhook precisa do `ClienteAppId` para carregar o `ClienteApp` e decriptografar o `webhookSecret`.

---

### 3.37 `Events/DocumentoFiscalRejeitadoEvent.cs`

```csharp
namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalRejeitadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    string MotivoRejeicao) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

---

### 3.38 `Events/DocumentoFiscalCanceladoEvent.cs`

```csharp
namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalCanceladoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    DateTime CanceladoAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

---

### 3.39 `Events/DocumentoFiscalFalhouEvent.cs`

```csharp
namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalFalhouEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    DateTime FalhouAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

---

### 3.40 `Events/DocumentoFiscalDenegadoEvent.cs`

```csharp
namespace VisuFiscalHub.Domain.Events;

public sealed record DocumentoFiscalDenegadoEvent(
    DocumentoFiscalId DocumentoFiscalId,
    TenantId TenantId,
    string Cnpj,
    string XMotivo) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

**Notas:** `Cnpj` é o CNPJ do emitente (string 14 dígitos) — incluído para log de nível `Critical` no `DocumentoFiscalDenegadoEventHandler`. Ver `decisions.md` seção DA-11.

---

### 3.41 `Events/TenantProvisionadoEvent.cs`

```csharp
namespace VisuFiscalHub.Domain.Events;

public sealed record TenantProvisionadoEvent(
    TenantId TenantId,
    ClienteAppId ClienteAppId,
    string Cnpj) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

---

### 3.42 `Errors/ClienteAppErrors.cs`

**Namespace:** `VisuFiscalHub.Domain.Errors`

**Usings:** `VisuFiscalHub.Domain.Common`

```csharp
namespace VisuFiscalHub.Domain.Errors;

public static class ClienteAppErrors
{
    public static readonly Error NaoEncontrado =
        new("ClienteApp.NaoEncontrado", "ClienteApp não encontrado.");

    public static readonly Error ClientIdJaExiste =
        new("ClienteApp.ClientIdJaExiste", "O ClientId informado já está em uso.");

    public static readonly Error Inativo =
        new("ClienteApp.Inativo", "O ClienteApp está inativo.");
}
```

---

### 3.43 `Errors/TenantErrors.cs`

```csharp
namespace VisuFiscalHub.Domain.Errors;

public static class TenantErrors
{
    public static readonly Error NaoEncontrado =
        new("Tenant.NaoEncontrado", "Tenant não encontrado.");

    public static readonly Error CnpjInvalido =
        new("Tenant.CnpjInvalido", "O CNPJ informado é inválido.");

    public static readonly Error CnpjJaCadastrado =
        new("Tenant.CnpjJaCadastrado", "Já existe um Tenant com este CNPJ neste ClienteApp.");

    public static readonly Error NaoPertenceAoClienteApp =
        new("Tenant.NaoPertenceAoClienteApp", "O Tenant não pertence ao ClienteApp autenticado.");

    public static readonly Error SemCertificado =
        new("Tenant.SemCertificado", "O Tenant não possui certificado digital configurado.");

    public static readonly Error Inativo =
        new("Tenant.Inativo", "O Tenant está inativo.");

    public static readonly Error EnderecoInvalido =
        new("Tenant.EnderecoInvalido", "O endereço do Tenant é inválido.");
}
```

---

### 3.44 `Errors/DocumentoFiscalErrors.cs`

```csharp
namespace VisuFiscalHub.Domain.Errors;

public static class DocumentoFiscalErrors
{
    public static readonly Error NaoEncontrado =
        new("DocumentoFiscal.NaoEncontrado", "Documento fiscal não encontrado.");

    public static readonly Error IdempotencyKeyJaUsada =
        new("DocumentoFiscal.IdempotencyKeyJaUsada", "A chave de idempotência já foi utilizada.");

    public static readonly Error StatusInvalidoParaOperacao =
        new("DocumentoFiscal.StatusInvalidoParaOperacao", "O status atual do documento não permite esta operação.");

    public static readonly Error TransicaoInvalida =
        new("DocumentoFiscal.TransicaoInvalida", "Transição de status inválida para o documento fiscal.");

    public static readonly Error TotalPagamentosInvalido =
        new("DocumentoFiscal.TotalPagamentosInvalido", "O total dos pagamentos não corresponde ao valor da nota.");

    public static readonly Error ValorTotalInvalido =
        new("DocumentoFiscal.ValorTotalInvalido", "O valor total da nota não corresponde ao somatório dos itens.");

    public static readonly Error PrazoDeCancelamentoExpirado =
        new("DocumentoFiscal.PrazoDeCancelamentoExpirado", "O prazo de 30 minutos para cancelamento foi expirado.");

    public static readonly Error CpfInvalido =
        new("DocumentoFiscal.CpfInvalido", "O CPF do consumidor é inválido.");
}
```

---

### 3.45 `Interfaces/IClienteAppRepository.cs`

**Namespace:** `VisuFiscalHub.Domain.Interfaces`

**Usings:** `VisuFiscalHub.Domain.Entities`, `VisuFiscalHub.Domain.Identifiers`

```csharp
namespace VisuFiscalHub.Domain.Interfaces;

public interface IClienteAppRepository
{
    Task<ClienteApp?> GetByClientIdAsync(string clientId, CancellationToken ct = default);
    Task<ClienteApp?> GetByIdAsync(ClienteAppId id, CancellationToken ct = default);
    Task AddAsync(ClienteApp clienteApp, CancellationToken ct = default);
}
```

**Invariantes:**
- Apenas aggregate root. Sem métodos de atualização direta — a entidade é carregada, mutada via métodos de domínio, e persistida via `IUnitOfWork.SaveChangesAsync`.
- `GetBy*` retorna `null` quando não encontrado — não lança exceção.

---

### 3.46 `Interfaces/ITenantRepository.cs`

**Namespace:** `VisuFiscalHub.Domain.Interfaces`

**Usings:** `VisuFiscalHub.Domain.Entities`, `VisuFiscalHub.Domain.Identifiers`, `VisuFiscalHub.Domain.ValueObjects`

```csharp
namespace VisuFiscalHub.Domain.Interfaces;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(TenantId id, CancellationToken ct = default);
    Task<Tenant?> GetByCnpjAsync(Cnpj cnpj, ClienteAppId clienteAppId, CancellationToken ct = default);
    Task<IReadOnlyList<Tenant>> GetByClienteAppIdAsync(
        ClienteAppId clienteAppId,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    Task<long> GetNextNumeracaoAsync(TenantId tenantId, string serie, CancellationToken ct = default);
}
```

**Notas:**
- `GetNextNumeracaoAsync` executa `SELECT nextval('seq_nfe_{tenantId:N}_{serie}')` no PostgreSQL. A série é validada com regex `^[0-9]{1,3}$` no `CreateTenantCommandHandler` antes da interpolação — a interface não repete esta validação.
- `GetByClienteAppIdAsync` tem paginação explícita — nunca retorna lista não paginada.

---

### 3.47 `Interfaces/IDocumentoFiscalRepository.cs`

**Namespace:** `VisuFiscalHub.Domain.Interfaces`

**Usings:** `VisuFiscalHub.Domain.Entities`, `VisuFiscalHub.Domain.Identifiers`

```csharp
namespace VisuFiscalHub.Domain.Interfaces;

public interface IDocumentoFiscalRepository
{
    Task<DocumentoFiscal?> GetByIdAsync(DocumentoFiscalId id, CancellationToken ct = default);
    Task<DocumentoFiscal?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        TenantId tenantId,
        CancellationToken ct = default);
    Task AddAsync(DocumentoFiscal documento, CancellationToken ct = default);
    Task<IReadOnlyList<DocumentoFiscal>> GetProcessandoAntigoAsync(
        TimeSpan timeout,
        CancellationToken ct = default);
}
```

**Notas:**
- `GetProcessandoAntigoAsync` retorna documentos com `Status = Processando` e `CreatedAt < utcNow - timeout`. Usado pelo `ReconciliacaoJobProcessor` (Fase 7) para detectar documentos travados.
- Sem método `Update` explícito — EF Core rastreia mudanças via Change Tracker. `IUnitOfWork.SaveChangesAsync` persiste as mudanças.

---

### 3.48 `Interfaces/IUnitOfWork.cs`

**Namespace:** `VisuFiscalHub.Domain.Interfaces`

```csharp
namespace VisuFiscalHub.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

**Notas:** Interface mínima. A implementação concreta (`UnitOfWork : IUnitOfWork`) é um wrapper sobre `ApplicationDbContext.SaveChangesAsync` na Infrastructure. O `DomainEventsInterceptor` é disparado automaticamente nesta chamada via EF Core interceptor.

---

### 3.49 Application Layer Interfaces (`Application/Common/Interfaces/`)

Estas interfaces são criadas no projeto `VisuFiscalHub.Application`, mas são documentadas aqui por sua relação direta com os tipos do Domain.

#### `IDocumentJobQueue.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IDocumentJobQueue
{
    void EnqueueProcessing(DocumentoFiscalId id);
}
```

**Notas:** Abstrai o Hangfire `IBackgroundJobClient`. Parâmetro é `DocumentoFiscalId` — **nunca** `Guid` diretamente — para type-safety. A implementação concreta `HangfireDocumentJobQueue` é registrada na Infrastructure.

#### `ISefazClient.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ISefazClient
{
    Task<SefazRetorno> SubmeterAutorizacaoAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        CancellationToken ct);

    Task<SefazConsultaRetorno> ConsultarNfeAsync(
        string chaveAcesso,
        Tenant tenant,
        CancellationToken ct);
}
```

**Notas:** Dois métodos — obrigatório. `ConsultarNfeAsync` é chamado antes de todo retry após timeout (ver `decisions.md` seção DA-06 e RNF-01). `SefazRetorno` e `SefazConsultaRetorno` são records definidos na Fase 7.

#### `ICurrentUserContext.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICurrentUserContext
{
    ClienteAppId ClienteAppId { get; }
    TenantId TenantId { get; }
}
```

**Notas:** Expõe os dois campos obrigatórios — `ClienteAppId` (do claim `sub` do JWT) e `TenantId` (do `HttpContext.Items["TenantContext"]`). Handlers injetam por construtor — nunca acessam `IHttpContextAccessor` diretamente na Application layer.

#### `ITenantCertificateProvider.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITenantCertificateProvider
{
    Task<CertificadoDigital> GetCertificateAsync(TenantId tenantId, CancellationToken ct = default);
}
```

#### `ICertificateEncryptionService.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICertificateEncryptionService
{
    byte[] Encrypt(byte[] data);
    byte[] Decrypt(byte[] encrypted);

    // Sobrecargas para CSC e webhookSecret (string)
    byte[] EncryptString(string text);
    string DecryptToString(byte[] encrypted);
}
```

#### `ITributacaoCalculator.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITributacaoCalculator
{
    Tributo CalcularParaCrt1(Produto produto, CSOSN csosn = CSOSN.Csosn400);
    Tributo CalcularParaCrt2(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins);
    Tributo CalcularParaCrt3(Produto produto, decimal aliquotaIcms, decimal aliquotaPis, decimal aliquotaCofins);
}
```

#### `INfceXmlBuilder.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface INfceXmlBuilder
{
    System.Xml.XmlDocument Construir(DocumentoFiscal documento, Tenant tenant);
}
```

#### `IQrCodeGenerator.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IQrCodeGenerator
{
    string Gerar(
        ChaveAcesso chaveAcesso,
        AmbienteSefaz ambiente,
        string csc,
        string cIdToken,
        string urlConsultaSefaz);
}
```

#### `IWebhookDeliveryService.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IWebhookDeliveryService
{
    Task DeliverAsync(
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
        CancellationToken ct);
}
```

#### `ITokenService.cs`

```csharp
namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateToken(ClienteApp clienteApp);
}
```

---

## 4. Dependências entre Tipos

```
┌──────────────────────────────────────────────────────────────────────┐
│                     DOMAIN LAYER — Fase 1                            │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Common/                                                             │
│  ┌─────────────┐   ┌──────────────┐   ┌───────────────────────┐     │
│  │    Error    │◄──│    Result    │   │   IDomainEventSource  │     │
│  └─────────────┘   └──────────────┘   └───────────┬───────────┘     │
│                                                   │implements       │
│  ┌──────────────────────────────────────┐         │                 │
│  │         IDomainEvent                 │   ┌─────┴──────────┐      │
│  └──────────────────────────────────────┘   │  Entity<TId>   │      │
│              ▲ implemented by               └────────────────┘      │
│              │                                      ▲               │
│  Events/     │                                      │ inherits      │
│  ┌──────────────────────┐           Entities/       │               │
│  │ DocFiscalAutorizado  │           ┌────────────────────────┐      │
│  │ DocFiscalRejeitado   │           │      ClienteApp        │      │
│  │ DocFiscalCancelado   │    uses   ├────────────────────────┤      │
│  │ DocFiscalFalhou      │◄──────────│      Tenant            │      │
│  │ DocFiscalDenegado    │           ├────────────────────────┤      │
│  │ TenantProvisionado   │           │   DocumentoFiscal      │      │
│  └──────────────────────┘           ├────────────────────────┤      │
│                                     │    ItemDocumento       │      │
│  Identifiers/                       ├────────────────────────┤      │
│  ┌────────────────────┐             │   DeliveryAttempt      │      │
│  │  ClienteAppId      │             └────────────────────────┘      │
│  │  TenantId          │◄──────────────── TId parameter              │
│  │  DocumentoFiscalId │                                             │
│  │  DeliveryAttemptId │             ▲ use value objects             │
│  └────────────────────┘             │                               │
│        ▲ inherits                   │                               │
│  ┌─────────────────────┐  ValueObjects/                             │
│  │ StronglyTypedId<T>  │  ┌─────────────────────────────────┐      │
│  └─────────────────────┘  │  Cnpj    Cpf    ChaveAcesso     │      │
│                            │  QrCode  Endereco  Produto      │      │
│                            │  Tributo  Pagamento             │      │
│                            │  ConfiguracaoFiscal             │      │
│                            │  CertificadoDigital             │      │
│                            └─────────────────────────────────┘      │
│                                     ▲ use enums                     │
│  Enums/                             │                               │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │ TipoDocumento  StatusDocumento  RegimeTributario             │    │
│  │ TipoEmissao    AmbienteSefaz    TipoPagamento                │    │
│  │ ModalidadeFrete TipoIcms  OrigemMercadoria                   │    │
│  │ CSOSN  CstIcms  CstPisCofins  TipoTentativa                 │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                                                                      │
│  Errors/                        Interfaces/                          │
│  ┌────────────────────────┐     ┌──────────────────────────┐        │
│  │  ClienteAppErrors      │     │  IClienteAppRepository   │        │
│  │  TenantErrors          │     │  ITenantRepository       │        │
│  │  DocumentoFiscalErrors │     │  IDocumentoFiscalRepo.   │        │
│  └────────────────────────┘     │  IUnitOfWork             │        │
│          ▲ used by              └──────────────────────────┘        │
│          │ Result.Failure(...)          ▲ implemented by             │
│          │ in entity methods            │ Infrastructure layer       │
└──────────────────────────────────────────────────────────────────────┘

Regra de dependência:
  Domain ──► nada externo
  Application ──► Domain
  Infrastructure ──► Domain + Application interfaces
  Api ──► Application
```

---

## 5. Contratos de Teste

Lista de pré-condições e pós-condições que os testes unitários devem verificar (código dos testes é escrito na Fase 10, mas os contratos são definidos aqui como contrato de implementação).

### 5.1 `Error` e `Result`

| Cenário | Pré-condição | Pós-condição |
|---|---|---|
| `Result.Success()` | — | `IsSuccess = true`, `IsFailure = false`, `Error = Error.None` |
| `Result.Failure(err)` | `err != Error.None` | `IsSuccess = false`, `IsFailure = true`, `Error == err` |
| `Result<T>.Success(value)` | `value != null` | `IsSuccess = true`, `Value == value` |
| `Result<T>.Failure(err).Value` acesso | `IsFailure = true` | Lança `InvalidOperationException` |
| `Error` equality | dois `Error` com mesmo `Code` e `Message` | `error1 == error2` (record equality) |

### 5.2 `ChaveAcesso`

| Cenário | Pré-condição | Pós-condição |
|---|---|---|
| `Gerar(...)` retorna 44 dígitos | parâmetros válidos | `Valor.Length == 44` |
| cDV correto (MOC 7.0) | 43 dígitos `3509061420016714006512500100000180010000009` | cDV = `7`, chave completa = `35090614200167140065125001000001800100000097` |
| Resto 0 → cDV 0 | 43 dígitos que resultam em soma % 11 == 0 | cDV = `0` |
| Resto 1 → cDV 0 | 43 dígitos que resultam em soma % 11 == 1 | cDV = `0` |
| Resto 2 → cDV 9 | 43 dígitos que resultam em soma % 11 == 2 | cDV = `9` |
| `From` com 44 dígitos válidos | string de 44 dígitos numéricos | `IsSuccess = true` |
| `From` com 43 dígitos | string incompleta | `IsSuccess = false` |

### 5.3 `Cnpj`

| Cenário | Pré-condição | Pós-condição |
|---|---|---|
| CNPJ válido sem máscara | `"11222333000181"` | `IsSuccess = true`, `Valor = "11222333000181"` |
| CNPJ válido com máscara | `"11.222.333/0001-81"` | `IsSuccess = true`, `Valor = "11222333000181"` |
| Ambas formas produzem o mesmo `Cnpj` | mesmo CNPJ com e sem máscara | `cnpj1 == cnpj2` |
| DV incorreto | `"11111111111100"` (DV errado) | `IsSuccess = false` |
| Todos dígitos iguais | `"11111111111111"` | `IsSuccess = false` |
| Comprimento errado | `"1234567"` | `IsSuccess = false` |

### 5.4 `Cpf`

| Cenário | Pré-condição | Pós-condição |
|---|---|---|
| CPF válido sem máscara | `"12345678909"` (exemplo; usar CPF válido real) | `IsSuccess = true` |
| CPF com máscara | `"123.456.789-09"` | `IsSuccess = true`, mesma normalização |
| Todos dígitos iguais | `"00000000000"` a `"99999999999"` (11 casos) | `IsSuccess = false` |
| DV incorreto | qualquer CPF com DV alterado | `IsSuccess = false` |

### 5.5 `QrCode`

| Cenário | Pré-condição | Pós-condição |
|---|---|---|
| URL não contém CSC | qualquer geração válida | `!UrlCompleta.Contains(csc)` |
| Hash correto (sandbox AM) | chave MOC 7.0, tpAmb=2, csc="0123456789" | `UrlCompleta` contém o hash SHA-1 pré-computado |
| `|2|` literal na string de hash | qualquer geração | string SHA-1 é `chave + "|2|" + tpAmb + "|" + csc` |
| CSC vazio retorna falha | `csc = ""` | `IsSuccess = false` |

### 5.6 `DocumentoFiscal` — State Machine

| Cenário | Estado inicial | Método chamado | Estado final esperado | Evento publicado |
|---|---|---|---|---|
| Transição válida | `Criado` | `Enfileirar()` | `Enfileirado` | nenhum |
| Transição válida | `Enfileirado` | `IniciarProcessamento()` | `Processando` | nenhum |
| Transição válida | `Processando` | `Autorizar(...)` | `Autorizado` | `DocFiscalAutorizadoEvent` |
| Transição válida | `Processando` | `Rejeitar(motivo)` | `Rejeitado` | `DocFiscalRejeitadoEvent` |
| Transição válida | `Processando` | `Falhar()` | `Falhou` | `DocFiscalFalhouEvent` |
| Transição válida | `Processando` | `Denegar(motivo)` | `Denegado` | `DocFiscalDenegadoEvent` |
| Cancelamento dentro prazo | `Autorizado`, +29 min | `Cancelar(tp)` | `Cancelado` | `DocFiscalCanceladoEvent` |
| Cancelamento exatamente no limite | `Autorizado`, +30 min exatos | `Cancelar(tp)` | `Autorizado` (sem mudança) | nenhum, `Result.Failure` |
| Cancelamento fora do prazo | `Autorizado`, +31 min | `Cancelar(tp)` | `Autorizado` (sem mudança) | nenhum, `Result.Failure` |
| Transição inválida: Autorizar já autorizado | `Autorizado` | `Autorizar(...)` | `Autorizado` (sem mudança) | nenhum, `Result.Failure(TransicaoInvalida)` |
| Transição inválida: Rejeitar autorizado | `Autorizado` | `Rejeitar(...)` | `Autorizado` (sem mudança) | nenhum, `Result.Failure(TransicaoInvalida)` |
| Transição inválida: Cancelar `Criado` | `Criado` | `Cancelar(tp)` | `Criado` (sem mudança) | nenhum, `Result.Failure(TransicaoInvalida)` |
| Transição inválida: Enfileirar `Processando` | `Processando` | `Enfileirar()` | `Processando` (sem mudança) | nenhum, `Result.Failure(TransicaoInvalida)` |

### 5.7 `ConfiguracaoFiscal`

| Cenário | Pré-condição | Pós-condição |
|---|---|---|
| Serie válida | `"001"` | `IsSuccess = true` |
| Serie inválida (letras) | `"ABC"` | `IsSuccess = false` — regex `^[0-9]{1,3}$` |
| Serie inválida (mais de 3 dígitos) | `"1234"` | `IsSuccess = false` |
| UF inválida | `ufCodigo = 0` | `IsSuccess = false` |

---

## 6. Checklist de Conclusão

- [ ] Projeto `VisuFiscalHub.Domain.csproj` sem nenhum `<PackageReference>` para NuGet externo
- [ ] `Common/Error.cs` — `sealed record Error(string Code, string Message)` compilando
- [ ] `Common/Result.cs` — `Result` e `Result<T>` com factories estáticas; acesso a `Value` em falha lança `InvalidOperationException`
- [ ] `Common/IDomainEvent.cs` — interface com `EventId` e `OccurredAt`
- [ ] `Common/IDomainEventSource.cs` — interface com `IReadOnlyList<IDomainEvent>` e `ClearDomainEvents`
- [ ] `Common/Entity.cs` — `Entity<TId>` abstrata, `IDomainEventSource`, `AddDomainEvent` protegido, `Equals` por identidade
- [ ] `Common/StronglyTypedId.cs` — `abstract record struct StronglyTypedId<T>`
- [ ] Quatro identificadores em `Identifiers/` com `New()`, `From()`, conversões implícita/explícita
- [ ] Treze enums em `Enums/` com valores numéricos corretos
- [ ] `StatusDocumento` contém `Falhou = 7` e `Denegado = 8`
- [ ] `AmbienteSefaz.Producao = 1` e `AmbienteSefaz.Homologacao = 2`
- [ ] `ValueObjects/Cnpj.cs` — `Criar` normaliza máscara e valida dois DVs
- [ ] `ValueObjects/Cpf.cs` — `Criar` normaliza máscara e valida dois DVs
- [ ] `ValueObjects/ChaveAcesso.cs` — `Gerar` produz 44 dígitos; cDV do MOC 7.0 = `7`
- [ ] `ValueObjects/QrCode.cs` — fórmula `SHA1(chave + "|2|" + tpAmb + "|" + csc)`; CSC ausente da URL
- [ ] `ValueObjects/ConfiguracaoFiscal.cs` — `Serie` validada com `^[0-9]{1,3}$`
- [ ] Demais value objects em `ValueObjects/` compilando
- [ ] `Entities/ClienteApp.cs` — factory `Criar`, `Desativar`, `AtualizarWebhookSecret`
- [ ] `Entities/Tenant.cs` — factory `Criar` publica `TenantProvisionadoEvent`
- [ ] `Entities/DocumentoFiscal.cs` — state machine completa; todas as transições retornam `Result`; nunca lançam exceção
- [ ] `Entities/ItemDocumento.cs` — sem identidade própria, backing fields `_items` e `_pagamentos` em `DocumentoFiscal`
- [ ] `Entities/DeliveryAttempt.cs` — compilando com todos os campos
- [ ] Seis events em `Events/` com `EventId = Guid.NewGuid()` e `OccurredAt = DateTime.UtcNow`
- [ ] `DocumentoFiscalAutorizadoEvent` contém `ClienteAppId`
- [ ] Três classes de errors estáticas em `Errors/` com instâncias `static readonly Error`
- [ ] `DocumentoFiscalErrors.PrazoDeCancelamentoExpirado` definido
- [ ] Quatro interfaces em `Interfaces/` sem referência a EF Core ou qualquer NuGet
- [ ] `ITenantRepository.GetNextNumeracaoAsync` tem assinatura com `TenantId`, `string serie` e `CancellationToken`
- [ ] `IDocumentoFiscalRepository.GetProcessandoAntigoAsync` tem assinatura com `TimeSpan timeout`
- [ ] Projeto compila com `dotnet build` sem warnings de nullable e sem erros
