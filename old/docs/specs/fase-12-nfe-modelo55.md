# Fase 12 — NF-e Modelo 55

**Versão:** 1.0
**Data:** 2026-05-24

---

## Objetivo

Adicionar suporte à emissão de NF-e (Nota Fiscal Eletrônica, Modelo 55) para operações B2B com destinatário variável (CNPJ ou CPF + endereço completo). O fluxo de autorização, cancelamento, reconciliação, webhook e outbox é 100% reaproveitado sem alteração de comportamento — apenas os componentes específicos de construção de XML, resolução de endpoints SEFAZ e modelo de dados do destinatário precisam ser estendidos.

**Escopo desta fase:**
- `finNFe=1` (NF-e normal) apenas
- Destinatário variável: CNPJ ou CPF, razão social, IE, endereço completo
- Pagamento à vista (sem parcelamento / `cobr` / duplicatas)
- `modFrete` configurável, sem dados de transportadora
- `natOp` configurável pelo caller (obrigatório para NF-e)
- Série NF-e separada da série NFC-e por Tenant
- Cancellation window: 24h para NF-e (vs 30 min para NFC-e)

**Fora de escopo:**
- `finNFe` 2, 3, 4 (complementar, ajuste, devolução)
- Transportadora, veículo, lacres, volumes
- Parcelamento (duplicatas)
- Contingência offline (tpEmis≠1)
- NFS-e

---

## Decisões de design

### D1 — Um único `FiscalDocumentXmlBuilder` com branching interno

`NfceXmlBuilder` é renomeado para `FiscalDocumentXmlBuilder` (interface `IFiscalDocumentXmlBuilder`). O método `Construir(DocumentoFiscal, Tenant)` usa `documento.Tipo` internamente para selecionar blocos condicionais. ~80% do código é idêntico entre os dois modelos; os blocos que diferem são extraídos em métodos privados (`BuildIdeNfce`, `BuildIdeNfe`, `BuildDestNfce`, `BuildDestNfe`, `BuildInfNFeSupl`). Alternativa rejeitada: dois builders separados duplicariam ~400 linhas de código XML idêntico.

### D2 — `SerieNfe` separada em `ConfiguracaoFiscal`

NF-e e NFC-e usam séries distintas. `ConfiguracaoFiscal` recebe campo opcional `SerieNfe` (string, mesmo padrão de `Serie`). Se não configurado, o sistema rejeita emissão de NF-e com erro claro. A sequence PostgreSQL para NF-e é criada no mesmo `TenantProvisionadoEventHandler` que cria a de NFC-e, usando o padrão `seq_nfe_{tenantId}`.

### D3 — `NfeDestinatario` como value object, persistido como `jsonb`

Endereço completo do destinatário B2B é armazenado em coluna `nfe_destinatario jsonb` na tabela `documentos_fiscais`. NFC-e docs têm `NULL`. Nunca consultado por sub-campo individualmente nesta fase. EF Core mapeia via owned entity com `HasColumnType("jsonb")`.

### D4 — `IndPresencaValidos` type-aware na entidade

`DocumentoFiscal.Criar` usa set diferente por modelo:
- NFC-e: `{1, 3, 4, 9}` (atual — correto)
- NF-e: `{0, 1, 2, 3, 4, 5, 9}` (conforme schema 4.00)

### D5 — Cancellation window 24h para NF-e

`IniciarCancelamento` passa a derivar o prazo de `Tipo`:
- NFC-e: 30 minutos
- NF-e: 24 horas

### D6 — `SefazEndpointResolver` com tabela NF-e completamente independente

Os métodos `Autorizacao`, `ConsultaProtocolo`, `RetAutorizacao` e `ResolveEvento` recebem parâmetro `TipoDocumento tipo`. O branch NF-e usa tabela de URLs totalmente separada (SVRS NF-e = `nfe.svrs.rs.gov.br`; SVAN para MG, RS, SP, PR; servidores próprios para AM, PA, etc.).

---

## Componentes — o que muda

### Novos

