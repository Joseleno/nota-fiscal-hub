# Spec Fase 7 — Integração SEFAZ

**Versão:** 1.0
**Data:** 2026-05-12
**Dependências:** Fases 6a e 6b concluídas
**Critério de conclusão:** `HangfireDocumentJobQueue` e `HangfireJobProcessor` são classes distintas; `ISefazClient` expõe ambos os métodos; `cStat=110` transiciona para `Denegado`; sandbox AM retorna cStat=100 em smoke test

---

## 1. Visão Geral

### Objetivo

Implementar o envio do XML ao SEFAZ via SOAP/HTTPS com mTLS, processamento do retorno, interpretação correta dos códigos `cStat`, retry inteligente via Hangfire e reconciliação periódica de documentos presos em `Processando`.

### Dependências diretas

| Dependência | Fase |
|---|---|
| `ISefazClient` interface definida | Fase 1 |
| `IDocumentJobQueue` interface definida | Fase 1 |
| `NfceXmlBuilder`, `XmlSigner`, `TributacaoCalculator` | Fase 6a |
| `TenantCertificateProvider`, `CertificateEncryptionService` | Fase 6b |
| `ApplicationDbContext`, repositórios, `IUnitOfWork` | Fase 2 |
| `DocumentoFiscal` state machine completa | Fase 1 |

### Critério de conclusão

- [ ] `HangfireDocumentJobQueue` e `HangfireJobProcessor` são CLASSES SEPARADAS (SRP)
- [ ] `ISefazClient` expõe `SubmeterAutorizacaoAsync` E `ConsultarNfeAsync`
- [ ] `SefazEndpointResolver` cobre todos os 27 códigos de UF
- [ ] `cStat=110` transiciona para `Denegado` (nunca `Rejeitado`)
- [ ] `cStat=204` chama `Rejeitar` sem lançar exceção (sem retry)
- [ ] Timeout/ConnectionRefused: consulta `nfeConsultaNFe` ANTES de decidir retry
- [ ] `HangfireJobProcessor` recebe apenas `Guid documentoId` — nunca dados sensíveis
- [ ] `ReconciliacaoJobProcessor` executa a cada 5 minutos

---

## 2. Árvore de Arquivos

```
src/VisuFiscalHub.Infrastructure/
└── Fiscal/
    └── Sefaz/
        ├── GrupoAutorizador.cs                  ← enum
        ├── SefazEndpointResolver.cs             ← resolver de URLs por UF
        ├── SefazHttpClient.cs                   ← cliente HTTP mTLS
        ├── SefazRetorno.cs                      ← records de retorno
        ├── NfceAutorizacaoService.cs            ← ISefazClient impl
└── Scheduling/
    ├── HangfireDocumentJobQueue.cs              ← adapter IDocumentJobQueue
    ├── HangfireJobProcessor.cs                 ← worker de processamento
    ├── ReconciliacaoJobProcessor.cs             ← job periódico 5min
    ├── OutboxRelayJob.cs                        ← (Fase 6b — referência)
└── DependencyInjection.cs                       ← AddSefazInfrastructure
```

---

## 3. Arquivos — Especificações Detalhadas

### 3.1 `GrupoAutorizador.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Sefaz`

```csharp
namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

public enum GrupoAutorizador
{
    SVRS,      // estados sem infraestrutura própria para NFC-e
    SefazSP,   // São Paulo
    SefazMG,   // Minas Gerais
    SefazRS,   // Rio Grande do Sul
    SefazPR,   // Paraná
    SefazBA,   // Bahia
    SefazMT,   // Mato Grosso
    SandboxAM  // ambiente de desenvolvimento — aceita qualquer certificado
}
```

**Invariantes:**
- Enum fixo — nunca usar valores numéricos hardcoded fora do resolver
- `SandboxAM` nunca deve ser resolvido em ambiente produção

---

### 3.2 `SefazEndpointResolver.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Sefaz`

**Usings:**
```csharp
using VisuFiscalHub.Domain.Enums;
```

