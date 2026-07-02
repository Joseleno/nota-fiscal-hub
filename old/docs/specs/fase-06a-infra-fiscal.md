# Spec Técnica — Fase 6a: Infraestrutura Fiscal (Criptografia, Assinatura XML, Tributação)

**Versão:** 1.0
**Data:** 2026-05-12
**Dependências:** Fase 1 (Domain) — APENAS. Pode ser executada em paralelo à Fase 2.
**Esforço estimado:** M (3–5 dias)

---

## 1. Visão Geral da Fase

### Objetivo

Implementar os componentes técnicos de infraestrutura fiscal que não dependem de banco de dados. Esta fase produz os building blocks usados em Fase 7 (Integração SEFAZ): criptografia AES-256-GCM para certificados e secrets, assinatura digital XML com RSA-SHA1 + C14N, construção do XML NFC-e conforme schema SEFAZ, cálculo de tributação por CRT e geração do QR Code.

### Por que sem banco

Estes componentes são algoritmos puros ou leem configuração de variáveis de ambiente e embedded resources. Nenhum deles acessa `ApplicationDbContext` diretamente. Isso permite desenvolvimento e teste em paralelo com a configuração do banco (Fase 2).

### Dependências obrigatórias resolvidas antes desta fase

| Artefato | De onde vem |
|---|---|
| `DocumentoFiscal`, `Tenant`, `ItemDocumento` | Fase 1 — Domain Entities |
| `ConfiguracaoFiscal`, `Produto`, `Tributo`, `Pagamento` | Fase 1 — Domain Value Objects |
| `RegimeTributario`, `AmbienteSefaz`, `TipoDocumento` | Fase 1 — Domain Enums |
| `ChaveAcesso`, `QrCode` | Fase 1 — Domain Value Objects |
| `ICertificateEncryptionService`, `INfceXmlBuilder` | Fase 1 — Application Interfaces |
| `ITributacaoCalculator`, `IQrCodeGenerator` | Fase 1 — Application Interfaces |

### Critério de Conclusão

- `XmlSigner.Assinar(...)` produz `<Transform Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315">`
- `NfceXmlBuilder.Construir(...)` gera XML com `procEmi=3`, `verProc="VisuFiscalHub 1.0"`, `cIdToken`, sem `<dest>` quando consumidor é nulo
- `NfceXmlBuilder.Construir(...)` passa validação XSD `nfe_v4.00.xsd`
- `TributacaoCalculator` cobre CRT 1 (CSOSN 400/500/102), CRT 2 (CSOSN 900 com base de cálculo), CRT 3 (CST)
- `QrCodeGenerator` produz hash SHA-1 verificável contra valor pré-computado externamente
- `UfFusoHorario.Mapa` contém todos os 27 códigos IBGE de UF com offsets corretos
- `IBPTService` retorna 0,00 com `Log.Warning` para NCM inválido (nunca lança exceção)
- `CertificateEncryptionService` round-trip encrypt/decrypt preserva bytes originais

---

## 2. Árvore de Arquivos

```
src/VisuFiscalHub.Infrastructure/
  Fiscal/
    Certificates/
      CertificateEncryptionService.cs
    XmlBuilder/
      NfceXmlBuilder.cs
      UfFusoHorario.cs
    XmlSigner.cs
    TributacaoCalculator.cs
    IBPTService.cs
    IBPTHealthCheck.cs
    QrCodeGenerator.cs
```

**Total: 8 arquivos .cs**

Adicionalmente, um embedded resource no projeto Infrastructure:
```
src/VisuFiscalHub.Infrastructure/
  Fiscal/
    Resources/
      ibpt_tabela.csv          ← Embedded resource (não é .cs)
```

---

## 3. Especificação por Arquivo

---

### 3.1 `CertificateEncryptionService.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/Certificates/CertificateEncryptionService.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.Certificates`

**Usings:**
```csharp
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
```

**Assinatura:**
```csharp
public sealed class CertificateEncryptionService : ICertificateEncryptionService
{
    private readonly byte[] _key;   // 32 bytes — AES-256
    private readonly ILogger<CertificateEncryptionService> _logger;

    public CertificateEncryptionService(
        IConfiguration configuration,
        ILogger<CertificateEncryptionService> logger);

    // Overload principal: bytes → bytes
    public byte[] Encrypt(byte[] data);
    public byte[] Decrypt(byte[] encryptedData);

    // Overloads para CSC e webhookSecret (strings UTF-8)
    public byte[] EncryptString(string text);
    public string DecryptToString(byte[] encryptedData);
}
```

**Layout do byte[] criptografado (obrigatório):**

```
[ nonce: 12 bytes ][ tag: 16 bytes ][ ciphertext: N bytes ]
   0..11              12..27           28..28+N-1
```

**Invariantes críticas:**