| Arquivo | Descrição |
|---|---|
| `Domain/ValueObjects/NfeDestinatario.cs` | Value object com todos os campos do `<dest>` NF-e |
| `Domain/Errors/DocumentoFiscalErrors.cs` | +2 erros: `DestinatarioObrigatorioParaNfe`, `DestinatarioNaoPermitidoEmNfce` |
| `Application/Documents/Commands/IssueDocument/IssueNfeRequest.cs` | DTO de entrada da API para NF-e (separado do NFC-e) |
| `Infrastructure/Persistence/Migrations/AddNfeSupport.cs` | Adiciona `nfe_destinatario jsonb`, `serie_nfe varchar(3)` ao tenant, índice em `tipo` |

### Modificados

| Arquivo | Mudança |
|---|---|
| `Domain/ValueObjects/ConfiguracaoFiscal.cs` | +`SerieNfe?` field + validação |
| `Domain/Entities/DocumentoFiscal.cs` | +`NfeDestinatario?`, +`NatOp?`, +`ModFrete`; `IndPresencaValidos` type-aware; `IniciarCancelamento` com prazo 24h/30min por `Tipo` |
| `Infrastructure/Fiscal/NfceXmlBuilder.cs` | Renomear → `FiscalDocumentXmlBuilder`; interface `IFiscalDocumentXmlBuilder`; suporte NF-e completo |
| `Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` | +parâmetro `TipoDocumento tipo` em todos os métodos; tabela URLs NF-e |
| `Infrastructure/Fiscal/Sefaz/SefazClient.cs` | Usar `IFiscalDocumentXmlBuilder`; passar `documento.Tipo` ao resolver; `QrCodeUrl` null-safe para NF-e |
| `Infrastructure/Jobs/NfceProcessingJob.cs` | Renomear → `FiscalDocumentProcessingJob`; logs usam `documento.Tipo`; `AutorizarAsync` null-safe para `QrCodeUrl` |
| `Infrastructure/Jobs/CancelamentoJob.cs` | Passar `documento.Tipo` em `ResolveEvento` |
| `Infrastructure/Persistence/Configurations/TenantConfiguration.cs` | Mapear `SerieNfe` |
| `Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs` | Mapear `NfeDestinatario`, `NatOp`, `ModFrete` |
| `Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs` | +`DestinatarioNfe?`, +`NatOp?`, +`ModFrete` |
| `Application/Documents/Commands/IssueDocument/IssueDocumentCommandValidator.cs` | Regras NF-e: destinatário obrigatório, `natOp` obrigatório, `modFrete` 0–9, IE válida |
| `Application/Documents/Commands/IssueDocument/IssueDocumentCommandHandler.cs` | Passar `SerieNfe` para NF-e; passar `NfeDestinatario`, `NatOp`, `ModFrete` ao `DocumentoFiscal.Criar` |
| `Application/Tenants/Commands/CreateTenant/CreateTenantCommand.cs` | +`SerieNfe?` |
| `Application/Tenants/Commands/CreateTenant/CreateTenantCommandHandler.cs` | Criar sequence `seq_nfe_{tenantId}` |
| `Infrastructure/Persistence/SequenceManager.cs` | Suporte a `seq_nfe_{tenantId}` |
| `Api/Program.cs` | +endpoint `POST /api/v1/documentos/nfe`; renomear cron `"reconciliacao-nfce"` → `"reconciliacao-fiscal"` |

---

## Modelo de dados

### `NfeDestinatario` (value object)

```csharp
public sealed record NfeDestinatario
{
    public string CnpjOuCpf { get; }      // 14 dígitos = CNPJ, 11 dígitos = CPF
    public string RazaoSocial { get; }    // 2–60 chars
    public string? Email { get; }         // max 60 chars, opcional
    public int IndIeDest { get; }         // 1=contribuinte com IE, 2=SUFRAMA, 9=não contribuinte
    public string? Ie { get; }            // obrigatória quando IndIeDest=1; null/proibido quando 2/9
    public string Logradouro { get; }     // xLgr, max 60
    public string Numero { get; }         // nro, max 60
    public string? Complemento { get; }  // xCpl, max 60
    public string Bairro { get; }         // xBairro, max 60
    public int CodigoMunicipio { get; }   // cMun, 7 dígitos IBGE
    public string NomeMunicipio { get; }  // xMun, max 60
    public string Uf { get; }             // UF, 2 letras
    public string Cep { get; }            // CEP, 8 dígitos numéricos sem máscara
    // cPais=1058 e xPais="BRASIL" hardcoded no builder — não armazenados
}
```