**Assinatura da classe:**
```csharp
public sealed class SefazEndpointResolver
{
    // Dicionário ufCodigo → GrupoAutorizador (todas as 27 UFs)
    private static readonly IReadOnlyDictionary<int, GrupoAutorizador> _ufParaGrupo;

    // URLs por (GrupoAutorizador, TipoDocumento, AmbienteSefaz)
    private static readonly IReadOnlyDictionary<(GrupoAutorizador, TipoDocumento, AmbienteSefaz), string> _urlsAutorizacao;
    private static readonly IReadOnlyDictionary<(GrupoAutorizador, AmbienteSefaz), string> _urlsConsultaQr;
    private static readonly IReadOnlyDictionary<(GrupoAutorizador, AmbienteSefaz), string> _urlsEvento;

    static SefazEndpointResolver() { /* inicializar dicionários */ }

    public string ResolverUrl(int ufCodigo, TipoDocumento tipo, AmbienteSefaz ambiente);
    public string ResolverUrlConsultaQr(int ufCodigo, AmbienteSefaz ambiente);
    public string ResolverUrlEvento(int ufCodigo, AmbienteSefaz ambiente);

    private static GrupoAutorizador ObterGrupo(int ufCodigo);
}
```

**Mapeamento completo `ufCodigo → GrupoAutorizador` (TODAS as 27 UFs):**

| ufCodigo | UF | Grupo |
|---|---|---|
| 12 | AC | SVRS |
| 27 | AL | SVRS |
| 16 | AP | SVRS |
| 13 | AM | SandboxAM (em homologação de desenvolvimento) / SVRS (produção) |
| 29 | BA | SefazBA |
| 23 | CE | SVRS |
| 53 | DF | SVRS |
| 32 | ES | SVRS |
| 52 | GO | SVRS |
| 21 | MA | SVRS |
| 51 | MT | SefazMT |
| 50 | MS | SVRS |
| 31 | MG | SefazMG |
| 15 | PA | SVRS |
| 25 | PB | SVRS |
| 41 | PR | SefazPR |
| 26 | PE | SVRS |
| 22 | PI | SVRS |
| 33 | RJ | SVRS |
| 24 | RN | SVRS |
| 11 | RO | SVRS |
| 14 | RR | SVRS |
| 43 | RS | SefazRS |
| 42 | SC | SVRS |
| 35 | SP | SefazSP |
| 28 | SE | SVRS |
| 17 | TO | SVRS |

> NOTA: Sergipe (SE, ufCodigo=28) pertence ao grupo SVRS — confirmado em DA-07.
> Amazonas (AM, ufCodigo=13): o grupo `SandboxAM` é exclusivo para o ambiente de desenvolvimento (DA-08). Em produção, AM usa SVRS ou infraestrutura própria — verificar documentação técnica SEFAZ mais recente. No scaffold inicial, mapear AM→SandboxAM para facilitar desenvolvimento.

**URLs SVRS (NFC-e):**

| Ambiente | URL NfceAutorizacao | URL ConsultaQR |
|---|---|---|
| Homologacao | `https://nfce-homologacao.svrs.rs.gov.br/ws/NfceAutorizacao/NfceAutorizacao.asmx` | `https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx` |
| Producao | `https://nfce.svrs.rs.gov.br/ws/NfceAutorizacao/NfceAutorizacao.asmx` | `https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx` |

**URLs SandboxAM:**

| Serviço | URL |
|---|---|
| Autorização | `https://homnfce.sefaz.am.gov.br/nfceweb/services/NfceAutorizacao4.asmx` |
| Consulta | `https://homnfce.sefaz.am.gov.br/nfceweb/services/NfceConsulta4.asmx` |
| Evento | `https://homnfce.sefaz.am.gov.br/nfceweb/services/NfceRecepcaoEvento4.asmx` |

