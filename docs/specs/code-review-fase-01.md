# Code Review — Fase 1: Domain Layer

**Revisado por:** Arquiteto Sênior .NET 10  
**Data:** 2026-05-12  
**Versão revisada:** commit mais recente em `master`  
**Documentos de referência:** `docs/decisions.md` v3.0, `docs/specs/fase-01-domain.md` v1.0

---

## Sumário Executivo

O Domain Layer do VisuFiscalHub foi implementado com qualidade acima da média para um projeto de portfólio. A estrutura geral está correta, Clean Architecture é respeitada, o padrão Result está bem implementado, e a state machine do DocumentoFiscal funciona corretamente. Não há dependências externas no Domain.

Contudo, existem issues que precisam ser corrigidos antes de considerar a Fase 1 concluída com qualidade de portfólio:

| Severidade | Quantidade |
|---|---|
| Crítico (impedem merge/uso) | 4 |
| Importante (devem ser corrigidos antes da Fase 2) | 9 |
| Menor (melhorias de qualidade) | 8 |

**Recomendação:** Não avançar para a Fase 2 sem corrigir os 4 issues críticos e os 9 issues importantes. Os issues críticos envolvem corretude fiscal (algoritmo de cDV incorreto com comentário errado embutido no código), inconsistência de tipo em IDomainEvent (`DateTimeOffset` vs `DateTime`), violação de encapsulamento na entidade `DocumentoFiscal` (desvio do contrato da spec), e ausência de validações obrigatórias em `AtualizarCsc`.

---

## Issues Críticos

---

### CRITICO-01 — Comentário errado no `CalcularCDV` aponta para vetor MOC inválido

**Arquivo:** `src/VisuFiscalHub.Domain/ValueObjects/ChaveAcesso.cs:67-68`

**Código atual:**
```csharp
// Vetor MOC 7.0: "3509061420016714006512500100000180010000009" → soma=448, resto=8, cDV=3
// Chave completa: "35090614200167140065125001000001800100000093"
private static int CalcularCDV(string quarentaTresDígitos)
```

**Problema:** O comentário está completamente errado. Conforme `decisions.md` seção 5.2 e `fase-01-domain.md` seção 3.23, o vetor MOC 7.0 para os 43 dígitos `3509061420016714006512500100000180010000009` deve produzir `cDV = 7`, resultando na chave `35090614200167140065125001000001800100000097`. O comentário no código diz `cDV=3` e chave terminando em `...93` — valores errados.

O algoritmo em si está correto (lógica de pesos cíclicos 2-9 da direita para a esquerda, `resto < 2 → 0`, `senão 11 - resto`). O problema é que o comentário documenta um resultado incorreto, o que pode induzir quem escrever os testes unitários a hardcodar o valor errado como `[InlineData]`. Em um portfólio este é um erro grave de documentação inline.

**Correção:**
```csharp
// Módulo 11 da direita para a esquerda, pesos cíclicos 2-9.
// Vetor de validação (MOC 7.0 Seção 4.1.6):
// 43 dígitos: "3509061420016714006512500100000180010000009" → cDV = 7
// Chave completa: "35090614200167140065125001000001800100000097"
private static int CalcularCDV(string quarentaTresDígitos)
```

---

### CRITICO-02 — `IDomainEvent.OccurredAt` é `DateTimeOffset` mas spec define `DateTime`; events usam `DateTimeOffset` — inconsistência de tipo não documentada

**Arquivo:** `src/VisuFiscalHub.Domain/Common/IDomainEvent.cs:6`

**Código atual:**
```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}
```

**Problema:** A spec (`fase-01-domain.md` seção 3.3) define explicitamente `DateTime OccurredAt { get; }` na interface. O código implementa `DateTimeOffset`. Esta é uma divergência deliberada do plano, e seria aceitável **se fosse consistente**, mas cria inconsistência grave em toda a camada de eventos:

- `DocumentoFiscalFalhouEvent.FalhouAt` é `DateTime` (linha 7 do event)
- `DocumentoFiscalCanceladoEvent.CanceladoAt` é `DateTime` (linha 9 do event)
- `DocumentoFiscalAutorizadoEvent.AuthorizedAt` é `DateTime` (linha 12 do event)
- Mas `OccurredAt` em todos os events é `DateTimeOffset`