- Chave AES lida de `CERT__EncryptionKey` (variável de ambiente) em Base64, esperando 32 bytes ao decodificar
- `nonce` gerado com `RandomNumberGenerator.GetBytes(12)` A CADA CHAMADA de `Encrypt` — NUNCA reutilizar
- Tag GCM tem sempre 16 bytes (padrão AES-GCM)
- Se `CERT__EncryptionKey` estiver ausente ou resultar em chave != 32 bytes após decodificação → lançar `InvalidOperationException` no construtor (falha no startup)
- Reutilização de nonce com AES-GCM compromete TODA a confidencialidade do ciphertext

**Implementação do construtor:**
```csharp
public CertificateEncryptionService(IConfiguration configuration, ILogger<...> logger)
{
    var base64Key = configuration["CERT__EncryptionKey"]
        ?? throw new InvalidOperationException("CERT__EncryptionKey não configurada.");
    _key = Convert.FromBase64String(base64Key);
    if (_key.Length != 32)
        throw new InvalidOperationException(
            $"CERT__EncryptionKey deve decodificar para 32 bytes. Obtido: {_key.Length}.");
    _logger = logger;
}
```

**Implementação de Encrypt (esboço):**
```csharp
public byte[] Encrypt(byte[] data)
{
    var nonce = RandomNumberGenerator.GetBytes(12);        // a cada chamada
    var tag = new byte[16];
    var ciphertext = new byte[data.Length];

    using var aes = new AesGcm(_key, tagSizeInBytes: 16);
    aes.Encrypt(nonce, data, ciphertext, tag);

    // Layout: nonce(12) + tag(16) + ciphertext
    var result = new byte[12 + 16 + ciphertext.Length];
    Buffer.BlockCopy(nonce, 0, result, 0, 12);
    Buffer.BlockCopy(tag, 0, result, 12, 16);
    Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);
    return result;
}
```

**Implementação de Decrypt (esboço):**
```csharp
public byte[] Decrypt(byte[] encryptedData)
{
    var nonce      = encryptedData[0..12];
    var tag        = encryptedData[12..28];
    var ciphertext = encryptedData[28..];
    var plaintext  = new byte[ciphertext.Length];

    using var aes = new AesGcm(_key, tagSizeInBytes: 16);
    aes.Decrypt(nonce, ciphertext, tag, plaintext);
    return plaintext;
}
```

**Notas de implementação:**
- Registrado como `Singleton` (chave lida uma vez no startup)
- `EncryptString` e `DecryptToString` usam `System.Text.Encoding.UTF8`
- NUNCA usar `AesGcm` com `tagSizeInBytes` diferente de 16

---

### 3.2 `XmlSigner.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/XmlSigner.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal`

**Usings:**
```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
```

**Assinatura:**
```csharp
public sealed class XmlSigner
{
    // Constante: URL exata para C14N exclusiva (NÃO ExcC14N)
    private const string C14NUrl = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";

    public XmlDocument Assinar(XmlDocument xmlDoc, X509Certificate2 certificado, string chaveAcesso);
}
```

**Invariantes críticas de implementação:**

1. **Canonicalização**: usar `XmlDsigC14NTransform` com URL `http://www.w3.org/TR/2001/REC-xml-c14n-20010315`
   - NUNCA `XmlDsigExcC14NTransform` (Exclusive C14N) — o SEFAZ rejeita
   - A URL deve aparecer no XML gerado como `<Transform Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315">`

2. **Digest**: `SignedXml.XmlDsigSHA1Url` = `"http://www.w3.org/2000/09/xmldsig#sha1"`
   - SHA-1 é o único digest aceito pelo SEFAZ para NFC-e/NF-e (DA-04)
   - SHA-256 resulta em rejeição no SEFAZ

3. **Algoritmo de assinatura**: `SignedXml.XmlDsigRSASHA1Url` = `"http://www.w3.org/2000/09/xmldsig#rsa-sha1"`

4. **Reference URI**: `"#NFe" + chaveAcesso` — aponta para o elemento `infNFe` pelo `Id`

**Esboço de implementação:**
```csharp
public XmlDocument Assinar(XmlDocument xmlDoc, X509Certificate2 certificado, string chaveAcesso)
{
    var signedXml = new SignedXml(xmlDoc)
    {
        SigningKey = certificado.GetRSAPrivateKey()
    };
    signedXml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
    signedXml.SignedInfo.CanonicalizationMethod = C14NUrl;

    var reference = new Reference($"#NFe{chaveAcesso}")
    {
        DigestMethod = SignedXml.XmlDsigSHA1Url
    };
    reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
    reference.AddTransform(new XmlDsigC14NTransform());  // URL automática: C14N

    signedXml.AddReference(reference);

    var keyInfo = new KeyInfo();
    keyInfo.AddClause(new KeyInfoX509Data(certificado));
    signedXml.KeyInfo = keyInfo;

    signedXml.ComputeSignature();

    var xmlSignature = signedXml.GetXml();
    xmlDoc.DocumentElement!.AppendChild(xmlDoc.ImportNode(xmlSignature, true));

    return xmlDoc;
}
```