**Regras críticas:**
- `ObterGrupo` lança `InvalidOperationException` com mensagem clara se `ufCodigo` não mapeado
- Classe `sealed` — não estendida via herança
- Dicionários `static readonly` inicializados no construtor estático — evitar alocação por chamada
- `ResolverUrlEvento` preparado para Fase 6B (cancelamento via `NfeRecepcaoEvento4`)

---

### 3.3 `SefazRetorno.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Sefaz`

```csharp
namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Retorno do envio de autorização ao SEFAZ.
/// cStat segue classificação de decisions.md seção 5.6 — NÃO segue semântica HTTP.
/// </summary>
public sealed record SefazRetorno(
    bool Autorizado,
    string CStat,
    string XMotivo,
    string? NProt,
    string? XmlAutorizado
)
{
    public bool EhDefinitivo =>
        CStat == "100" || CStat == "101" || CStat == "110" || CStat == "204" ||
        (int.TryParse(CStat, out var c) && c >= 400 && c < 500);

    public bool EhDenegado => CStat == "110";
    public bool EhDuplicidade => CStat == "204";
    public bool EhTemporario =>
        int.TryParse(CStat, out var c) &&
        ((c >= 300 && c < 400) || (c >= 500 && c < 600));
}

/// <summary>
/// Retorno da consulta nfeConsultaNFe.
/// </summary>
public sealed record SefazConsultaRetorno(
    bool Encontrado,
    bool Autorizado,
    string CStat,
    string? NProt,
    string? XmlProtocolo
);
```

---

### 3.4 `SefazHttpClient.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Sefaz`

**Usings:**
```csharp
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
```

**Assinatura da classe:**
```csharp
/// <summary>
/// Cliente HTTP para comunicação SOAP com o SEFAZ via mTLS.
/// DEVE ser registrado via services.AddHttpClient<SefazHttpClient>() —
/// NUNCA instanciar HttpClientHandler por chamada (socket exhaustion).
/// </summary>
public sealed class SefazHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SefazHttpClient> _logger;

    // Injetado via AddHttpClient — HttpClient gerenciado pelo IHttpClientFactory
    public SefazHttpClient(HttpClient httpClient, ILogger<SefazHttpClient> logger);

    /// <summary>
    /// Envia envelope SOAP ao SEFAZ.
    /// Timeout via CancellationTokenSource linked — separado do token do Hangfire.
    /// </summary>
    public async Task<string> EnviarSoapAsync(
        string url,
        string soapEnvelope,
        X509Certificate2 certificadoTenant,
        CancellationToken cancellationToken);
}
```

**Notas de implementação:**
1. **Registro no DI (em `DependencyInjection.cs`):**
   ```csharp
   services.AddHttpClient<SefazHttpClient>()
       .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
       {
           AllowAutoRedirect = false,
           // SslProtocols configurado via runtime padrão (.NET 10 usa TLS 1.2+)
       });
   ```
2. **mTLS:** O certificado do tenant é adicionado por chamada via `HttpRequestMessage` com `HttpRequestOptions`, ou via handler configurado dinamicamente. Como o certificado varia por tenant, a abordagem recomendada é criar um `HttpRequestMessage` e definir o certificado via `Properties` que o handler lê, OU usar `SocketsHttpHandler` com `SslOptions.ClientCertificates`. Documentar a abordagem escolhida no código.
3. **Timeout separado:**
   ```csharp
   using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
   using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
       cancellationToken, timeoutCts.Token);
   ```
   O timeout de 30s é do HTTP. O `cancellationToken` original vem do Hangfire (não deve vencer antes).
4. **`AllowAutoRedirect = false`** — obrigatório para prevenir bypass de SSRF via redirect
5. **Content-Type:** `application/soap+xml; charset=utf-8` para SOAP 1.2
6. **SOAPAction:** header obrigatório conforme WSDL do SEFAZ (ex: `"http://www.portalfiscal.inf.br/nfe/wsdl/NfceAutorizacao4/nfceAutorizacaoNfce"`)

---

### 3.5 `NfceAutorizacaoService.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Sefaz`

**Usings:**
```csharp
using System.Xml;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
```