A mistura de `DateTime` e `DateTimeOffset` dentro do mesmo aggregate é um problema de corretude temporal. Para portfólio de .NET 10, `DateTimeOffset` é o tipo correto para qualquer timestamp (conforme boas práticas da plataforma), mas a decisão deve ser **tomada de forma consistente**. Se `OccurredAt` é `DateTimeOffset`, então `FalhouAt`, `CanceladoAt` e `AuthorizedAt` nos events também devem ser `DateTimeOffset`. Se é `DateTime`, devem ser todos `DateTime`.

Dado que `decisions.md` (seção DA-05) e o spec (seção 3.33) definem `AuthorizedAt` como `DateTime`, e que os eventos carregam esses campos, o mais correto para este projeto é definir a interface com `DateTime` (alinhado à spec) e manter consistência, OU atualizar toda a hierarquia para `DateTimeOffset` e documentar a divergência do plano.

**Correção opção A (alinhamento com spec atual):**
```csharp
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }  // UTC — alinhado com spec fase-01-domain.md seção 3.3
}
```

**Correção opção B (melhoria intencional — requer atualização do plano):** Adotar `DateTimeOffset` em toda a hierarquia: interface, eventos, propriedades das entidades (`AuthorizedAt`, `CanceladoAt`, `FalhouAt`, `CreatedAt`). Documentar como decisão técnica melhorada em relação à spec.

A opção B é tecnicamente superior. Se adotada, o `TenantErrors.ConfiguracaoFiscalInvalida` e outros campos `DateTime` nas entidades também precisam ser migrados para `DateTimeOffset`, e a decisão deve estar documentada em `decisions.md`.

---

### CRITICO-03 — `DocumentoFiscal.Criar` aceita `idempotencyKey` vazia com erro semântico incorreto

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs:87`

**Código atual:**
```csharp
if (string.IsNullOrWhiteSpace(idempotencyKey))
    return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IdempotencyKeyJaUsada);
```

**Problema:** Retornar `IdempotencyKeyJaUsada` quando a chave é nula ou vazia é semanticamente incorreto. `IdempotencyKeyJaUsada` significa "esta chave já foi processada com sucesso" — é um erro de negócio diferente de "a chave fornecida é inválida/ausente". Este código errado vai causar mensagens de erro confusas para os consumidores da API e dificultar debugging.

A spec (`fase-01-domain.md` seção 3.33) não especifica este erro explicitamente, mas o erro correto para entrada inválida seria `DocumentoFiscalErrors.StatusInvalidoParaOperacao` ou, melhor, um erro dedicado.

**Correção:** Adicionar erro dedicado ou usar o erro existente correto:
```csharp
// Em DocumentoFiscalErrors.cs — adicionar:
public static readonly Error IdempotencyKeyInvalida =
    new("DocumentoFiscal.IdempotencyKeyInvalida", "A chave de idempotência não pode ser nula ou vazia.");

// Em DocumentoFiscal.cs:
if (string.IsNullOrWhiteSpace(idempotencyKey))
    return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IdempotencyKeyInvalida);
```

---

### CRITICO-04 — `AtualizarCsc` não valida `cIdToken` — invariante definido na spec violado silenciosamente

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/Tenant.cs:94-98`

**Código atual:**
```csharp
public void AtualizarCsc(byte[] cscCriptografado, string cIdToken)
{
    Csc = cscCriptografado;
    CIdToken = cIdToken;
}
```

**Problema:** A spec (`fase-01-domain.md` seção 3.32) define como invariante: "`CIdToken` quando presente deve ter exatamente 6 caracteres numéricos (ex: `'000001'`)". O método `AtualizarCsc` aceita qualquer string sem validação. Um `cIdToken` inválido vai causar rejeição do SEFAZ no campo `cIdToken` do XML da NFC-e (seção 5.3 do decisions.md: "6 dígitos com zeros à esquerda"). O domínio é a última linha de defesa para invariantes — este invariante está documentado e não está sendo enforced.

Além disso, `cscCriptografado` pode ser `null` ou vazio sem validação.

**Correção:**
```csharp
public Result AtualizarCsc(byte[] cscCriptografado, string cIdToken)
{
    if (cscCriptografado is null || cscCriptografado.Length == 0)
        return Result.Failure(TenantErrors.CscInvalido);

    if (string.IsNullOrWhiteSpace(cIdToken) || cIdToken.Length != 6 || !cIdToken.All(char.IsDigit))
        return Result.Failure(TenantErrors.CIdTokenInvalido);

    Csc = cscCriptografado;
    CIdToken = cIdToken;
    return Result.Success();
}
```

Nota: `TenantErrors.CscInvalido` e `TenantErrors.CIdTokenInvalido` precisam ser adicionados ao arquivo de erros.