**Notas de implementação:**
- Registrado como `Transient` (sem estado)
- `certificado.GetRSAPrivateKey()` retorna `RSA` que encapsula a chave privada do PFX
- O `XmlDocument` recebido deve ter o namespace correto `xmlns:xsi` para NFC-e
- O elemento `infNFe` deve ter atributo `Id="NFe{chaveAcesso}"` ANTES de chamar `Assinar`

---

### 3.3 `NfceXmlBuilder.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/NfceXmlBuilder.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder`

**Usings:**
```csharp
using System.Xml;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
```

**Assinatura:**
```csharp
public sealed class NfceXmlBuilder : INfceXmlBuilder
{
    private const string NfeNamespace = "http://www.portalfiscal.inf.br/nfe";
    private const string VerNF = "4.00";
    private const string VerProc = "VisuFiscalHub 1.0";

    private readonly ITributacaoCalculator _tributacaoCalculator;
    private readonly IQrCodeGenerator _qrCodeGenerator;
    private readonly IBPTService _ibptService;
    private readonly ILogger<NfceXmlBuilder> _logger;

    public NfceXmlBuilder(
        ITributacaoCalculator tributacaoCalculator,
        IQrCodeGenerator qrCodeGenerator,
        IBPTService ibptService,
        ILogger<NfceXmlBuilder> logger);

    public XmlDocument Construir(DocumentoFiscal documento, Tenant tenant);

    // Extensão para NT 2025.002 (IBS/CBS/IS) — retorna XML inalterado até ativação
    public XmlDocument AdicionarCamposReformaTributaria(XmlDocument xmlDoc, DocumentoFiscal documento);

    // Métodos privados de construção de seções
    private XmlElement ConstruirIde(XmlDocument doc, DocumentoFiscal documento, Tenant tenant);
    private XmlElement ConstruirEmit(XmlDocument doc, Tenant tenant);
    private XmlElement? ConstruirDest(XmlDocument doc, DocumentoFiscal documento);  // null se consumidor ausente
    private XmlElement ConstruirDet(XmlDocument doc, ItemDocumento item, int numeroItem, Tenant tenant);
    private XmlElement ConstruirIcms(XmlDocument doc, ItemDocumento item, RegimeTributario crt);
    private XmlElement ConstruirPis(XmlDocument doc, ItemDocumento item, RegimeTributario crt);
    private XmlElement ConstruirCofins(XmlDocument doc, ItemDocumento item, RegimeTributario crt);
    private XmlElement ConstruirTotal(XmlDocument doc, DocumentoFiscal documento, Tenant tenant);
    private XmlElement ConstruirTransp(XmlDocument doc);
    private XmlElement ConstruirPag(XmlDocument doc, DocumentoFiscal documento);
    private XmlElement ConstruirInfNFeSupl(XmlDocument doc, DocumentoFiscal documento, Tenant tenant);
}
```

**Campos fixos obrigatórios em `<ide>`:**

| Campo | Valor | Origem |
|---|---|---|
| `cUF` | Código IBGE da UF | `tenant.ConfiguracaoFiscal.UfCodigo` |
| `cNF` | 8 dígitos | `documento.ChaveAcesso.Valor[22..30]` (posição na chave) |
| `natOp` | `"VENDA"` | Hardcoded para NFC-e |
| `mod` | `"65"` | Hardcoded |
| `serie` | série do tenant | `tenant.ConfiguracaoFiscal.Serie` (zero-padded 3 chars) |
| `nNF` | número sequencial | `documento.Numero` (zero-padded 9 chars) |
| `dhEmi` | data/hora com offset | `UfFusoHorario.Mapa[ufCodigo]` para offset |
| `tpNF` | `"1"` | Hardcoded (saída) |
| `idDest` | `"1"` | Hardcoded (operação interna) |
| `cMunFG` | código IBGE município | `tenant.Endereco.CodigoMunicipio` |
| `tpImp` | `"4"` | Hardcoded (DANFE NFC-e) |
| `tpEmis` | `"1"` | Hardcoded (normal) |
| `cDV` | dígito verificador | `documento.ChaveAcesso.Valor[43]` |
| `tpAmb` | `"1"` ou `"2"` | `(int)tenant.ConfiguracaoFiscal.Ambiente` |
| `finNFe` | `"1"` | Hardcoded (NF-e normal) |
| `indFinal` | `"1"` | Hardcoded (consumidor final) |
| `indPres` | valor do documento | `documento.IndPresenca` (1, 3, 4 ou 9) |
| `procEmi` | `"3"` | Hardcoded (app do contribuinte via API) |
| `verProc` | `"VisuFiscalHub 1.0"` | Constante `VerProc` |
| `cIdToken` | 6 dígitos zerados | `tenant.CIdToken` (ex: `"000001"`) |

**`dhEmi` — regra crítica de fuso horário:**
```csharp
// CORRETO: offset fixo por lei, NÃO TimeZoneInfo (DST-aware)
var offset = UfFusoHorario.Mapa[tenant.ConfiguracaoFiscal.UfCodigo];
var dhEmi = new DateTimeOffset(DateTime.UtcNow).ToOffset(offset);
// Formato: "yyyy-MM-ddTHH:mm:ssK"  →  ex: "2026-05-11T14:30:00-03:00"
```