**Assinatura da classe:**
```csharp
/// <summary>
/// Implementação concreta de ISefazClient.
/// Implementa AMBOS os métodos: SubmeterAutorizacaoAsync e ConsultarNfeAsync.
/// Parsers são testáveis unitariamente sem rede (aceitam string raw).
/// </summary>
public sealed class NfceAutorizacaoService : ISefazClient
{
    private readonly SefazHttpClient _httpClient;
    private readonly SefazEndpointResolver _resolver;
    private readonly INfceXmlBuilder _xmlBuilder;
    private readonly XmlSigner _xmlSigner;
    private readonly ITenantCertificateProvider _certProvider;
    private readonly ILogger<NfceAutorizacaoService> _logger;

    public NfceAutorizacaoService(
        SefazHttpClient httpClient,
        SefazEndpointResolver resolver,
        INfceXmlBuilder xmlBuilder,
        XmlSigner xmlSigner,
        ITenantCertificateProvider certProvider,
        ILogger<NfceAutorizacaoService> logger);

    // --- ISefazClient ---

    public async Task<SefazRetorno> SubmeterAutorizacaoAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        CancellationToken cancellationToken);

    public async Task<SefazConsultaRetorno> ConsultarNfeAsync(
        string chaveAcesso,
        Tenant tenant,
        CancellationToken cancellationToken);

    // --- Parsers testáveis unitariamente (internal para testes) ---

    internal SefazRetorno ParseRetornoAutorizacao(string soapResponse);
    internal SefazConsultaRetorno ParseRetornoConsulta(string soapResponse);

    // --- Helpers privados ---

    private string MontarEnvelopeSoapAutorizacao(string xmlAssinadoBase64, string ufCodigo);
    private string MontarEnvelopeSoapConsulta(string chaveAcesso, string ufCodigo);
}
```

**Pipeline de `SubmeterAutorizacaoAsync`:**
1. Carregar `X509Certificate2` do tenant via `ITenantCertificateProvider.GetCertificateAsync`
2. Construir XML via `INfceXmlBuilder.Construir(documento, tenant)`
3. Assinar XML via `XmlSigner.Assinar(xmlDoc, certificado)`
4. Serializar XML assinado para string
5. Montar envelope SOAP 1.2
6. Resolver URL via `SefazEndpointResolver.ResolverUrl(tenant.ConfiguracaoFiscal.UfCodigo, TipoDocumento.NfCe, ambiente)`
7. Chamar `SefazHttpClient.EnviarSoapAsync`
8. Parsear resposta via `ParseRetornoAutorizacao`

**Pipeline de `ConsultarNfeAsync`:**
1. Carregar certificado do tenant
2. Montar envelope SOAP de consulta (`nfeConsultaNFe`)
3. Resolver URL do serviço de consulta (namespace diferente do de autorização)
4. Chamar `SefazHttpClient.EnviarSoapAsync`
5. Parsear via `ParseRetornoConsulta`

**Regras de `ParseRetornoAutorizacao(string soapResponse)`:**
- Analisa `<cStat>` e `<xMotivo>` do XML de retorno
- `cStat=100` → `Autorizado=true`, extrai `nProt` e `xmlAutorizado`
- `cStat=110` → `EhDenegado=true`
- `cStat=204` → `EhDuplicidade=true`
- XML malformado → retorna `SefazRetorno(Autorizado: false, CStat: "999", XMotivo: "Resposta inválida", ...)` — SEM lançar exceção
- **Método aceita string raw** — não faz I/O, não chama rede → testável sem mock

**Regras de `ParseRetornoConsulta(string soapResponse)`:**
- Analisa `<cStat>` da resposta de consulta
- `cStat=100` → `Encontrado=true, Autorizado=true`
- `cStat=217` (não encontrada) → `Encontrado=false`
- Outros → `Encontrado=true, Autorizado=false`
- XML malformado → `Encontrado=false` com log de warning