---

## Issues Importantes

---

### IMP-01 — `StronglyTypedId<T>` é `abstract record` (class) mas spec define `abstract record struct`; divergência não elimina o problema

**Arquivo:** `src/VisuFiscalHub.Domain/Common/StronglyTypedId.cs:9`

**Código atual:**
```csharp
public abstract record StronglyTypedId<T>(T Value)
    where T : notnull;
```

**Problema:** O comentário explica corretamente a limitação do C# com herança de `record struct`, e os identificadores concretos são `readonly record struct` independentes — o que é tecnicamente correto. No entanto, o resultado é que `ClienteAppId`, `TenantId`, etc. **não implementam** `StronglyTypedId<T>`. A classe base é um artefato sem nenhum uso real — nunca é usada como tipo em nenhuma interface, entidade ou parâmetro.

Se a base não tem utilidade prática (nenhum código faz `StronglyTypedId<Guid> id = ...`), ela é código morto que polui o namespace e pode confundir futuros leitores. Para portfólio, a decisão deve ser uma das duas: (a) remover a classe base e documentar a razão nos identificadores, ou (b) adotá-la como classe base real e aceitar que os IDs são `record` (reference type) em vez de `record struct` — o que é perfeitamente válido para EF Core.

A solução mais limpa para um portfólio seria remover `StronglyTypedId.cs` e deixar os identificadores autocontidos com o comentário explicativo já presente neles.

---

### IMP-02 — `ClienteApp.Criar` usa `DateTime.UtcNow` hardcoded; `Tenant.Criar` também

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/ClienteApp.cs:66`  
**Arquivo:** `src/VisuFiscalHub.Domain/Entities/Tenant.cs:74`

**Código atual:**
```csharp
DateTime.UtcNow  // em ClienteApp.Criar e Tenant.Criar
```

**Problema:** `decisions.md` seção DA-T05 define: "`TimeProvider` deve ser injetado via `IServiceProvider` em todos os handlers e serviços que usam data/hora". Para entidades do domain, a spec observa que `TimeProvider` não pode ser injetado diretamente (entidades não são serviços), mas a solução correta para testabilidade é passar o timestamp como parâmetro para o factory method. Testabilidade é um requisito explícito de portfólio (meta de 95% de cobertura no Domain).

A situação do `DocumentoFiscal.Falhar` (linha 189) é ainda mais problemática: `DateTime.UtcNow` é passado diretamente ao construir o `DocumentoFiscalFalhouEvent` — enquanto o `Cancelar` corretamente usa `timeProvider.GetUtcNow()`. Esta inconsistência dentro da mesma entidade é um defeito.

**Correção:** Passar `DateTimeOffset createdAt` (ou `DateTime`) como parâmetro dos factory methods:
```csharp
public static Result<ClienteApp> Criar(
    string name,
    string clientId,
    string clientSecretHash,
    DateTimeOffset? createdAt = null,
    string? webhookUrl = null,
    byte[]? webhookSecretCriptografado = null)
```

Para o `DocumentoFiscal.Falhar`, deve receber `TimeProvider` como o `Cancelar` já faz:
```csharp
public Result Falhar(TimeProvider timeProvider)
{
    // ...
    AddDomainEvent(new DocumentoFiscalFalhouEvent(Id, TenantId, timeProvider.GetUtcNow()));
}
```

---

### IMP-03 — `DocumentoFiscal` tem propriedade `ClienteAppId` não prevista na spec — desvio não documentado

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs:53-56`

**Código atual:**
```csharp
// Armazenado para evitar query adicional ao publicar DocumentoFiscalAutorizadoEvent/DenegadoEvent
public ClienteAppId ClienteAppId { get; private set; }
```

**Problema:** A spec (`fase-01-domain.md` seção 3.33) e o `decisions.md` (seção 3 — DocumentoFiscal) não incluem `ClienteAppId` como campo do `DocumentoFiscal`. O modelo de identidade define apenas `tenantId` como FK no documento. O comentário em linha justifica a decisão, mas o desvio não foi registrado em `decisions.md`.

A justificativa é válida tecnicamente — evita um JOIN ao publicar o evento. No entanto, este campo denormaliza o modelo: o `ClienteAppId` já pode ser obtido via `Tenant.ClienteAppId`. Além disso, o factory method `Criar` exposto também aceita `clienteAppId` como parâmetro público, o que não está na spec.