**Regras de construção:**
- `CnpjOuCpf`: 14 dígitos → validar como CNPJ (dígitos verificadores); 11 dígitos → validar como CPF; outros tamanhos → erro
- `RazaoSocial`: trim, min 2, max 60 chars
- `Email`: se presente, max 60; não valida formato (SEFAZ não valida)
- `IndIeDest=1` → `Ie` obrigatória, não pode ser `"ISENTO"`, deve ser `[0-9]{2,14}`
- `IndIeDest=2` ou `9` → `Ie` deve ser null (zerado no factory se passado)
- `Cep`: strip de formatação, deve ter exatamente 8 dígitos numéricos
- `CodigoMunicipio`: deve ser inteiro positivo (validação de tabela IBGE não incluída nesta fase)

### `DocumentoFiscal` — novos campos

```csharp
public NfeDestinatario? NfeDestinatario { get; private set; }  // null para NFC-e
public string? NatOp { get; private set; }                      // null para NFC-e; obrigatório para NF-e
public ModalidadeFrete ModFrete { get; private set; }           // default SemFrete=9
```

### `ConfiguracaoFiscal` — novo campo

```csharp
public string? SerieNfe { get; }  // null = NF-e não configurada para este tenant
```

### Tabela `documentos_fiscais` — novas colunas

```sql
ALTER TABLE documentos_fiscais
  ADD COLUMN nfe_destinatario jsonb NULL,
  ADD COLUMN nat_op           varchar(60) NULL,
  ADD COLUMN mod_frete        integer NOT NULL DEFAULT 9;

CREATE INDEX ix_documentos_fiscais_tipo ON documentos_fiscais (tipo);
```

### Tabela `tenants` — nova coluna

```sql
ALTER TABLE tenants ADD COLUMN serie_nfe varchar(3) NULL;
```

---

## XML NF-e — diferenças por bloco

### `<ide>` — campos que diferem

| Campo | NFC-e | NF-e |
|---|---|---|
| `mod` | `65` | `55` |
| `tpImp` | `4` | `1` (DANFE retrato) |
| `indFinal` | `1` | `0` |
| `indPres` | passado pelo caller (`{1,3,4,9}`) | passado pelo caller (`{0,1,2,3,4,5,9}`) |
| `natOp` | `"VENDA AO CONSUMIDOR"` hardcoded | passado pelo caller (obrigatório) |
| `dhSaiEnt` | ausente | igual a `dhEmi` (emitir sempre) |

### `<dest>` — bloco completo para NF-e

```xml
<dest>
  <CNPJ>14 dígitos</CNPJ>           <!-- ou <CPF>11 dígitos</CPF> — exclusivos -->
  <xNome>razão social</xNome>        <!-- obrigatório, max 60 -->
  <enderDest>
    <xLgr>logradouro</xLgr>
    <nro>numero</nro>
    <xCpl>complemento</xCpl>          <!-- opcional -->
    <xBairro>bairro</xBairro>
    <cMun>código IBGE</cMun>
    <xMun>nome município</xMun>
    <UF>sigla</UF>
    <CEP>8 dígitos</CEP>
    <cPais>1058</cPais>               <!-- hardcoded -->
    <xPais>BRASIL</xPais>             <!-- hardcoded -->
    <fone>telefone</fone>             <!-- ausente nesta fase -->
  </enderDest>
  <email>email</email>               <!-- opcional -->
  <indIEDest>1|2|9</indIEDest>
  <IE>inscrição estadual</IE>         <!-- somente quando indIEDest=1 -->
</dest>
```

### `<transp>` — para NF-e

```xml
<transp>
  <modFrete>0–9</modFrete>   <!-- valor do campo ModFrete do documento -->
</transp>
```

### `<infNFeSupl>` — NFC-e only

Bloco QR Code e URL de consulta: **não emitido para NF-e**. O guard `tenant.CIdToken is null || tenant.Csc is null` é condicional em `documento.Tipo == NfCe`.

---

## Endpoints SEFAZ NF-e

### Autorizadoras por UF