**Envelope SOAP 1.2 obrigatório:**
```xml
<soap12:Envelope
    xmlns:soap12="http://www.w3.org/2003/05/soap-envelope"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <soap12:Header/>
  <soap12:Body>
    <nfeDadosMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NfceAutorizacao4">
      <!-- XML NFC-e assinado aqui -->
    </nfeDadosMsg>
  </soap12:Body>
</soap12:Envelope>
```

---

### 3.6 `HangfireDocumentJobQueue.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Scheduling`

**Usings:**
```csharp
using Hangfire;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;
```

**Assinatura da classe:**
```csharp
/// <summary>
/// Adapter que enfileira jobs no Hangfire.
/// SRP: responsabilidade ÚNICA — enfileirar. Não executa.
/// CLASSE SEPARADA de HangfireJobProcessor (violação de SRP se unidas).
/// </summary>
public sealed class HangfireDocumentJobQueue : IDocumentJobQueue
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public HangfireDocumentJobQueue(IBackgroundJobClient backgroundJobClient);

    public void EnqueueProcessing(DocumentoFiscalId documentoId)
    {
        _backgroundJobClient.Enqueue<HangfireJobProcessor>(
            processor => processor.ProcessarDocumentoAsync(documentoId.Value, CancellationToken.None));
    }
}
```

**Invariantes:**
- `IDocumentJobQueue.EnqueueProcessing` recebe apenas `DocumentoFiscalId` — nunca serializa dados do documento
- O argumento do job é exclusivamente `Guid documentoId.Value` — dados sensíveis NUNCA são argumento do Hangfire
- A referência a `CancellationToken.None` no `Enqueue` é o padrão do Hangfire (token de cancelamento é gerenciado internamente pelo framework)

---

### 3.7 `HangfireJobProcessor.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Scheduling`

**Usings:**
```csharp
using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
```

**Assinatura da classe:**
```csharp
/// <summary>
/// Worker que executa o processamento completo de um documento fiscal.
/// SRP: responsabilidade ÚNICA — processar. Não enfileira.
/// CLASSE SEPARADA de HangfireDocumentJobQueue.
/// Recebe APENAS o Guid do documento — dados sensíveis nunca serializados como argumento.
/// </summary>
public sealed class HangfireJobProcessor
{
    private readonly IDocumentoFiscalRepository _documentoRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ISefazClient _sefazClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<HangfireJobProcessor> _logger;

    public HangfireJobProcessor(
        IDocumentoFiscalRepository documentoRepository,
        ITenantRepository tenantRepository,
        ISefazClient sefazClient,
        IUnitOfWork unitOfWork,
        ILogger<HangfireJobProcessor> logger);

    [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 30, 120, 600, 1800, 3600 })]
    public async Task ProcessarDocumentoAsync(Guid documentoId, CancellationToken cancellationToken);
}
```

**Pipeline completo de `ProcessarDocumentoAsync`:**