Se a decisão de manter é tomada (que é defensável), ela deve ser documentada em `decisions.md` como desvio intencional com justificativa.

---

### IMP-04 — `Denegar` tem assinatura diferente da spec — parâmetro extra não documentado

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs:196`

**Código atual:**
```csharp
public Result Denegar(string motivo, string cnpjEmitente)
```

**Spec (`fase-01-domain.md` seção 3.33):**
```csharp
public Result Denegar(string motivo);
```

**Problema:** A assinatura do método difere da spec. O `cnpjEmitente` foi adicionado para o evento `DocumentoFiscalDenegadoEvent`, que requer o CNPJ para log `Critical`. A lógica é correta, mas o desvio da assinatura não foi documentado. Qualquer handler da Fase 2 que siga a spec vai passar apenas `motivo` e não compilar.

Adicionalmente, o campo `cnpjEmitente` não é validado (pode ser `null` ou string vazia). Um CNPJ inválido passado ao evento vai aparecer no log de auditoria `Critical` de forma incorreta.

**Correção:** Documentar o desvio em `decisions.md` E adicionar validação:
```csharp
public Result Denegar(string motivo, string cnpjEmitente)
{
    if (Status != StatusDocumento.Processando)
        return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

    if (string.IsNullOrWhiteSpace(cnpjEmitente))
        return Result.Failure(DocumentoFiscalErrors.CnpjEmitenteMissing);
    // ...
}
```

---

### IMP-05 — `AtualizarClientSecretHash` silencia erros — viola o Result Pattern

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/ClienteApp.cs:71-75`

**Código atual:**
```csharp
public void AtualizarClientSecretHash(string novoHash)
{
    if (!string.IsNullOrWhiteSpace(novoHash))
        ClientSecretHash = novoHash;
}
```

**Problema:** Se `novoHash` for nulo ou vazio, o método silenciosamente não faz nada. Este comportamento viola o princípio de fail-fast e o Result Pattern adotado no projeto. O caller que chama `AtualizarClientSecretHash("")` não tem como saber que a operação falhou — vai chamar `SaveChangesAsync` com o hash antigo ainda em vigor, sem nenhum erro.

**Correção:**
```csharp
public Result AtualizarClientSecretHash(string novoHash)
{
    if (string.IsNullOrWhiteSpace(novoHash))
        return Result.Failure(ClienteAppErrors.ClientSecretHashInvalido);

    ClientSecretHash = novoHash;
    return Result.Success();
}
```

---

### IMP-06 — `ConfiguracaoFiscal` usa `Regex.IsMatch` sem `RegexOptions.Compiled` — performance em hot path

**Arquivo:** `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs:28`

**Código atual:**
```csharp
if (string.IsNullOrWhiteSpace(serie) || !Regex.IsMatch(serie, @"^[0-9]{1,3}$"))
```

**Problema:** `Regex.IsMatch` com string literal instancia um novo `Regex` a cada chamada. Em .NET 10, o padrão correto é usar um campo `static readonly` com `[GeneratedRegex]` (source generator) ou ao menos `RegexOptions.Compiled`. Para um portfólio .NET 10, não usar o source generator é um item que avaliadores experientes notam.

**Correção:**
```csharp
[GeneratedRegex(@"^[0-9]{1,3}$")]
private static partial Regex SerieRegex();

// No método Criar:
if (string.IsNullOrWhiteSpace(serie) || !SerieRegex().IsMatch(serie))
```

Nota: A classe precisará ser `partial` para usar `GeneratedRegex`.

---

### IMP-07 — `Tributo` não tem factory method `Criar` com validação — value object sem proteção de invariantes

**Arquivo:** `src/VisuFiscalHub.Domain/ValueObjects/Tributo.cs`

**Código atual:**
```csharp
public sealed record Tributo(
    TipoIcms TipoIcms,
    int CsosnOuCst,
    // ...
    decimal ValorCofins);
```

**Problema:** `Tributo` é um `sealed record` com construtor primário público. Qualquer código pode criar um `Tributo` com valores negativos (`ValorIcms = -100`), zero em `BaseCalculoIcms` com `AliquotaIcms > 0`, ou combinações inválidas (ex: `TipoIcms = CST` mas `CsosnOuCst = 400` que é um valor CSOSN). A spec (`fase-01-domain.md` seção 3.27) define invariantes claros: "Todos os valores decimais são `>= 0`".

Para portfólio, value objects sem validação de invariantes são um anti-pattern DDD grave.