| Autorizadora | UFs (código IBGE) |
|---|---|
| **SVRS NF-e** (`nfe.svrs.rs.gov.br`) | AC(12), AL(27), AP(16), DF(53), ES(32), PB(25), RJ(33), RN(24), RO(11), RR(14), SC(42), SE(28), TO(17) |
| **SVAN** (`www.nfe.fazenda.gov.br`) | AM(13), BA(29), CE(23), GO(52), MA(21), MS(50), MT(51), PA(15), PE(26), PI(22) |
| **SEFAZ-SP** | SP(35) |
| **SEFAZ-MG** | MG(31) |
| **SEFAZ-PR** | PR(41) |
| **SEFAZ-RS** | RS(43) |

### URLs base NF-e

```
SVRS NF-e Prod:    https://nfe.svrs.rs.gov.br/ws
SVRS NF-e Hom:     https://hom.nfe.svrs.rs.gov.br/ws
SVAN Prod:         https://www.sefazvirtual.fazenda.gov.br
SVAN Hom:          https://hom.sefazvirtual.fazenda.gov.br
SP Prod:           https://nfe.fazenda.sp.gov.br/ws
SP Hom:            https://homologacao.nfe.fazenda.sp.gov.br/ws
MG Prod:           https://nfe.fazenda.mg.gov.br/nfe/services
MG Hom:            https://hnfe.fazenda.mg.gov.br/nfe/services
PR Prod:           https://nfe2.fazenda.pr.gov.br/nfe-services
PR Hom:            https://homologacao.nfe2.fazenda.pr.gov.br/nfe-services
RS Prod:           https://nfe.sefazrs.rs.gov.br/ws
RS Hom:            https://nfe-homologacao.sefazrs.rs.gov.br/ws
AM Prod:           https://nfe.sefaz.am.gov.br/services
AM Hom:            https://hom.sefaz.am.gov.br/services
PA Prod:           https://app.sefa.pa.gov.br/nfe
PA Hom:            https://app.sefa.pa.gov.br/nfe-homologacao
```

---

## Validações — `IssueDocumentCommandValidator`

### Regras adicionais para NF-e

```
When(Tipo == NFe):
  DestinatarioNfe != null               → obrigatório
  NatOp não vazio, max 60 chars         → obrigatório
  ModFrete entre 0 e 9                  → obrigatório
  IndPresenca em {0,1,2,3,4,5,9}        → NF-e permite 0

When(Tipo == NfCe):
  DestinatarioNfe == null               → proibido
  IndPresenca em {1,3,4,9}              → NFC-e (atual)
```

---

## Cancellation — prazo por modelo

```csharp
// DocumentoFiscal.IniciarCancelamento
var prazo = Tipo == TipoDocumento.NfCe
    ? TimeSpan.FromMinutes(30)
    : TimeSpan.FromHours(24);
if (utcNow >= AuthorizedAt.Value.Add(prazo))
    return Result.Failure(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado);
```

---

## API

### `POST /api/v1/documentos/nfe`

**Request:**
```json
{
  "destinatario": {
    "cnpjOuCpf": "12345678000195",
    "razaoSocial": "Empresa Destino Ltda",
    "email": "nfe@empresa.com.br",
    "indIeDest": 1,
    "ie": "123456789",
    "logradouro": "Rua das Flores",
    "numero": "100",
    "complemento": "Sala 1",
    "bairro": "Centro",
    "codigoMunicipio": 3550308,
    "nomeMunicipio": "São Paulo",
    "uf": "SP",
    "cep": "01310100"
  },
  "natOp": "VENDA DE MERCADORIAS",
  "modFrete": 1,
  "itens": [...],
  "pagamentos": [...],
  "indPresenca": 9
}
```

**Response:** `202 Accepted` — idêntico ao NFC-e (`DocumentoId`, `Status=Enfileirado`, header `Location`)

**Erros:** `422` sem destinatário, sem `natOp`, `IndPresenca` inválido, `IndIeDest=1` sem IE; `401/403` padrão

### Sequência de numeração NF-e

O handler usa `_sequenceManager.GetNextNumber("seq_nfe_{tenantId}")` em vez de `"seq_nfce_{tenantId}"`. A sequence é criada pelo `TenantProvisionadoEventHandler` se `tenant.ConfiguracaoFiscal.SerieNfe != null`.