```
Passo 1: Carregar DocumentoFiscal por documentoId
         → Se não encontrado: lançar InvalidOperationException (job inválido)

Passo 2: Carregar Tenant por documento.TenantId
         → Se não encontrado: lançar InvalidOperationException

Passo 3: documento.IniciarProcessamento()
         → Transição: Enfileirado → Processando
         → Se falhar (já Processando ou outro estado): log warning + retornar sem exception
           (idempotência para retry do Hangfire)

Passo 4: await _unitOfWork.SaveChangesAsync(cancellationToken)
         → Persiste transição para Processando ANTES de chamar SEFAZ

Passo 5: var retorno = await _sefazClient.SubmeterAutorizacaoAsync(documento, tenant, cancellationToken)

Passo 6: switch(retorno.CStat):
         
         "100" → Autorizado:
           documento.Autorizar(retorno.NProt!, xmlAssinado, qrCode, DateTimeOffset.UtcNow)
           tipoTentativa = TipoTentativa.Envio
         
         "110" → Denegado (CNPJ irregular — NUNCA tratar como rejeição comum):
           documento.Denegar(retorno.XMotivo)
           _logger.LogCritical(
               "Denegacao fiscal CNPJ irregular. TenantId={TenantId} CNPJ={Cnpj} xMotivo={XMotivo}",
               documento.TenantId, tenant.Cnpj.Valor, retorno.XMotivo)
           tipoTentativa = TipoTentativa.Envio
           // NÃO lança exception — Hangfire não retenta
         
         "204" → Duplicidade (nota já autorizada anteriormente):
           documento.Rejeitar("Duplicidade: nota já autorizada no SEFAZ (cStat=204)")
           tipoTentativa = TipoTentativa.Envio
           // NÃO lança exception — não retentar
         
         cStat 4xx (exceto 110, 204) → Rejeição definitiva:
           documento.Rejeitar(retorno.XMotivo)
           tipoTentativa = TipoTentativa.Envio
           // NÃO lança exception — não retentar

Passo 7 (apenas para temporários e falhas de rede):
         Se retorno.EhTemporario → lançar exception (Hangfire retenta com backoff)
         
         Se OperationCanceledException ou HttpRequestException (timeout/ConnectionRefused):
           // Consultar SEFAZ ANTES de decidir retry
           var consulta = await _sefazClient.ConsultarNfeAsync(
               documento.ChaveAcesso.Valor, tenant, cancellationToken)
           
           Se consulta.Autorizado && consulta.CStat == "100":
             documento.Autorizar(consulta.NProt!, xmlConsultado, qrCode, DateTimeOffset.UtcNow)
             tipoTentativa = TipoTentativa.Consulta
             // NÃO lança exception — autorizado via consulta
           Senão:
             // Não processado pelo SEFAZ — Hangfire deve retentar
             lançar a exception original

Passo 8: Registrar DeliveryAttempt:
         var attempt = new DeliveryAttempt(
             documento.Id, tipoTentativa, AttemptedAt: DateTimeOffset.UtcNow,
             Success: retorno.Autorizado, ResponseCode: retorno.CStat,
             ResponseMessage: retorno.XMotivo, ElapsedMs: medidoViaStopwatch)

Passo 9: await _unitOfWork.SaveChangesAsync(cancellationToken)
         → Persiste status final + DeliveryAttempt + domain events via DomainEventsInterceptor
```

**Invariantes críticas:**
- `cStat=110` log SEMPRE em nível `Critical` — nunca `Error` ou `Warning`
- `cStat=204` e `cStat=4xx`: `Rejeitar` sem exception — Hangfire NÃO retenta
- Timeout/ConnectionRefused: consultar ANTES de retentar — prevenção de duplicidade
- `DeliveryAttempt.TipoTentativa = Consulta` quando autorizado via `ConsultarNfeAsync`
- Apenas `Guid documentoId` como argumento — dados sensíveis NUNCA no Dashboard do Hangfire

---

### 3.8 `ReconciliacaoJobProcessor.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Scheduling`

**Usings:**
```csharp
using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
```

**Assinatura da classe:**
```csharp
/// <summary>
/// Job periódico: a cada 5 minutos.
/// Reconcilia documentos presos em status Processando há mais de 10 minutos.
/// Consulta SEFAZ para confirmar situação real.
/// DEPENDE de ISefazClient concreto (Fase 7) — nunca executar antes desta fase.
/// </summary>
public sealed class ReconciliacaoJobProcessor
{
    private readonly IDocumentoFiscalRepository _documentoRepository;
    private readonly ISefazClient _sefazClient;
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReconciliacaoJobProcessor> _logger;

    public ReconciliacaoJobProcessor(
        IDocumentoFiscalRepository documentoRepository,
        ISefazClient sefazClient,
        ITenantRepository tenantRepository,
        IUnitOfWork unitOfWork,
        ILogger<ReconciliacaoJobProcessor> logger);

    /// <summary>
    /// Chamado pelo Hangfire a cada 5 minutos via AddOrUpdateRecurringJob.
    /// Cron: "*/5 * * * *"
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task ProcessarReconciliacaoAsync(CancellationToken cancellationToken);
}
```