**Correção:** Adicionar construtor privado e factory method:
```csharp
public sealed record Tributo
{
    private Tributo(TipoIcms tipoIcms, int csosnOuCst, /* ... */) { /* assigns */ }

    public static Result<Tributo> Criar(TipoIcms tipoIcms, int csosnOuCst, /* ... */)
    {
        if (aliquotaIcms < 0 || baseCalculoIcms < 0 || valorIcms < 0)
            return Result.Failure<Tributo>(DocumentoFiscalErrors.TributoInvalido);
        // demais validações
        return Result.Success(new Tributo(/* ... */));
    }
}
```

---

### IMP-08 — `CertificadoDigital` usa `DateTime` e não valida `PfxBytes` — inconsistência de tipo e ausência de guarda

**Arquivo:** `src/VisuFiscalHub.Domain/ValueObjects/CertificadoDigital.cs`

**Código atual:**
```csharp
public sealed record CertificadoDigital(DateTime VencimentoEm, byte[] PfxBytes);
```

**Problema:** `decisions.md` seção DA-05 especifica que o `CertificadoVencimento` é `DateTime` no Tenant, mas para um value object moderno em .NET 10 que representa um ponto no tempo, `DateTimeOffset` é o tipo correto (permite representar a data de vencimento no fuso do certificado). Além disso, não há validação: `PfxBytes` pode ser `null` ou array vazio, e `VencimentoEm` pode ser uma data no passado.

Para uso em memória (conforme spec), a ausência de factory method é aceitável por se tratar de um DTO de retorno de `ITenantCertificateProvider`, mas a ausência de validação do `null` pode causar `NullReferenceException` no consumidor.

**Correção mínima:**
```csharp
public sealed record CertificadoDigital
{
    public DateTime VencimentoEm { get; }
    public byte[] PfxBytes { get; }

    public CertificadoDigital(DateTime vencimentoEm, byte[] pfxBytes)
    {
        ArgumentNullException.ThrowIfNull(pfxBytes);
        if (pfxBytes.Length == 0)
            throw new ArgumentException("PfxBytes não pode ser vazio.", nameof(pfxBytes));
        VencimentoEm = vencimentoEm;
        PfxBytes = pfxBytes;
    }
}
```

---

### IMP-09 — `ISefazClient` recebe `DocumentoFiscal` e `Tenant` completos — viola ISP e expõe dados sensíveis desnecessariamente

**Arquivo:** `src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs:20-29`

**Código atual:**
```csharp
Task<SefazRetorno> SubmeterAutorizacaoAsync(
    DocumentoFiscal documento,
    Tenant tenant,
    CancellationToken ct);
```

**Problema:** Passar a entidade `Tenant` completa para `ISefazClient` expõe `CertificadoPfxCriptografado`, `Csc` criptografado, `ClientSecretHash` (via navegação), e todos os outros campos — quando o SEFAZ client precisa apenas do certificado desencriptografado (que vem de `ITenantCertificateProvider`) e dos dados de configuração (`UfCodigo`, `Ambiente`). Esta é uma violação do Princípio do Mínimo Privilégio aplicado a interfaces.

A spec define esta interface desta forma (seção 3.49), então a questão é se a spec é adequada para portfólio. Para um portfólio de altíssimo nível, a interface deveria receber apenas os dados necessários para a operação, não o aggregate completo.

Contudo, como a spec define assim, este issue é um ponto de discussão entre "seguir o plano" vs "portfólio de qualidade máxima". Registrado como importante para ser endereçado na Fase 7 quando a implementação concreta for feita.

---

## Issues Menores

---

### MENOR-01 — `using System.Collections.Generic` redundante em .NET 10

**Arquivo:** `src/VisuFiscalHub.Domain/Common/IDomainEventSource.cs:1`  
**Arquivo:** `src/VisuFiscalHub.Domain/Common/Entity.cs:1`  
**Arquivo:** `src/VisuFiscalHub.Domain/ValueObjects/Pagamento.cs:1-2`

**Problema:** Em .NET 10 com `ImplicitUsings` habilitado (padrão para `net10.0`), `System.Collections.Generic`, `System.Linq` e outros namespaces comuns são implícitos. Os `using` explícitos são redundantes. Para portfólio, consistência importa — ou todos os arquivos têm usings explícitos, ou nenhum (usando globais implícitos).

**Correção:** Verificar se o `.csproj` tem `<ImplicitUsings>enable</ImplicitUsings>`. Se sim, remover os `using` redundantes.

---