**Tributação em `<det>` por CRT:**

```
CRT 1 → Simples Nacional:
  <ICMS><ICMSSN400>
    <orig>{origemMercadoria}</orig>
    <CSOSN>400</CSOSN>
  </ICMSSN400></ICMS>
  <PIS><PISNT><CST>07</CST></PISNT></PIS>
  <COFINS><COFINSNT><CST>07</CST></COFINSNT></COFINS>
  ValorIcms = 0, BaseCalculoIcms = 0

CRT 2 → Simples Nacional Excesso:
  <ICMS><ICMSSN900>
    <orig>{origemMercadoria}</orig>
    <CSOSN>900</CSOSN>
    <modBC>3</modBC>
    <vBC>{baseCalculo:F2}</vBC>
    <pICMS>{aliquota:F4}</pICMS>
    <vICMS>{valorIcms:F2}</vICMS>
  </ICMSSN900></ICMS>
  <PIS><PISAliq>
    <CST>01</CST>
    <vBC>{baseCalculo:F2}</vBC>
    <pPIS>{aliquota:F4}</pPIS>
    <vPIS>{valor:F2}</vPIS>
  </PISAliq></PIS>
  (COFINS análogo ao PIS)

CRT 3 → Regime Normal:
  <ICMS><ICMS00> (ou variante de CST)
    <orig>{origemMercadoria}</orig>
    <CST>{cst}</CST>
    <modBC>3</modBC>
    <vBC>{baseCalculo:F2}</vBC>
    <pICMS>{aliquota:F4}</pICMS>
    <vICMS>{valorIcms:F2}</vICMS>
  </ICMS00></ICMS>
```

**`<dest>` — regra crítica:**
- OMITIDO completamente quando `documento.Consumidor == null`
- Incluído apenas com `<CPF>` quando `documento.Consumidor.Cpf != null`
- CNPJ no destinatário de NFC-e é PROIBIDO (regra desde novembro de 2025)

**`<total>` — campos obrigatórios:**
```
vBC, vICMS, vICMSDeson, vFCP, vBCST, vST, vFCPST, vFCPSTRet,
vProd, vFrete, vSeg, vDesc, vII, vIPI, vIPIDevol, vPIS, vCOFINS,
vOutro, vNF, vTotTrib
```
- `vNF` deve corresponder ao somatório correto (previne rejeição 610)
- `vTotTrib` calculado via `IBPTService.ObterAliquotaAproximada(ncm, uf) * vProd`

**`<infNFeSupl>` — obrigatório para NFC-e:**
```xml
<infNFeSupl>
  <qrCode>{URL completa com hash SHA-1}</qrCode>
  <urlFe>{URL base de consulta do SEFAZ}</urlFe>
</infNFeSupl>
```
- `qrCode` gerado por `IQrCodeGenerator.Gerar(...)`
- `urlFe` via `SefazEndpointResolver.ResolverUrlConsultaQr(ufCodigo, ambiente)` (disponível na Fase 7, pode ser injetado como `ISefazEndpointResolver`)

**`<transporte>` — para NFC-e sem frete:**
```xml
<transp>
  <modFrete>9</modFrete>  <!-- Sem frete -->
</transp>
```

**Notas de implementação:**
- Registrado como `Transient`
- Todos os valores monetários formatados com `F2` (2 casas decimais)
- Alíquotas formatadas com `F4` (4 casas decimais conforme schema)
- XML gerado deve ter `<?xml version="1.0" encoding="UTF-8"?>` e namespace `xmlns="http://www.portalfiscal.inf.br/nfe"`
- O elemento `infNFe` deve ter atributo `Id="NFe{chaveAcesso}"` para que `XmlSigner` referencie corretamente
- `IndPresenca` deve ser lido de `documento.IndPresenca` — nunca hardcoded como 1

---

### 3.4 `UfFusoHorario.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/UfFusoHorario.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder`

**Assinatura:**
```csharp
/// <summary>
/// Mapa de offsets UTC fixos por código IBGE de UF.
/// Offsets fixos por lei (Decreto 11.835/2023 — suspensão permanente do horário de verão).
/// NÃO usar TimeZoneInfo (DST-aware) — SEFAZ exige offset fixo.
/// </summary>
public static class UfFusoHorario
{
    public static readonly IReadOnlyDictionary<int, TimeSpan> Mapa;

    static UfFusoHorario();
}
```

**Mapa completo obrigatório (27 UFs):**