**Pipeline de `ProcessarReconciliacaoAsync`:**
1. `var documentosTravados = await _documentoRepository.GetProcessandoAntigoAsync(TimeSpan.FromMinutes(10), cancellationToken)`
2. Para cada documento:
   a. Carregar Tenant correspondente
   b. `var consulta = await _sefazClient.ConsultarNfeAsync(documento.ChaveAcesso.Valor, tenant, cancellationToken)`
   c. Se `consulta.Autorizado && consulta.CStat == "100"`:
      - `documento.Autorizar(consulta.NProt!, xmlProtocolo, qrCode, DateTimeOffset.UtcNow)`
      - Log Information com DocumentoId e NProt
   d. Senão (não encontrado ou rejeição definitiva):
      - `documento.Falhar()`
      - Log Warning com DocumentoId (gap de numeração aceito pelo SEFAZ)
   e. `await _unitOfWork.SaveChangesAsync(cancellationToken)`
   f. Registrar `DeliveryAttempt` com `TipoTentativa=Consulta`

**Registro no Hangfire:**
```csharp
// Em DependencyInjection.cs ou no startup
RecurringJob.AddOrUpdate<ReconciliacaoJobProcessor>(
    "reconciliacao-documentos",
    job => job.ProcessarReconciliacaoAsync(CancellationToken.None),
    "*/5 * * * *");
```

**`[DisableConcurrentExecution]`** previne execução simultânea em múltiplas instâncias.

---

### 3.9 `DependencyInjection.cs` (atualização)

**Namespace:** `VisuFiscalHub.Infrastructure`

**Adições para Fase 7:**
```csharp
public static IServiceCollection AddSefazInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // SefazEndpointResolver — singleton (dicionários estáticos)
    services.AddSingleton<SefazEndpointResolver>();

    // SefazHttpClient via IHttpClientFactory — NUNCA new HttpClientHandler por chamada
    services.AddHttpClient<SefazHttpClient>()
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
            // mTLS: certificado adicionado por request (ver SefazHttpClient)
        });

    // ISefazClient
    services.AddScoped<ISefazClient, NfceAutorizacaoService>();

    // IDocumentJobQueue
    services.AddScoped<IDocumentJobQueue, HangfireDocumentJobQueue>();

    // HangfireJobProcessor — registrado para resolução pelo IBackgroundJobClient
    services.AddScoped<HangfireJobProcessor>();

    // ReconciliacaoJobProcessor
    services.AddScoped<ReconciliacaoJobProcessor>();

    return services;
}
```

---

## 4. Fluxos e Diagramas

### 4.1 Pipeline de Envio ao SEFAZ

```
IssueDocumentCommandHandler
  └── IDocumentJobQueue.EnqueueProcessing(documentoId)
         │
         ▼ (Hangfire executa assincronamente)
HangfireJobProcessor.ProcessarDocumentoAsync(Guid)
  ├── [1] Carregar DocumentoFiscal + Tenant do banco
  ├── [2] IniciarProcessamento() → Enfileirado→Processando
  ├── [3] SaveChanges (Processando persistido)
  ├── [4] ISefazClient.SubmeterAutorizacaoAsync
  │       └── NfceAutorizacaoService
  │               ├── ITenantCertificateProvider.GetCertificateAsync
  │               ├── INfceXmlBuilder.Construir
  │               ├── XmlSigner.Assinar
  │               ├── SefazHttpClient.EnviarSoapAsync (SOAP/mTLS)
  │               └── ParseRetornoAutorizacao(soapResponse)
  ├── [5] Switch cStat:
  │       ├── 100 → Autorizar() + evento
  │       ├── 110 → Denegar() + Critical log + evento
  │       ├── 204 → Rejeitar("Duplicidade") sem exception
  │       ├── 4xx → Rejeitar(xMotivo) sem exception
  │       ├── 3xx/5xx → throw (Hangfire retenta)
  │       └── Timeout/ConnRefused:
  │               └── ConsultarNfeAsync
  │                       ├── cStat=100 → Autorizar() com TipoTentativa=Consulta
  │                       └── Não processado → throw (Hangfire retenta)
  ├── [6] Registrar DeliveryAttempt
  └── [7] SaveChanges (estado final + domain events → outbox)
```