### MENOR-02 — Comentário explicativo do EF Core desnecessariamente verboso em `Tenant.cs`

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/Tenant.cs:14-18`

**Código atual:**
```csharp
private Tenant()
{
    // Para EF Core — inicialização via reflexão.
    // null! é justificado: EF Core popula essas propriedades via reflexão após instanciar.
    RazaoSocial = string.Empty;
    Cnpj = null!;
    ConfiguracaoFiscal = null!;
    Endereco = null!;
}
```

**Problema:** O comentário "null! é justificado" em código de produção é um code smell. O `!` no nullable já é a anotação explícita de "sei que isto é nulo mas prometo que não vai ser nulo em uso". A frase "é justificado" em comentário é defensiva e desnecessária. O padrão correto para .NET 10 + EF Core é simplesmente usar `null!` sem comentário de justificativa, pois todo desenvolvedor .NET sabe que `null!` suprimir o warning para propriedades EF Core.

**Correção:** Manter apenas o comentário de uma linha `// Para EF Core`.

---

### MENOR-03 — `_domainEvents` é sobrescrito duas vezes no construtor privado de `DocumentoFiscal`

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs:13-14 e 20-21`

**Código atual:**
```csharp
// Linha 13-14 (campo + inicializador):
private readonly List<ItemDocumento> _items = [];
private readonly List<Pagamento> _pagamentos = [];

// Linha 20-21 (dentro do construtor privado EF Core):
_items = [];
_pagamentos = [];
```

**Problema:** Os campos `_items` e `_pagamentos` são inicializados no inline initializer (`= []`) e novamente no construtor privado sem parâmetros. O inicializador inline é executado antes do corpo do construtor, portanto o corpo do construtor sobrescreve a lista recém-criada com uma nova lista vazia — duas alocações desnecessárias. Embora funcionalmente correto (EF Core vai popular a lista), é código redundante.

**Correção:** Remover as reatribuições do construtor privado:
```csharp
private DocumentoFiscal()
{
    // Para EF Core — inicialização via reflexão
    IdempotencyKey = string.Empty;
    Serie = string.Empty;
    ChaveAcesso = null!;
    // _items e _pagamentos já inicializados pelo inline initializer
}
```

---

### MENOR-04 — Erro de mensagem em português gramaticalmente incorreta

**Arquivo:** `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs:27`

**Código atual:**
```csharp
public static readonly Error PrazoDeCancelamentoExpirado =
    new("DocumentoFiscal.PrazoDeCancelamentoExpirado", "O prazo de 30 minutos para cancelamento foi expirado.");