```csharp
// Inicialização no static constructor:
Mapa = new Dictionary<int, TimeSpan>
{
    // UTC-5: Acre
    { 12, TimeSpan.FromHours(-5) },  // AC

    // UTC-4: região Oeste e Amazônica
    { 11, TimeSpan.FromHours(-4) },  // RO
    { 13, TimeSpan.FromHours(-4) },  // AM
    { 14, TimeSpan.FromHours(-4) },  // RR
    { 50, TimeSpan.FromHours(-4) },  // MS
    { 51, TimeSpan.FromHours(-4) },  // MT

    // UTC-3: demais UFs (a maioria)
    { 15, TimeSpan.FromHours(-3) },  // PA
    { 16, TimeSpan.FromHours(-3) },  // AP
    { 17, TimeSpan.FromHours(-3) },  // TO
    { 21, TimeSpan.FromHours(-3) },  // MA
    { 22, TimeSpan.FromHours(-3) },  // PI
    { 23, TimeSpan.FromHours(-3) },  // CE
    { 24, TimeSpan.FromHours(-3) },  // RN
    { 25, TimeSpan.FromHours(-3) },  // PB
    { 26, TimeSpan.FromHours(-3) },  // PE
    { 27, TimeSpan.FromHours(-3) },  // AL
    { 28, TimeSpan.FromHours(-3) },  // SE  ← cStat docs usam SE como exemplo padrão
    { 29, TimeSpan.FromHours(-3) },  // BA
    { 31, TimeSpan.FromHours(-3) },  // MG
    { 32, TimeSpan.FromHours(-3) },  // ES
    { 33, TimeSpan.FromHours(-3) },  // RJ
    { 35, TimeSpan.FromHours(-3) },  // SP
    { 41, TimeSpan.FromHours(-3) },  // PR
    { 42, TimeSpan.FromHours(-3) },  // SC
    { 43, TimeSpan.FromHours(-3) },  // RS
    { 52, TimeSpan.FromHours(-3) },  // GO
    { 53, TimeSpan.FromHours(-3) },  // DF
}.AsReadOnly();
```

**Invariantes:**
- 27 entradas exatas (26 estados + DF)
- Offsets são `TimeSpan`, não strings — usados diretamente em `DateTimeOffset.ToOffset(offset)`
- Fernando de Noronha (subconjunto de PE com UTC-2) não é gerenciado separadamente — NFC-e usa UF do emitente (PE = UTC-3)

**Notas de implementação:**
- `static readonly` — inicializado uma vez na carga do assembly
- Acesso: `UfFusoHorario.Mapa[tenant.ConfiguracaoFiscal.UfCodigo]`
- Lançar `KeyNotFoundException` se UF desconhecida — nunca silenciar (indica bug de configuração de tenant)

---

### 3.5 `TributacaoCalculator.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/TributacaoCalculator.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal`

**Usings:**
```csharp
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;
```

**Assinatura:**
```csharp
public sealed class TributacaoCalculator : ITributacaoCalculator
{
    // CRT 1 — Simples Nacional (três variantes de CSOSN)
    public Tributo CalcularParaCrt1(Produto produto);         // CSOSN 400
    public Tributo CalcularParaCrt1Csosn500(Produto produto); // CSOSN 500
    public Tributo CalcularParaCrt1Csosn102(Produto produto); // CSOSN 102

    // CRT 2 — Simples Nacional Excesso (CSOSN 900 com destaques)
    public Tributo CalcularParaCrt2(
        Produto produto,
        decimal aliquotaIcms,
        decimal aliquotaPis,
        decimal aliquotaCofins);

    // CRT 3 — Regime Normal (CST com base de cálculo)
    public Tributo CalcularParaCrt3(
        Produto produto,
        decimal aliquotaIcms,
        decimal aliquotaPis,
        decimal aliquotaCofins);
}
```

**Regras por método:**

**`CalcularParaCrt1` (CSOSN 400):**
```
TipoIcms = TipoIcms.CSOSN
CsosnOuCst = (int)CSOSN.Csosn400  →  400
AliquotaIcms = 0
BaseCalculoIcms = 0      ← CRÍTICO: sem destaque de ICMS
ValorIcms = 0            ← CRÍTICO: sem destaque de ICMS
CstPis = (int)CstPisCofins.Cst07  → 7 (PISNT)
ValorPis = 0
CstCofins = (int)CstPisCofins.Cst07  → 7 (COFINSNT)
ValorCofins = 0
```

**`CalcularParaCrt1Csosn500`:**
```
CsosnOuCst = 500
AliquotaIcms = 0, BaseCalculoIcms = 0, ValorIcms = 0
CstPis/Cofins = 07 (NT)
```

**`CalcularParaCrt1Csosn102`:**
```
CsosnOuCst = 102
AliquotaIcms = 0, BaseCalculoIcms = 0, ValorIcms = 0
CstPis/Cofins = 07 (NT)
```

**`CalcularParaCrt2` (CSOSN 900):**
```
TipoIcms = TipoIcms.CSOSN
CsosnOuCst = 900
BaseCalculoIcms = produto.ValorUnitario * produto.Quantidade - produto.ValorDesconto
AliquotaIcms = aliquotaIcms
ValorIcms = Math.Round(BaseCalculoIcms * aliquotaIcms / 100, 2)
CstPis = 01 (PISAliq)
ValorPis = Math.Round(BaseCalculoIcms * aliquotaPis / 100, 2)
CstCofins = 01 (COFINSAliq)
ValorCofins = Math.Round(BaseCalculoIcms * aliquotaCofins / 100, 2)
```