---

## Testes

### Unitários — Domain

| Teste | Cenário |
|---|---|
| `NfeDestinatario_CNPJ_Valido` | CNPJ válido cria value object |
| `NfeDestinatario_CPF_Valido` | CPF válido cria value object |
| `NfeDestinatario_IndIeDest1_SemIE_Erro` | IndIeDest=1 sem IE retorna falha |
| `NfeDestinatario_IndIeDest1_IeIsento_Erro` | IE="ISENTO" com IndIeDest=1 retorna falha |
| `NfeDestinatario_IndIeDest9_ComIE_NulaIe` | IE passada com IndIeDest=9 é nulificada |
| `NfeDestinatario_CepSemMascara` | CEP "01310-100" normalizado para "01310100" |
| `IniciarCancelamento_Nfe_Dentro24h_Sucesso` | NF-e autorizada há 23h pode ser cancelada |
| `IniciarCancelamento_Nfe_Apos24h_Erro` | NF-e autorizada há 25h retorna PrazoDeCancelamentoExpirado |
| `IniciarCancelamento_Nfce_Apos30min_Erro` | NFC-e após 30 min ainda retorna erro (regressão) |
| `DocumentoFiscal_Criar_Nfe_IndPresenca0_Sucesso` | indPres=0 válido para NF-e |
| `DocumentoFiscal_Criar_Nfce_IndPresenca0_Erro` | indPres=0 inválido para NFC-e (regressão) |

### Unitários — Infrastructure/Fiscal

| Teste | Cenário |
|---|---|
| `FiscalDocumentXmlBuilder_Nfe_Mod55` | XML NF-e tem `<mod>55</mod>` |
| `FiscalDocumentXmlBuilder_Nfe_SemInfNFeSupl` | XML NF-e não contém `<infNFeSupl>` |
| `FiscalDocumentXmlBuilder_Nfe_DestCNPJ` | `<dest>` emite `<CNPJ>` quando 14 dígitos |
| `FiscalDocumentXmlBuilder_Nfe_DestCPF` | `<dest>` emite `<CPF>` quando 11 dígitos |
| `FiscalDocumentXmlBuilder_Nfe_DhSaiEnt` | XML NF-e contém `<dhSaiEnt>` igual a `<dhEmi>` |
| `FiscalDocumentXmlBuilder_Nfce_Mod65_Regressao` | XML NFC-e continua com `<mod>65</mod>` |
| `FiscalDocumentXmlBuilder_Nfce_ComInfNFeSupl_Regressao` | XML NFC-e ainda contém `<infNFeSupl>` |
| `SefazEndpointResolver_Nfe_Svrs_AC` | AC(12) → SVRS NF-e URL |
| `SefazEndpointResolver_Nfe_Svan_SP` | SP(35) → SEFAZ-SP URL (não SVAN) |
| `SefazEndpointResolver_Nfe_Svan_BA` | BA(29) → SVAN URL |
| `SefazEndpointResolver_Nfce_Svrs_Regressao` | AC(12) NFC-e ainda → SVRS NFC-e URL |

### Unitários — Application

| Teste | Cenário |
|---|---|
| `IssueDocumentValidator_Nfe_SemDestinatario_422` | NFe sem destinatário retorna erro |
| `IssueDocumentValidator_Nfe_SemNatOp_422` | NFe sem natOp retorna erro |
| `IssueDocumentValidator_Nfe_ModFrete11_422` | modFrete=11 retorna erro |
| `IssueDocumentValidator_Nfce_ComDestinatario_422` | NfCe com destinatário retorna erro |

### Integração

| Teste | Cenário |
|---|---|
| `PostNfe_DocumentoValido_Retorna202ComStatusEnfileirado` | Happy path |
| `PostNfe_SemDestinatario_Retorna422` | Destinatário ausente |
| `PostNfe_SemNatOp_Retorna422` | natOp ausente |
| `PostNfe_DestinatarioIndIeDest1SemIe_Retorna422` | IndIeDest=1 sem IE |
| `PostNfce_ComDestinatario_Retorna422` | NFC-e não aceita destinatário (regressão) |
| `PostNfce_DocumentoValido_Retorna202_Regressao` | NFC-e existente continua funcionando |