```

**Problema:** "foi expirado" é incorreto gramaticalmente em português. O correto é "expirou" (verbo intransitivo) ou "foi excedido".

**Correção:**
```csharp
new("DocumentoFiscal.PrazoDeCancelamentoExpirado", "O prazo de 30 minutos para cancelamento expirou.");
```

---

### MENOR-05 — `DeliveryAttempt` tem construtor público — entidade de domínio com construção não protegida

**Arquivo:** `src/VisuFiscalHub.Domain/Entities/DeliveryAttempt.cs:14`

**Código atual:**
```csharp
public DeliveryAttempt(
    DeliveryAttemptId id,
    // ...
```

**Problema:** A spec (`fase-01-domain.md` seção 3.35) define o construtor público. Para entidades de domínio, a boa prática DDD é ter factory method estático (`Registrar(...)`) que retorna `Result<DeliveryAttempt>`. Um construtor público permite criação sem validação. Embora `DeliveryAttempt` seja simples e sem invariantes complexos, a inconsistência com o padrão do resto do projeto (todos os outros usam factory methods) é visível.

**Correção:** Adicionar factory method estático e tornar o construtor privado, seguindo o padrão do restante do projeto.

---

### MENOR-06 — `IQrCodeGenerator` retorna `string` ao invés de `Result<QrCode>` ou `QrCode` — inconsistência

**Arquivo:** `src/VisuFiscalHub.Application/Common/Interfaces/IQrCodeGenerator.cs:8`

**Código atual:**
```csharp
string Gerar(ChaveAcesso chaveAcesso, AmbienteSefaz ambiente, string csc, string cIdToken, string urlConsultaSefaz);
```

**Problema:** O `QrCode` value object já existe e já tem `QrCode.Gerar(...)` que retorna `Result<QrCode>`. `IQrCodeGenerator.Gerar` retornando `string` perde o encapsulamento do value object e a tipagem forte. O `cIdToken` no parâmetro também não existe no `QrCode.Gerar` original (que não usa `cIdToken` — este campo vai no `<ide>` do XML, não na URL do QR Code). A presença de `cIdToken` aqui pode ser uma confusão de responsabilidades.

---

### MENOR-07 — `DocumentoFiscalErrors.ConfiguracaoFiscalInvalida` duplica `TenantErrors.ConfiguracaoFiscalInvalida`

**Arquivo:** `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs:43-45`

**Código atual:**
```csharp
public static readonly Error ConfiguracaoFiscalInvalida =
    new("DocumentoFiscal.ConfiguracaoFiscalInvalida", "A configuração fiscal é inválida.");
```

**Problema:** `TenantErrors.ConfiguracaoFiscalInvalida` já existe com código `"Tenant.ConfiguracaoFiscalInvalida"`. A configuração fiscal pertence ao `Tenant` — não existe `ConfiguracaoFiscal` do `DocumentoFiscal`. Este erro em `DocumentoFiscalErrors` não está sendo referenciado em nenhum código do Domain e provavelmente foi adicionado por antecipação incorreta.

**Correção:** Remover `DocumentoFiscalErrors.ConfiguracaoFiscalInvalida` para evitar ambiguidade. Se for necessário no futuro, readicioná-lo.

---

### MENOR-08 — `Endereco.Criar` não normaliza `uf` antes de validar — case sensitivity oculta

**Arquivo:** `src/VisuFiscalHub.Domain/ValueObjects/Endereco.cs:45`

**Código atual:**
```csharp
if (string.IsNullOrWhiteSpace(uf) || uf.Length != 2 || !uf.All(char.IsLetter))
    return Result.Failure<Endereco>(TenantErrors.EnderecoInvalido);
```

**Problema:** A validação aceita `"sp"`, `"Sp"`, ou `"SP"` (todos têm 2 letras), mas o armazenamento normaliza para maiúsculas via `uf.ToUpperInvariant()` na linha 59. O problema é que a mensagem de erro não distingue entre "UF inválida (não são 2 letras)" e "UF inválida (não é uma UF brasileira real)". Qualquer combinação de 2 letras como `"XX"` ou `"ZZ"` é aceita. Para portfólio fiscal, validar contra a lista de 27 UFs seria mais correto, consistente com a validação do `UfCodigo` em `ConfiguracaoFiscal`.

---

## Positivos

### O que está muito bem feito

**Result Pattern — implementação exemplar:**  
`Result.cs` e `Result<T>.cs` estão implementados corretamente com invariantes de construção (`ArgumentException` no construtor protegido), backing field nullable para `_value`, acesso protegido com `InvalidOperationException` em caso de falha, e métodos de fábrica estáticos. A hierarquia está correta.

**State Machine `DocumentoFiscal` — 7 transições implementadas:**  
Todas as 7 transições válidas estão implementadas (`Enfileirar`, `IniciarProcessamento`, `Autorizar`, `Rejeitar`, `Cancelar`, `Falhar`, `Denegar`). O boundary de cancelamento usa `>=` corretamente (boundary estrito — `authorizedAt + 30min == utcNow` retorna erro). `TimeProvider` é injetado no `Cancelar`. Todas as transições inválidas retornam `Result.Failure` — nenhum `throw` de exceção de domínio.

**Domain Events publicados dentro das transições:**  
Cada transição de estado que deve publicar um evento o publica imediatamente via `AddDomainEvent` dentro do método de transição — padrão DDD correto. Os 6 domain events obrigatórios estão presentes.

**Algoritmo de validação CNPJ — correto:**  
Pesos `5,4,3,2,9,8,7,6,5,4,3,2` para o primeiro DV e `6,5,4,3,2,9,8,7,6,5,4,3,2` para o segundo. Rejeição de todos-dígitos-iguais. Normalização de máscara. Algoritmo correto.

**Algoritmo de validação CPF — correto:**  
Fórmula `(soma * 10) % 11` correta, rejeição de `resto == 10 || resto == 11` (equivalente ao `== 10` pois `%11` não pode dar 11), rejeição de todos-iguais.

**QR Code — fórmula SHA-1 correta:**  
String de entrada: `chaveAcesso + "|2|" + tpAmb + "|" + csc` — o `|2|` literal está correto, o CSC não aparece na URL final, `SHA1.HashData` com `Convert.ToHexStringLower` usando APIs modernas do .NET 10.

**Módulo 11 — algoritmo correto (apesar do comentário errado):**  
O código do `CalcularCDV` em si está correto: pesos cíclicos 2-9 da direita para a esquerda, `resto < 2 → 0`, `senão 11 - resto`. Apenas o comentário de documentação está errado (CRITICO-01).

**Interfaces de repositório sem dependências de infraestrutura:**  
`IClienteAppRepository`, `ITenantRepository`, `IDocumentoFiscalRepository`, `IUnitOfWork` — zero dependências de EF Core. Correto.

**`ISefazClient` com ambos os métodos:**  
`SubmeterAutorizacaoAsync` e `ConsultarNfeAsync` presentes, conforme critério obrigatório da spec.

**Hierarquia de identidade com `readonly record struct`:**  
`ClienteAppId`, `TenantId`, `DocumentoFiscalId`, `DeliveryAttemptId` são `readonly record struct` com conversão implícita para `Guid` e explícita no sentido inverso — type-safety correta.

**`ConfiguracaoFiscal` valida série para prevenção de SQL injection:**  
Validação regex `^[0-9]{1,3}$` presente e com comentário explicando o "porquê" (prevenção de SQL injection no DDL da sequence). Exatamente o tipo de comentário útil que deve existir.

**Cobertura completa de enums fiscais:**  
`CSOSN`, `CstIcms`, `CstPisCofins`, `TipoPagamento` com todos os valores corretos conforme legislação fiscal.

---

## Checklist de Conformidade

| Critério | Status | Observação |
|---|---|---|
| `DateTimeOffset` vs `DateTime` — uso consistente | FALHA | `IDomainEvent` usa `DateTimeOffset`, spec define `DateTime`, eventos têm ambos (CRITICO-02) |
| Nullable annotations corretas | PARCIAL | `null!` sem `!` sem justificativa (MENOR-02 — justificativa em comentário é desnecessária) |
| C# 12 collection expressions (`[]`) | OK | Usado em `_domainEvents = []`, arrays de pesos, etc. |
| Construtores EF Core privados sem parâmetros | OK | Presentes em todas as entidades |
| Value objects `sealed record` | OK | Todos implementados corretamente |
| Entidades só modificam estado via métodos | OK | Setters `private set` em todas as propriedades |
| Factory methods retornam `Result<T>` | PARCIAL | `DeliveryAttempt` usa construtor público (MENOR-05); `Tributo` sem factory (IMP-07) |
| Domain events publicados dentro das transições | OK | Todas as 7 transições corretas |
| Interfaces de repositório sem EF Core | OK | Zero dependências de infraestrutura |
| Zero dependências NuGet externas no Domain | OK | Conforme spec |
| Linguagem ubíqua em português-BR | OK | Nomes consistentes com domínio fiscal |
| NUNCA `throw` de domínio — sempre `Result.Failure` | OK | Nenhum throw de exceção de domínio |
| `Result<T>.Value` protegido | OK | Lança `InvalidOperationException` internamente |
| Todas as 7 transições da state machine | OK | Implementadas corretamente |
| Boundary de cancelamento `<` estrito | OK | `utcNow >= prazoLimite` → erro |
| `TimeProvider` injetado no `Cancelar` | OK | |
| `TimeProvider` em `Falhar` | FALHA | Usa `DateTime.UtcNow` hardcoded (IMP-02) |
| `DateTime.UtcNow` hardcoded nas entidades | FALHA | `ClienteApp.Criar`, `Tenant.Criar`, `DocumentoFiscal.Falhar` (IMP-02) |
| `cNF` gerado externamente | OK | `ChaveAcesso.Gerar` recebe `cNF` como parâmetro |
| Módulo 11 algoritmo correto | OK | Código correto; comentário errado (CRITICO-01) |
| CNPJ rejeita todos-dígitos-iguais | OK | |
| CPF fórmula `(soma * 10) % 11` | OK | |
| QR Code `\|2\|` literal no hash | OK | |
| CSC nunca na URL final | OK | |
| SHA-1 correto no QR Code | OK | |
| `ISefazClient` com ambos os métodos | OK | |
| `IDomainEvent.OccurredAt` — tipo consistente | FALHA | CRITICO-02 |
| `AtualizarCsc` valida `CIdToken` | FALHA | CRITICO-04 |
| Erro semanticamente correto em `DocumentoFiscal.Criar` | FALHA | CRITICO-03 |
| `ConfiguracaoFiscalInvalida` sem duplicata | FALHA | MENOR-07 |
| Regex compilada para série | FALHA | IMP-06 |

---

*Fim do relatório. Total de issues: 4 críticos, 9 importantes, 8 menores.*