**`CalcularParaCrt3` (CST normal):**
```
TipoIcms = TipoIcms.CST
CsosnOuCst = (int)CstIcms.Cst00  (padrão — CRT 3 com tributação plena)
BaseCalculoIcms = produto.ValorUnitario * produto.Quantidade - produto.ValorDesconto
AliquotaIcms = aliquotaIcms
ValorIcms = Math.Round(BaseCalculoIcms * aliquotaIcms / 100, 2)
CstPis = 01
ValorPis = Math.Round(BaseCalculoIcms * aliquotaPis / 100, 2)
CstCofins = 01
ValorCofins = Math.Round(BaseCalculoIcms * aliquotaCofins / 100, 2)
```

**Notas de implementação:**
- Registrado como `Singleton` (sem estado mutável)
- `Math.Round(..., 2, MidpointRounding.AwayFromZero)` para arredondamento fiscal
- As alíquotas de CRT 2 e CRT 3 virão de configuração do Tenant (a definir na Fase 7 — para MVP podem ser parâmetros fixos de 12% ICMS, 0,65% PIS, 3% COFINS para CRT 3)

---

### 3.6 `IBPTService.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/IBPTService.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal`

**Usings:**
```csharp
using System.Reflection;
using Microsoft.Extensions.Logging;
```

**Assinatura:**
```csharp
public sealed class IBPTService
{
    private readonly ILogger<IBPTService> _logger;
    private readonly IReadOnlyDictionary<string, decimal> _tabelaPorNcm;

    public IBPTService(ILogger<IBPTService> logger);

    public decimal ObterAliquotaAproximada(string ncm, string uf);

    private static IReadOnlyDictionary<string, decimal> CarregarTabela();
}
```

**Invariantes críticas:**
- CSV como embedded resource no assembly `VisuFiscalHub.Infrastructure` (build action: `EmbeddedResource`)
- NCM inválido (não numérico, menos de 8 dígitos) ou ausente na tabela → retorna `0.00m` e `_logger.LogWarning(...)`
- NUNCA lançar exceção por NCM inválido — não bloquear emissão por dado de tabela
- `vTotTrib = 0` é aceito tecnicamente pelo SEFAZ mas deve ser monitorado

**Carga da tabela no construtor:**
```csharp
public IBPTService(ILogger<IBPTService> logger)
{
    _logger = logger;
    _tabelaPorNcm = CarregarTabela();
    _logger.LogInformation("IBPT: tabela carregada com {Count} NCMs.", _tabelaPorNcm.Count);
}

private static IReadOnlyDictionary<string, decimal> CarregarTabela()
{
    var assembly = Assembly.GetExecutingAssembly();
    var resourceName = "VisuFiscalHub.Infrastructure.Fiscal.Resources.ibpt_tabela.csv";
    using var stream = assembly.GetManifestResourceStream(resourceName)!;
    using var reader = new StreamReader(stream);
    // Parse CSV: coluna 0 = NCM (8 dígitos), coluna relevante = alíquota total aproximada
    // ...
    return dict.AsReadOnly();
}
```

**Comportamento de `ObterAliquotaAproximada`:**
```csharp
public decimal ObterAliquotaAproximada(string ncm, string uf)
{
    if (string.IsNullOrWhiteSpace(ncm)
        || ncm.Length != 8
        || !ncm.All(char.IsDigit))
    {
        _logger.LogWarning("IBPT: NCM inválido '{Ncm}'. Retornando alíquota 0,00.", ncm);
        return 0.00m;
    }

    if (!_tabelaPorNcm.TryGetValue(ncm, out var aliquota))
    {
        _logger.LogWarning("IBPT: NCM '{Ncm}' não encontrado na tabela. Retornando alíquota 0,00.", ncm);
        return 0.00m;
    }

    return aliquota;
}
```

**Notas de implementação:**
- Registrado como `Singleton`
- Tabela CSV deve ter linha de cabeçalho ignorada
- O cálculo do `vTotTrib` no `NfceXmlBuilder`: `aliquota * vProd / 100` arredondado em 2 casas

---

### 3.7 `IBPTHealthCheck.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/IBPTHealthCheck.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal`

**Usings:**
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
```

**Assinatura:**
```csharp
public sealed class IBPTHealthCheck : IHealthCheck
{
    private const int MesesParaAlerta = 7;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IBPTHealthCheck> _logger;