### 4.2 Roteamento por GrupoAutorizador

```
ufCodigo=28 (SE)
  └── _ufParaGrupo[28] = SVRS
        └── _urlsAutorizacao[(SVRS, NfCe, Homologacao)]
              = "https://nfce-homologacao.svrs.rs.gov.br/ws/..."

ufCodigo=35 (SP)
  └── _ufParaGrupo[35] = SefazSP
        └── _urlsAutorizacao[(SefazSP, NfCe, Producao)]
              = "https://nfe.fazenda.sp.gov.br/ws/nfceautorizacao4.asmx"

ufCodigo=13 (AM) — desenvolvimento
  └── _ufParaGrupo[13] = SandboxAM
        └── _urlsAutorizacao[(SandboxAM, NfCe, Homologacao)]
              = "https://homnfce.sefaz.am.gov.br/nfceweb/services/NfceAutorizacao4.asmx"
```

### 4.3 Classificação de cStat

```
cStat recebido do SEFAZ
  │
  ├── "100" → AUTORIZADO — único sucesso
  ├── "101" → Cancelado (definitivo)
  ├── "110" → DENEGADO (CNPJ irregular)
  │           ≠ Rejeitado — tratamento OBRIGATORIAMENTE diferente
  │           → Critical log + DocumentoFiscalDenegadoEvent
  ├── "204" → Duplicidade (nota já autorizada)
  │           → Rejeitar sem retry
  ├── "3xx" → Serviço temporariamente indisponível → retry
  ├── "4xx" (exceto 110, 204)
  │           → Rejeição definitiva → sem retry
  ├── "5xx" → Infraestrutura temporária → retry
  └── Timeout/ConnRefused
              → Consultar nfeConsultaNFe ANTES de decidir retry
```

---

## 5. Checklist de Conclusão

- [ ] `GrupoAutorizador` enum tem exatamente 8 valores: SVRS, SefazSP, SefazMG, SefazRS, SefazPR, SefazBA, SefazMT, SandboxAM
- [ ] `SefazEndpointResolver._ufParaGrupo` contém todos os 27 códigos IBGE de UF
- [ ] SE (28) → SVRS confirmado no dicionário
- [ ] `SefazHttpClient` registrado via `services.AddHttpClient<SefazHttpClient>()` — NUNCA `new HttpClientHandler` por chamada
- [ ] `AllowAutoRedirect = false` no handler do `SefazHttpClient`
- [ ] `NfceAutorizacaoService` implementa `ISefazClient` com AMBOS os métodos
- [ ] `ParseRetornoAutorizacao` e `ParseRetornoConsulta` são métodos `internal` que aceitam `string` raw
- [ ] `HangfireDocumentJobQueue` e `HangfireJobProcessor` são arquivos/classes SEPARADOS
- [ ] `HangfireJobProcessor` recebe apenas `Guid documentoId` no argumento do job
- [ ] `cStat=110` → `_logger.LogCritical(...)` + `documento.Denegar(motivo)` + sem exception
- [ ] `cStat=204` → `documento.Rejeitar(...)` + sem exception (sem retry)
- [ ] `cStat=4xx` → `documento.Rejeitar(...)` + sem exception (sem retry)
- [ ] Timeout/ConnectionRefused → `ConsultarNfeAsync` executada ANTES de lançar exception
- [ ] `ReconciliacaoJobProcessor` tem `[DisableConcurrentExecution]`
- [ ] `ReconciliacaoJobProcessor` registrado com cron `"*/5 * * * *"`
- [ ] `IDocumentJobQueue` registrado como `HangfireDocumentJobQueue` no DI
- [ ] Smoke test com `[SkippableFact]` para sandbox AM preparado na Fase 10