    public IBPTHealthCheck(IConfiguration configuration, ILogger<IBPTHealthCheck> logger);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default);
}
```

**Lógica do health check:**
```csharp
public Task<HealthCheckResult> CheckHealthAsync(...)
{
    var dataReferenciaStr = _configuration["IBPT__DataReferencia"];
    if (!DateOnly.TryParse(dataReferenciaStr, out var dataReferencia))
        return Task.FromResult(HealthCheckResult.Degraded(
            "IBPT__DataReferencia não configurada ou inválida."));

    var mesesDesdeAtualizacao =
        (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - dataReferencia.DayNumber) / 30;

    if (mesesDesdeAtualizacao > MesesParaAlerta)
    {
        var versao = _configuration["IBPT__TabelaVersao"] ?? "desconhecida";
        return Task.FromResult(HealthCheckResult.Degraded(
            $"Tabela IBPT versão '{versao}' tem {mesesDesdeAtualizacao} meses. " +
            $"Atualizar em até {MesesParaAlerta} meses."));
    }

    return Task.FromResult(HealthCheckResult.Healthy("Tabela IBPT atualizada."));
}
```

**Notas de implementação:**
- Registrado em `AddHealthChecks()` com nome `"ibpt"` e tag `"fiscal"`
- Status `Degraded` (não `Unhealthy`) — emissão continua funcionando com tabela antiga
- Alerta deve aparecer no endpoint `GET /health`

---

### 3.8 `QrCodeGenerator.cs`

**Caminho:** `src/VisuFiscalHub.Infrastructure/Fiscal/QrCodeGenerator.cs`

**Namespace:** `VisuFiscalHub.Infrastructure.Fiscal`

**Usings:**
```csharp
using System.Security.Cryptography;
using System.Text;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;
```

**Assinatura:**
```csharp
public sealed class QrCodeGenerator : IQrCodeGenerator
{
    public string Gerar(
        ChaveAcesso chaveAcesso,
        AmbienteSefaz ambiente,
        string csc,
        string cIdToken,
        string urlConsultaSefaz);
}
```

**Fórmula obrigatória (NT 2019.001 v1.50):**

```
Entrada para SHA-1: "{chaveAcesso}|2|{(int)ambiente}|{csc}"

Onde:
  - chaveAcesso = string de 44 dígitos
  - |2| = versão do QR Code (LITERAL FIXO "2", NÃO confundir com tpAmb)
  - (int)ambiente = 1 (produção) ou 2 (homologação)
  - csc = Código de Segurança do Contribuinte (ex: "0123456789" no sandbox)

cHashQRCode = SHA1(entrada).ToHexString().ToUpperInvariant()

URL final:
  "{urlConsultaSefaz}?p={chaveAcesso}|2|{(int)ambiente}|{cHashQRCode}"
```

**REGRA CRÍTICA: CSC NUNCA aparece na URL final.**

**Implementação obrigatória:**
```csharp
public string Gerar(
    ChaveAcesso chaveAcesso,
    AmbienteSefaz ambiente,
    string csc,
    string cIdToken,
    string urlConsultaSefaz)
{
    var tpAmb = (int)ambiente;
    var stringParaHash = $"{chaveAcesso.Valor}|2|{tpAmb}|{csc}";

    var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(stringParaHash));
    var cHashQRCode = Convert.ToHexString(hashBytes).ToUpperInvariant();

    // CSC NÃO entra na URL — apenas o hash
    return $"{urlConsultaSefaz}?p={chaveAcesso.Valor}|2|{tpAmb}|{cHashQRCode}";
}
```

**Valor de teste para validação (sandbox AM):**
```
chaveAcesso = "35090614200167140065125001000001800100000097"
tpAmb = 2 (homologação)
csc = "0123456789"
cIdToken = "000001"
stringParaHash = "35090614200167140065125001000001800100000097|2|2|0123456789"

Calcular externamente: echo -n "35090614200167140065125001000001800100000097|2|2|0123456789" | sha1sum
Resultado: deve ser hardcoded em [InlineData] do teste ANTES de escrever o código
```

**Notas de implementação:**
- Registrado como `Singleton` (sem estado)
- `SHA1.HashData(bytes)` — API estática, sem instância descartável
- `Convert.ToHexString` retorna maiúsculas sem separadores (padrão .NET 5+)
- `cIdToken` não é usado na URL mas é incluído no `<ide>` do XML

---

## 4. Fluxo de Dados

```
DocumentoFiscal + Tenant
         │
         ▼
[NfceXmlBuilder.Construir()]
    │
    ├─► UfFusoHorario.Mapa[ufCodigo] → offset de fuso para dhEmi
    │
    ├─► TributacaoCalculator.CalcularParaCrt{N}() por item
    │       └─► Tributo { TipoIcms, CsosnOuCst, ValorIcms, BaseCalculo, CstPis, ValorPis, ... }
    │
    ├─► IBPTService.ObterAliquotaAproximada(ncm, uf) por item
    │       └─► decimal aliquota → vTotTrib
    │
    ├─► QrCodeGenerator.Gerar(chaveAcesso, ambiente, csc, cIdToken, urlConsulta)
    │       │
    │       └─► SHA1("{chave}|2|{tpAmb}|{csc}")
    │               └─► cHashQRCode
    │               └─► URL: "{urlConsulta}?p={chave}|2|{tpAmb}|{cHashQRCode}"
    │                          (CSC ausente da URL)
    │
    └─► XmlDocument (NFC-e não assinado)

[XmlSigner.Assinar(xmlDoc, X509Certificate2, chaveAcesso)]
    │
    ├─► SignedXml com:
    │     - Transform: C14N (http://www.w3.org/TR/2001/REC-xml-c14n-20010315)
    │     - DigestMethod: SHA-1
    │     - SignatureMethod: RSA-SHA1
    │     - Reference URI: #NFe{chaveAcesso}
    │
    └─► XmlDocument assinado

[CertificateEncryptionService] (operação separada — para armazenar/recuperar certificado)
    │
    ├─► Encrypt(pfxBytes):
    │     nonce = RandomNumberGenerator.GetBytes(12)   ← a cada chamada
    │     AesGcm.Encrypt(nonce, pfxBytes, ciphertext, tag)
    │     → byte[]: [nonce:12][tag:16][ciphertext:N]
    │
    └─► Decrypt(encryptedData):
          nonce = encryptedData[0..12]
          tag = encryptedData[12..28]
          ciphertext = encryptedData[28..]
          AesGcm.Decrypt → pfxBytes
```

---

## 5. Checklist de Conclusão

### CertificateEncryptionService
- [ ] `Encrypt` gera nonce com `RandomNumberGenerator.GetBytes(12)` a cada chamada
- [ ] Layout do byte[]: nonce(12) + tag(16) + ciphertext concatenados nesta ordem
- [ ] `Decrypt` separa nonce, tag e ciphertext pelas posições corretas
- [ ] Chave de 32 bytes validada no construtor — falha no startup se ausente ou tamanho incorreto
- [ ] Round-trip encrypt/decrypt preserva bytes originais (testável sem banco)
- [ ] `AesGcm` inicializado com `tagSizeInBytes: 16`

### XmlSigner
- [ ] `<Transform Algorithm="http://www.w3.org/TR/2001/REC-xml-c14n-20010315">` presente no XML gerado
- [ ] `DigestMethod` = `"http://www.w3.org/2000/09/xmldsig#sha1"` (SHA-1, não SHA-256)
- [ ] `SignatureMethod` = `"http://www.w3.org/2000/09/xmldsig#rsa-sha1"`
- [ ] `Reference URI` = `"#NFe{chaveAcesso}"` (prefixo "NFe" obrigatório)
- [ ] `SignedXml.CheckSignature(certificado.GetRSAPublicKey())` retorna `true` após assinatura

### NfceXmlBuilder
- [ ] `procEmi=3` presente em `<ide>`
- [ ] `verProc="VisuFiscalHub 1.0"` presente em `<ide>`
- [ ] `cIdToken` presente em `<ide>` com 6 dígitos (zero-padded)
- [ ] `dhEmi` com offset de fuso correto (ex: `-03:00` para SE, `-04:00` para AM, `-05:00` para AC)
- [ ] `<dest>` OMITIDO quando consumidor é null
- [ ] CRT 1 → `<ICMSSN400>` com `ValorIcms=0` e `BaseCalculoIcms=0`
- [ ] CRT 2 → `<ICMSSN900>` com campos numéricos preenchidos
- [ ] `<infNFeSupl>` presente com `<qrCode>` e `<urlFe>`
- [ ] XML passa validação XSD `nfe_v4.00.xsd` (sem erros de severity `Error`)

### TributacaoCalculator
- [ ] CRT 1 CSOSN 400: `ValorIcms == 0`, `BaseCalculoIcms == 0`, PIS/COFINS CST 07
- [ ] CRT 2 CSOSN 900: `BaseCalculoIcms > 0`, `ValorIcms = round(base * aliquota / 100, 2)`
- [ ] CRT 3 CST 00: `BaseCalculoIcms > 0`, `ValorIcms = round(base * aliquota / 100, 2)`

### IBPTService
- [ ] NCM inválido (não 8 dígitos numéricos) retorna `0.00m` com `LogWarning` — sem exceção
- [ ] NCM ausente na tabela retorna `0.00m` com `LogWarning` — sem exceção
- [ ] Versão da tabela logada no startup

### IBPTHealthCheck
- [ ] Retorna `Degraded` (não `Unhealthy`) quando tabela > 7 meses

### QrCodeGenerator
- [ ] `|2|` é versão do QR Code — LITERAL fixo, não variável `tpAmb`
- [ ] CSC nunca aparece na URL final
- [ ] URL formato: `{urlConsulta}?p={chave}|2|{tpAmb}|{cHashQRCode}`
- [ ] Hash verificável contra valor pré-computado externamente (teste TDD com `[InlineData]`)

### UfFusoHorario
- [ ] Exatamente 27 entradas (26 estados + DF)
- [ ] AC = UTC-5 (cUF 12)
- [ ] AM = UTC-4 (cUF 13)
- [ ] SE = UTC-3 (cUF 28)
- [ ] Implementado com `TimeSpan`, não strings

### Clean Architecture
- [ ] Nenhum serviço desta fase acessa `ApplicationDbContext` diretamente
- [ ] Nenhum serviço desta fase depende de `VisuFiscalHub.Application` via projeto (apenas via interfaces)
- [ ] `IBPTService`, `QrCodeGenerator`, `TributacaoCalculator` registrados em `DependencyInjection.cs` da Infrastructure
- [ ] `IBPTHealthCheck` registrado em `AddHealthChecks()` na `DependencyInjection.cs`
