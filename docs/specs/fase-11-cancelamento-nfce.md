# Spec Fase 11 — Cancelamento de NFC-e

**Versão:** 1.2
**Data:** 2026-05-24
**Dependências:** Fases 1–10 concluídas
**Critério de conclusão:** `POST /api/v1/documentos/{id}/cancelar` retorna 202; job envia evento ao SEFAZ via `NfeRecepcaoEvento4`; status transiciona `Autorizado → Cancelando → Cancelado` (sucesso) ou `Cancelando → Autorizado` (rejeição SEFAZ); testes passam 100%.

---

## 1. Visão Geral

### Objetivo

Implementar o fluxo completo de cancelamento de NFC-e: validação do prazo de 30 minutos no domínio, envio do evento `110111` ao webservice `NfeRecepcaoEvento4` do SEFAZ, e atualização determinística do status baseada na resposta.

### Fluxo assíncrono com estado intermediário

```
POST /cancelar
  │
  ├─ Valida prazo (<30 min desde AuthorizedAt) → 422 se expirado
  ├─ Valida Justificativa (15–255 chars) → 422 se inválida
  ├─ Valida ClienteApp ownership → 403 se de outro ClienteApp
  │
  ├─ documento.IniciarCancelamento(timeProvider)
  │     Autorizado → Cancelando
  │     Limpa MotivoRejeicao = null
  │
  ├─ Persiste (status = Cancelando)
  ├─ Enfileira CancelamentoJob
  └─ Retorna 202 { documentoId, status: "Cancelando" }

CancelamentoJob (Hangfire)
  │
  ├─ Carrega documento (se não Cancelando → idempotência, return)
  ├─ Se documento.Protocolo == null → log error, return sem retry
  ├─ Carrega Tenant + certificado mTLS
  ├─ Gera idLote (via TimeProvider, mesmo padrão do SefazClient)
  ├─ Constrói XML de evento (CancelamentoEventoBuilder)
  ├─ Assina com XmlSigner (referenceUri = "#ID110111{chave}01")
  ├─ Envia para NfeRecepcaoEvento4 (SefazHttpClient)
  ├─ Parseia resposta → se IsFailure lança exceção (Hangfire retenta)
  │
  ├─ cStat=135 ou 155 (aceito)
  │     documento.ConfirmarCancelamento(dhRegEvento do SEFAZ)
  │     Cancelando → Cancelado, armazena CanceladoAt
  │     Publica DocumentoFiscalCanceladoEvent
  │
  └─ Outros cStat (rejeitado)
        documento.RejeitarCancelamento(xMotivo)
        Cancelando → Autorizado
        Armazena xMotivo em MotivoRejeicao
        Log warning (não publica domain event — cliente pode tentar novamente)
```

### Decisões-chave

- **Estado intermediário `Cancelando`**: evita duplo cancelamento e dá visibilidade ao ClienteApp via `GET /status` durante o processamento assíncrono.
- **`DocumentoFiscalCanceladoEvent` só é publicado ao confirmar**: o evento existente não muda — a diferença é que agora ele é emitido por `ConfirmarCancelamento`, não por `Cancelar`.
- **`nProt` obrigatório**: o campo `Protocolo` armazenado na autorização original é a única fonte válida do número de protocolo para o XML de cancelamento.
- **Rejeição SEFAZ reverte para `Autorizado`**: o ClienteApp pode tentar novamente se ainda estiver dentro do prazo de 30 minutos. Não é publicado domain event — o cliente recebeu 202 e pode consultar o status.
- **`MotivoRejeicao` é reutilizado e limpo**: `IniciarCancelamento` limpa `MotivoRejeicao = null` antes de transicionar, evitando que a segunda tentativa exiba o motivo de uma rejeição anterior. Em `RejeitarCancelamento`, o campo registra o motivo da rejeição do cancelamento (não da autorização).
- **`CanceladoAt` armazenado na entidade**: `ConfirmarCancelamento` persiste a data de cancelamento reportada pelo SEFAZ (`dhRegEvento`), exposta pelo GET /status.
- **`XmlSigner` aceita `referenceUri` completa**: para cancelamento a URI é `"#ID110111{chave}01"`; para NFC-e continua `"#NFe{chave}"`. Todos os call sites passam a URI completa com `#`.
- **`idLote` gerado pelo `CancelamentoJob`**: gerado da mesma forma que no `SefazClient` (via `TimeProvider`), passado explicitamente ao `CancelamentoEventoBuilder`.
- **Conversão UTC → fuso da UF no builder**: `CancelamentoEventoBuilder` recebe `DateTimeOffset utcNow` e converte internamente para o offset da UF via `UfFusoHorario.Mapa`.
- **`TipoTentativa.Cancelamento = 4`**: novo valor do enum para distinguir tentativas SEFAZ de cancelamento de entregas webhook.

---

## 2. Mapa de Arquivos

| Arquivo | Ação | Responsabilidade |
|---|---|---|
| `Domain/Enums/StatusDocumento.cs` | Editar | Adicionar `Cancelando = 9` |
| `Domain/Entities/DocumentoFiscal.cs` | Editar | `IniciarCancelamento`, `ConfirmarCancelamento`, `RejeitarCancelamento`, `CanceladoAt`; remover `Cancelar` |
| `Domain/Errors/DocumentoFiscalErrors.cs` | Sem alterações | Nenhum erro novo; rejeição SEFAZ tratada no job como reversão de estado |
| `Domain/Enums/TipoTentativa.cs` | Editar | Adicionar `Cancelamento = 4` |
| `Application/Common/Interfaces/ICancelamentoJobQueue.cs` | Criar | Interface do job queue de cancelamento |
| `Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommand.cs` | Criar | Command + DTOs |
| `Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandHandler.cs` | Criar | Handler CQRS |
| `Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandValidator.cs` | Criar | Validação FluentValidation |
| `Application/Common/Models/CancelarDocumentoResponse.cs` | Criar | DTO de resposta 202 |
| `Infrastructure/Fiscal/XmlSigner.cs` | Editar | `referenceUri` completa em vez de `chaveAcesso` sufixo (namespace: `VisuFiscalHub.Infrastructure.Fiscal`) |
| `Infrastructure/Fiscal/Sefaz/SefazClient.cs` | Editar | Atualizar call site do `XmlSigner.Assinar` |
| `Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs` | Editar | Mapear coluna `cancelado_at` para `CanceladoAt` |
| `Infrastructure/Fiscal/Sefaz/SefazHttpClient.cs` | Verificar | Confirmar assinatura de `PostSoapAsync` antes de chamar em `CancelamentoJob` |
| `Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` | Editar | `ResolveEvento(ufCodigo, ambiente)` |
| `Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs` | Editar | `BuildEvento(xmlEvento, cUF)` com `versaoDados="1.00"` |
| `Infrastructure/Fiscal/Sefaz/CancelamentoEventoBuilder.cs` | Criar | XML do evento `<infEvento>` |
| `Infrastructure/Fiscal/Sefaz/CancelamentoRetornoParser.cs` | Criar | Parse da resposta `NfeRecepcaoEvento4` |
| `Infrastructure/Jobs/CancelamentoJob.cs` | Criar | Job Hangfire de cancelamento |
| `Infrastructure/Jobs/HangfireCancelamentoJobQueue.cs` | Criar | Implementação de `ICancelamentoJobQueue` |
| `Infrastructure/DependencyInjection.cs` | Editar | Registrar `ICancelamentoJobQueue` |
| `Infrastructure/Persistence/Migrations/...AddCancelando.cs` | Criar | Migration para `Cancelando = 9` e `CanceladoAt` |
| `Api/Program.cs` | Editar | Substituir stub 501 pelo handler real |
| `Api/ResultExtensions.cs` | Editar | Nenhuma adição (rejeição SEFAZ não retorna HTTP direto) |
| `tests/.../Integration/CancelamentoTests.cs` | Criar | Testes de integração |
| `tests/.../Domain/DocumentoFiscalTests.cs` | Editar | **Remover** os 4 testes `Cancelar_*` existentes (linhas 100–181); adicionar novos casos da state machine |
| `tests/.../Domain/DocumentoFiscalBuilder.cs` | Editar | `Autorizado(DateTimeOffset)`, `Cancelando(DateTimeOffset)` |
| `tests/.../Application/CancelarDocumentoCommandHandlerTests.cs` | Criar | Testes unitários do handler |
| `tests/.../Infrastructure/Fiscal/CancelamentoRetornoParserTests.cs` | Criar | Testes unitários do parser |
| `tests/.../Infrastructure/Fiscal/CancelamentoEventoBuilderTests.cs` | Criar | Testes unitários do builder de XML |
| `tests/.../Infrastructure/Fiscal/XmlSignerTests.cs` | Editar | Atualizar os 5+ call sites existentes para a nova assinatura `referenceUri` |

---

## 3. Domínio

### 3.1 `StatusDocumento`

Adicionar ao enum existente:

```csharp
Cancelando = 9   // aguardando confirmação do SEFAZ
```

O valor 9 não colide com nenhum status existente (1–8).

### 3.2 `TipoTentativa`

Adicionar ao enum existente:

```csharp
Cancelamento = 4   // tentativa de cancelamento via NfeRecepcaoEvento4
```

### 3.3 `DocumentoFiscal` — state machine

Substituir o método `Cancelar(TimeProvider)` existente por três métodos. Adicionar campo `CanceladoAt`.

**Campo novo:**
```csharp
public DateTimeOffset? CanceladoAt { get; private set; }
```

**`IniciarCancelamento(TimeProvider timeProvider)`**
- Transição: `Autorizado → Cancelando`
- Valida: `Status == Autorizado` → falha com `TransicaoInvalida` se não
- Valida: `AuthorizedAt is null` → falha com `TransicaoInvalida` (invariante defensiva)
- Valida: `timeProvider.GetUtcNow() >= AuthorizedAt.Value.AddMinutes(30)` → falha com `PrazoDeCancelamentoExpirado` (boundary estrito: exatamente 30 min = expirado; consistente com o `Cancelar()` original)
- Limpa `MotivoRejeicao = null` (apaga motivo de cancelamento anterior, se houver)
- **Não** publica domain event (cancelamento ainda não confirmado pelo SEFAZ)
- Retorna `Result.Success()`

**`ConfirmarCancelamento(DateTimeOffset canceladoAt)`**
- Transição: `Cancelando → Cancelado`
- Valida: `Status == Cancelando` → falha com `TransicaoInvalida` se não
- Persiste `CanceladoAt = canceladoAt` (data `dhRegEvento` retornada pelo SEFAZ)
- Publica `DocumentoFiscalCanceladoEvent(Id, TenantId, canceladoAt, Guid.CreateVersion7(), canceladoAt)`
- Retorna `Result.Success()`

**`RejeitarCancelamento(string motivo)`**
- Transição: `Cancelando → Autorizado`
- Valida: `Status == Cancelando` → falha com `TransicaoInvalida` se não
- Armazena `MotivoRejeicao = motivo` (reutilizado para registrar rejeição do cancelamento)
- **Não** publica domain event (rejeição de cancelamento não é notificada via webhook — cliente pode tentar novamente se ainda dentro do prazo)
- Retorna `Result.Success()`

> **Nota:** O método `Cancelar(TimeProvider)` existente é **removido**. Qualquer uso deve ser migrado para `IniciarCancelamento`.

### 3.4 `DocumentoFiscalErrors`

Sem alterações. Nenhum novo erro é adicionado: `PrazoDeCancelamentoExpirado` e `TransicaoInvalida` já existem. A rejeição pelo SEFAZ é tratada como logging + reversão de estado no job, sem propagar `Result.Failure` para a API.

> **Nota:** O erro `CancelamentoRejeitadoPeloSefaz` não existe na classe atual — não há nada a remover.

---

## 4. Application Layer

### 4.1 `ICancelamentoJobQueue`

```csharp
// Application/Common/Interfaces/ICancelamentoJobQueue.cs
public interface ICancelamentoJobQueue
{
    Task EnqueueCancelamentoAsync(
        DocumentoFiscalId id,
        string justificativa,
        CancellationToken ct = default);
}
```

### 4.2 `CancelarDocumentoCommand`

```csharp
// Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommand.cs
public sealed record CancelarDocumentoCommand : ICommand<Result<CancelarDocumentoResponse>>
{
    public DocumentoFiscalId DocumentoId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string Justificativa { get; init; } = string.Empty;
}
```

### 4.3 `CancelarDocumentoCommandHandler`

O handler recebe `TimeProvider` via construtor (mesmo padrão do `IssueDocumentCommandHandler`).

Pipeline:
1. Carrega documento via `IDocumentoFiscalRepository.GetByIdAsync`; retorna `NaoEncontrado` se null
2. Valida ownership: `documento.ClienteAppId != command.ClienteAppId` → retorna `TenantErrors.NaoPertenceAoClienteApp`
3. Chama `documento.IniciarCancelamento(timeProvider)` → propaga falha se status inválido ou prazo expirado
4. Persiste via `IUnitOfWork.SaveChangesAsync`
5. Enfileira via `ICancelamentoJobQueue.EnqueueCancelamentoAsync(documentoId, justificativa, ct)`
6. Retorna `Result.Success(new CancelarDocumentoResponse(documento.Id, StatusDocumento.Cancelando))`

> **Race condition**: o handler usa `GetByIdAsync` sem lock. Dois requests concorrentes podem ambos passar pela guarda `Status == Autorizado`. Isso é tolerado: `IniciarCancelamento` é idempotente em memória e o job verifica `Status != Cancelando` na entrada — o segundo job retorna imediatamente sem efeito colateral. O risco de dois jobs persistirem é eliminado pelo `[DisableConcurrentExecution]` no job.

### 4.4 `CancelarDocumentoCommandValidator`

```csharp
RuleFor(x => x.Justificativa)
    .NotEmpty()
    .MinimumLength(15)
    .MaximumLength(255);
```

### 4.5 `CancelarDocumentoResponse`

```csharp
// Application/Common/Models/CancelarDocumentoResponse.cs
public sealed record CancelarDocumentoResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status);
```

---

## 5. Infraestrutura

### 5.1 `XmlSigner` — breaking change

Arquivo: `src/VisuFiscalHub.Infrastructure/Fiscal/XmlSigner.cs` (namespace `VisuFiscalHub.Infrastructure.Fiscal`, **não** `.Sefaz`).

**Alterar** a assinatura pública do método `Assinar`:

```csharp
// Antes:
public XmlDocument Assinar(XmlDocument xmlDoc, X509Certificate2 certificado, string chaveAcesso)
// Uri interna era: $"#NFe{chaveAcesso}"

// Depois:
public XmlDocument Assinar(XmlDocument xmlDoc, X509Certificate2 certificado, string referenceUri)
// Uri interna é: referenceUri (passada pelo caller, já inclui "#")
```

**Atualizar call sites** (verificar quantos existem em `XmlSignerTests.cs` antes de alterar):
- `SefazClient.cs`: passar `$"#NFe{documento.ChaveAcesso.Valor}"`
- `CancelamentoJob.cs`: passar `$"#ID110111{documento.ChaveAcesso.Valor}01"`
- `XmlSignerTests.cs`: atualizar todos os testes existentes para a nova assinatura

### 5.2 `SefazEndpointResolver` — `ResolveEvento`

Novo método público com mapeamento explícito das 27 UFs para `NfeRecepcaoEvento4`.

SVRS (UFs sem serviço próprio): `{svrsBase}/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx`

Mapeamento por UF (produção / homologação):
- AM (13): `https://nfce.sefaz.am.gov.br/services/NfeRecepcaoEvento4` / `https://nfce-homologacao.sefaz.am.gov.br/services/NfeRecepcaoEvento4`
- PA (15): `https://appnfce.sefa.pa.gov.br:444/nfce/NFeRecepcaoEvento4` / `https://appnfce.sefa.pa.gov.br:444/nfce-homologacao/NFeRecepcaoEvento4`
- MA (21): `https://www.sefaz.ma.gov.br/nfce/NFeRecepcaoEvento4` / `https://hom.sefaz.ma.gov.br/nfce/NFeRecepcaoEvento4`
- CE (23): `https://nfce.sefaz.ce.gov.br/nfce/NFeRecepcaoEvento4` / `https://nfceh.sefaz.ce.gov.br/nfce/NFeRecepcaoEvento4`
- PE (26): `https://nfce.sefaz.pe.gov.br/nfce-server/services/NFeRecepcaoEvento4` / `https://nfcehomolog.sefaz.pe.gov.br/nfce-server/services/NFeRecepcaoEvento4`
- BA (29): `https://nfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx` / `https://hnfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx`
- MG (31): `https://nfce.fazenda.mg.gov.br/nfce/services/NFeRecepcaoEvento4` / `https://hnfce.fazenda.mg.gov.br/nfce/services/NFeRecepcaoEvento4`
- SP (35): `https://nfe.fazenda.sp.gov.br/nfceservice/services/NFeRecepcaoEvento4` / `https://homologacao.nfe.fazenda.sp.gov.br/nfceservice/services/NFeRecepcaoEvento4`
- PR (41): `https://nfce.pr.gov.br/nfce/NFeRecepcaoEvento4` / `https://homologacao.nfce.pr.gov.br/nfce/NFeRecepcaoEvento4`
- RS (43): `https://nfce.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx` / `https://nfce-homologacao.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx`
- MS (50): `https://nfce.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4` / `https://homologacao.nfce.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4`
- MT (51): `https://nfce.sefaz.mt.gov.br/nfce/NFeRecepcaoEvento4` / `https://homologacao.sefaz.mt.gov.br/nfce/NFeRecepcaoEvento4`
- GO (52): `https://nfce.sefaz.go.gov.br/nfce/NFeRecepcaoEvento4` / `https://homologacao.nfce.sefaz.go.gov.br/nfce/NFeRecepcaoEvento4`
- Demais (SVRS): `https://nfce.svrs.rs.gov.br/ws/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx` / `https://nfce-homologacao.svrs.rs.gov.br/ws/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx`

UF inválida (não mapeada e não SVRS): `throw new InvalidOperationException($"UF {ufCodigo} não mapeada em ResolveEvento.")` — consistente com os demais métodos do resolver.

### 5.3 `SoapEnvelopeBuilder` — `BuildEvento`

```csharp
// Namespace do WSDL NFeRecepcaoEvento4 — diferente do de autorização.
private const string WsEventoNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";

public static string BuildEvento(string xmlEvento, int cUF)
{
    // versaoDados = "1.00" (schema <envEvento versao="1.00">)
    // NÃO reutilizar a constante "4.00" do BuildAutorizacao.
    // Estrutura idêntica ao BuildAutorizacao mas com WsEventoNs e versaoDados="1.00".
    // cUF no cabeçalho nfeCabMsg.
    // Body: <nfeDadosMsg> contém o xmlEvento diretamente.
}
```

### 5.4 `CancelamentoEventoBuilder`

Constrói o XML do evento de cancelamento conforme NT 2014.002 v1.04 (evento `110111`).

```csharp
internal static class CancelamentoEventoBuilder
{
    public static XmlDocument ConstruirEvento(
        DocumentoFiscal documento,
        Tenant tenant,
        string justificativa,
        string nProt,
        DateTimeOffset utcNow,
        string idLote);
}
```

- `idLote`: gerado pelo `CancelamentoJob` via `TimeProvider` (mesmo padrão do `SefazClient`)
- `dhEvento`: calculado internamente — `new DateTimeOffset(utcNow.UtcDateTime, UfFusoHorario.Mapa[ufCodigo])`, então formatado como `"yyyy-MM-ddTHH:mm:sszzz"`

Estrutura do XML resultante:
```xml
<envEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
  <idLote>{idLote}</idLote>
  <evento versao="1.00">
    <infEvento Id="ID110111{chaveAcesso44digitos}01">
      <cOrgao>{ufCodigo}</cOrgao>
      <tpAmb>{1|2}</tpAmb>
      <CNPJ>{cnpjEmitente}</CNPJ>
      <chNFe>{chaveAcesso44digitos}</chNFe>
      <dhEvento>{yyyy-MM-ddTHH:mm:sszzz com offset da UF}</dhEvento>
      <tpEvento>110111</tpEvento>
      <nSeqEvento>1</nSeqEvento>
      <verEvento>1.00</verEvento>
      <detEvento versao="1.00">
        <descEvento>Cancelamento</descEvento>
        <nProt>{protocoloAutorizacao}</nProt>
        <xJust>{justificativa}</xJust>
      </detEvento>
    </infEvento>
  </evento>
</envEvento>
```

> **`ConstruirEvento` retorna `XmlDocument` NÃO assinado.** A assinatura é responsabilidade exclusiva do `CancelamentoJob` (step 7): `XmlSigner.Assinar(xmlEvento, certificate, $"#ID110111{documento.ChaveAcesso.Valor}01")`. Não chamar `XmlSigner` dentro do builder — double-signing resulta em rejeição SEFAZ.

### 5.5 `CancelamentoRetornoParser`

Classe nova, independente do `SefazRetornoParser` existente (que parseia `NFeAutorizacao4`).

```csharp
public sealed record CancelamentoRetorno(
    bool Aceito,
    string CStat,
    string XMotivo,
    string? NProtCancelamento,
    DateTimeOffset? DhRegEvento);

internal static class CancelamentoRetornoParser
{
    // cStat=135: "Evento registrado e vinculado a NF-e" — cancelamento aceito
    // cStat=155: "Cancelamento homologado fora de prazo" — aceito
    //   (SEFAZ registra mesmo que o prazo SEFAZ já tenha passado; é distinto do
    //    prazo de 30 min validado no domínio — pode ocorrer em reenvios após timeout)
    // Qualquer outro cStat: rejeitado
    //
    // XPath de extração (namespace nfe = "http://www.portalfiscal.inf.br/nfe"):
    //   cStat:       //nfe:retEvento/nfe:infEvento/nfe:cStat
    //   xMotivo:     //nfe:retEvento/nfe:infEvento/nfe:xMotivo
    //   nProt:       //nfe:retEvento/nfe:infEvento/nfe:nProt
    //   dhRegEvento: //nfe:retEvento/nfe:infEvento/nfe:dhRegEvento
    //
    // Falha de parse (XML malformado) → Result.Failure (não lança exceção)
    public static Result<CancelamentoRetorno> Parse(string soapResponse);
}
```

`DhRegEvento` é a data de registro do evento no SEFAZ — usada como `canceladoAt` em `ConfirmarCancelamento`. Parsear com `DateTimeOffset.Parse(valor, null, System.Globalization.DateTimeStyles.RoundtripKind)` para preservar o offset da UF retornado pelo SEFAZ (ex: `"2026-05-24T10:00:00-03:00"`).

### 5.6 `CancelamentoJob`

```csharp
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300],
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
// Attempts = 3 = 1 inicial + 2 retries. DelaysInSeconds tem 2 elementos (um por retry).
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class CancelamentoJob
{
    public async Task ExecuteAsync(DocumentoFiscalId documentoId, string justificativa, CancellationToken ct);
}
```

> **`NfceProcessingJob` guard:** Adicionar `Cancelando` ao guard de idempotência do `NfceProcessingJob.ExecuteAsync`. Atualmente o job verifica `Status != Processando`; deve incluir também `Cancelando` para evitar reprocessamento de um documento que foi cancelado enquanto aguardava o job de autorização: `if (documento.Status is not StatusDocumento.Processando and not StatusDocumento.Cancelando → ...` — na prática, o guard atual deve rejeitar qualquer status diferente de `Processando`.

Pipeline do `ExecuteAsync`:
1. Carrega documento; se `Status != Cancelando` → return (idempotência — job já processado)
2. Se `documento.Protocolo` é null → log error, return sem retry (pré-condição violada: documento autorizado sem protocolo)
3. Carrega tenant; se null/inativo → lança exceção (Hangfire faz retry)
4. Carrega certificado via `ITenantCertificateProvider`; se falha → lança exceção (Hangfire faz retry)
5. Gera `idLote` via `TimeProvider` (mesmo padrão do `SefazClient`)
6. Constrói XML do evento: `CancelamentoEventoBuilder.ConstruirEvento(documento, tenant, justificativa, documento.Protocolo!, utcNow, idLote)`
7. Assina: `XmlSigner.Assinar(xmlEvento, certificate, $"#ID110111{documento.ChaveAcesso.Valor}01")`
8. Monta envelope SOAP: `SoapEnvelopeBuilder.BuildEvento(xmlAssinado.OuterXml, ufCodigo)`
9. Envia: `SefazHttpClient.PostSoapAsync(SefazEndpointResolver.ResolveEvento(ufCodigo, ambiente), envelope, certificate, ct)`
10. Parseia: `CancelamentoRetornoParser.Parse(soapResponse)`; se `IsFailure` → **lança exceção** para Hangfire retentar (evita documento preso em `Cancelando`)
11. Se aceito (cStat 135/155):
    - `documento.ConfirmarCancelamento(retorno.DhRegEvento ?? utcNow)` → `Cancelando → Cancelado`
    - Persiste via `IUnitOfWork.SaveChangesAsync`
    - Log information
12. Se rejeitado:
    - `documento.RejeitarCancelamento(xMotivo)` → `Cancelando → Autorizado`
    - Persiste via `IUnitOfWork.SaveChangesAsync`
    - Log warning com cStat e xMotivo
    - **Não** lança exceção — rejeição fiscal é definitiva, retry não ajuda
13. Registra `DeliveryAttempt(TipoTentativa.Cancelamento, success, responseCode, responseMessage, elapsedMs)`

### 5.7 `HangfireCancelamentoJobQueue`

```csharp
internal sealed class HangfireCancelamentoJobQueue : ICancelamentoJobQueue
{
    private readonly IBackgroundJobClient _jobClient;

    public Task EnqueueCancelamentoAsync(DocumentoFiscalId id, string justificativa, CancellationToken ct = default)
    {
        _jobClient.Enqueue<CancelamentoJob>(
            job => job.ExecuteAsync(id, justificativa, CancellationToken.None));
        return Task.CompletedTask;
    }
}
```

### 5.8 `DependencyInjection`

Adicionar no método `AddInfrastructure`:
```csharp
services.AddScoped<ICancelamentoJobQueue, HangfireCancelamentoJobQueue>();
```

### 5.9 Migration

A migration adiciona `Cancelando = 9` (sem alteração de schema se enum mapeado como `int`) e o campo `CanceladoAt` (DateTimeOffset? nullable):

```powershell
dotnet ef migrations add AddCancelando `
  --project src/VisuFiscalHub.Infrastructure `
  --startup-project src/VisuFiscalHub.Api
```

`StatusDocumento` é mapeado como `int` (`.HasConversion<int>()` na linha 102 de `DocumentoFiscalConfiguration.cs`) — a migration adiciona apenas a coluna `cancelado_at` como nullable `timestamp with time zone`. Nenhuma alteração de schema para o enum.

Adicionar em `DocumentoFiscalConfiguration.Configure`:
```csharp
builder.Property(d => d.CanceladoAt)
    .HasColumnName("cancelado_at");
```

Commitar migration após verificação.

---

## 6. API

### 6.1 `Program.cs` — substituir stub

Localizar:
```csharp
documentos.MapPost("/{id:guid}/cancelar",
    (Guid id) => TypedResults.StatusCode(StatusCodes.Status501NotImplemented))
```

Substituir por:
```csharp
documentos.MapPost("/{id:guid}/cancelar",
    async (
        Guid id,
        CancelarDocumentoRequest body,
        IMediator mediator,
        ICurrentUserContext userContext,
        CancellationToken ct) =>
    {
        var command = new CancelarDocumentoCommand
        {
            DocumentoId   = new DocumentoFiscalId(id),
            ClienteAppId  = userContext.ClienteAppId,
            Justificativa = body.Justificativa
        };
        var result = await mediator.Send(command, ct);
        return result.ToHttpResult(r => TypedResults.Accepted(
            $"/api/v1/documentos/{id}/status", r));
    })
    .RequireAuthorization()
    .RequireRateLimiting("api")
    .WithName("CancelarDocumento")
    .WithTags("Documentos");
```

`CancelarDocumentoRequest` — record local no Program.cs ou em `Api/Endpoints/`:
```csharp
public sealed record CancelarDocumentoRequest(string Justificativa);
```

### 6.2 `ResultExtensions`

Nenhuma alteração necessária. A rejeição de cancelamento pelo SEFAZ é tratada no job como logging + reversão de estado, sem propagar `Result.Failure` para a API.

---

## 7. Testes

### 7.1 Helpers de teste — `DocumentoFiscalBuilder`

Adicionar dois métodos ao builder existente:

```csharp
// Cria documento no estado Autorizado com AuthorizedAt e Protocolo preenchidos.
internal static DocumentoFiscal Autorizado(DateTimeOffset authorizedAt, long numero = 1)
{
    var doc = Processando(numero);
    var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
    doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, new FixedTimeProvider(authorizedAt)).IsSuccess.ShouldBeTrue();
    return doc;
}

// Cria documento no estado Cancelando (IniciarCancelamento chamado 1 minuto após autorização).
internal static DocumentoFiscal Cancelando(DateTimeOffset authorizedAt, long numero = 1)
{
    var doc = Autorizado(authorizedAt, numero);
    doc.IniciarCancelamento(new FixedTimeProvider(authorizedAt.AddMinutes(1))).IsSuccess.ShouldBeTrue();
    return doc;
}
```

### 7.2 Unitários de Domínio — `DocumentoFiscalTests`

Usar `FixedNow` (constante estática já existente no arquivo) em vez de `DateTimeOffset.UtcNow`.

```csharp
// Usar a constante já existente no arquivo:
// private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

[Fact]
public void IniciarCancelamento_QuandoAutorizadoDentroDoPrazo_TransicionaParaCancelando()
{
    var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-10));
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Cancelando);
    documento.MotivoRejeicao.ShouldBeNull(); // IniciarCancelamento limpa MotivoRejeicao
    documento.DomainEvents.ShouldBeEmpty();
}

[Fact]
public void IniciarCancelamento_QuandoPrazoExpirado_RetornaErro()
{
    var authorizedAt = FixedNow.AddMinutes(-31);
    var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    documento.Status.ShouldBe(StatusDocumento.Autorizado);
}

[Fact]
public void IniciarCancelamento_QuandoExatamente30Minutos_RetornaErro()
{
    // Boundary estrito: >= 30 min = expirado
    var authorizedAt = FixedNow.AddHours(-1);
    var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
    var result = documento.IniciarCancelamento(new FixedTimeProvider(authorizedAt.AddMinutes(30)));
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
}

[Fact]
public void IniciarCancelamento_QuandoNaoAutorizado_RetornaErro()
{
    var documento = DocumentoFiscalBuilder.Enfileirado();
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
}

[Fact]
public void IniciarCancelamento_QuandoJaCancelando_RetornaErro()
{
    var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
}

[Fact]
public void IniciarCancelamento_QuandoCancelado_RetornaErro()
{
    var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
    documento.ConfirmarCancelamento(FixedNow);
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
}

[Fact]
public void ConfirmarCancelamento_QuandoCancelando_TransicionaParaCancelado()
{
    var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
    var result = documento.ConfirmarCancelamento(FixedNow);
    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Cancelado);
    documento.CanceladoAt.ShouldBe(FixedNow);
    documento.DomainEvents.ShouldContain(e => e is DocumentoFiscalCanceladoEvent);
}

[Fact]
public void ConfirmarCancelamento_QuandoNaoCancelando_RetornaErro()
{
    var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-5));
    var result = documento.ConfirmarCancelamento(FixedNow);
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
}

[Fact]
public void RejeitarCancelamento_QuandoCancelando_RevertaParaAutorizado()
{
    var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
    var result = documento.RejeitarCancelamento("Prazo encerrado no SEFAZ");
    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Autorizado);
    documento.MotivoRejeicao.ShouldBe("Prazo encerrado no SEFAZ");
    // Não publica evento — cliente recebeu 202 e pode consultar status ou retentar
    documento.DomainEvents.ShouldBeEmpty();
}

[Fact]
public void RejeitarCancelamento_QuandoNaoCancelando_RetornaErro()
{
    var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-5));
    var result = documento.RejeitarCancelamento("motivo");
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
}

[Fact]
public void IniciarCancelamento_AposaRejeicao_LimpaMotivo()
{
    // Fluxo: cancelamento rejeitado → retentativa → MotivoRejeicao deve ser null em Cancelando
    var authorizedAt = FixedNow.AddMinutes(-5);
    var documento = DocumentoFiscalBuilder.Cancelando(authorizedAt);
    documento.RejeitarCancelamento("Motivo anterior");
    // Status voltou para Autorizado; tenta novamente
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsSuccess.ShouldBeTrue();
    documento.MotivoRejeicao.ShouldBeNull();
}
```

### 7.3 Unitários de Application — `CancelarDocumentoCommandHandlerTests`

O handler recebe `TimeProvider` no construtor. O helper de criação do handler deve injetar `new FixedTimeProvider(...)`.

```csharp
// Usar a mesma constante de DocumentoFiscalTests para consistência:
private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

private static (CancelarDocumentoCommandHandler handler,
                IDocumentoFiscalRepository docRepo,
                IUnitOfWork unitOfWork,
                ICancelamentoJobQueue jobQueue)
    CriarHandler(FixedTimeProvider timeProvider)
{
    var docRepo  = Substitute.For<IDocumentoFiscalRepository>();
    var unitOfWork = Substitute.For<IUnitOfWork>();
    var jobQueue = Substitute.For<ICancelamentoJobQueue>();
    unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
    jobQueue.EnqueueCancelamentoAsync(Arg.Any<DocumentoFiscalId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    var handler = new CancelarDocumentoCommandHandler(docRepo, unitOfWork, jobQueue, timeProvider);
    return (handler, docRepo, unitOfWork, jobQueue);
}

[Fact]
public async Task Handle_QuandoDocumentoAutorizado_EnfileirarJobERetornarCancelando()
{
    var authorizedAt = FixedNow.AddMinutes(-10);
    var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
    var (handler, docRepo, unitOfWork, jobQueue) = CriarHandler(new FixedTimeProvider(FixedNow));
    docRepo.GetByIdAsync(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
    var command = new CancelarDocumentoCommand
    {
        DocumentoId   = documento.Id,
        ClienteAppId  = documento.ClienteAppId,
        Justificativa = "Justificativa de cancelamento de teste aqui"
    };

    var result = await handler.Handle(command, CancellationToken.None);

    result.IsSuccess.ShouldBeTrue();
    result.Value.Status.ShouldBe(StatusDocumento.Cancelando);
    await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    await jobQueue.Received(1).EnqueueCancelamentoAsync(documento.Id, command.Justificativa, Arg.Any<CancellationToken>());
}

[Fact]
public async Task Handle_QuandoDocumentoDeOutroClienteApp_RetornarErro403()
{
    var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-10));
    var (handler, docRepo, _, _) = CriarHandler(new FixedTimeProvider(FixedNow));
    docRepo.GetByIdAsync(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
    var command = new CancelarDocumentoCommand
    {
        DocumentoId   = documento.Id,
        ClienteAppId  = ClienteAppId.New(), // diferente do documento
        Justificativa = "Justificativa de cancelamento de teste aqui"
    };

    var result = await handler.Handle(command, CancellationToken.None);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("Tenant.NaoPertenceAoClienteApp");
}

[Fact]
public async Task Handle_QuandoPrazoExpirado_RetornarErroPrazoDeCancelamentoExpirado()
{
    var authorizedAt = FixedNow.AddMinutes(-31);
    var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
    var (handler, docRepo, _, _) = CriarHandler(new FixedTimeProvider(FixedNow));
    docRepo.GetByIdAsync(documento.Id, Arg.Any<CancellationToken>()).Returns(documento);
    var command = new CancelarDocumentoCommand
    {
        DocumentoId   = documento.Id,
        ClienteAppId  = documento.ClienteAppId,
        Justificativa = "Justificativa de cancelamento de teste aqui"
    };

    var result = await handler.Handle(command, CancellationToken.None);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
}

[Fact]
public async Task Handle_QuandoDocumentoNaoEncontrado_RetornarErro404()
{
    var (handler, docRepo, _, _) = CriarHandler(new FixedTimeProvider(FixedNow));
    docRepo.GetByIdAsync(Arg.Any<DocumentoFiscalId>(), Arg.Any<CancellationToken>())
           .Returns((DocumentoFiscal?)null);
    var command = new CancelarDocumentoCommand
    {
        DocumentoId   = DocumentoFiscalId.New(),
        ClienteAppId  = ClienteAppId.New(),
        Justificativa = "Justificativa de cancelamento de teste aqui"
    };

    var result = await handler.Handle(command, CancellationToken.None);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.NaoEncontrado");
}
```

### 7.4 Unitários de Infraestrutura — `CancelamentoRetornoParserTests`

`CancelamentoRetornoParser` é classe nova — independente do `SefazRetornoParser` existente.

```csharp
// Fixtures SOAP
private const string SoapAceito135 = """
    <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
      <soap12:Body>
        <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4">
          <retEnvEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
            <retEvento>
              <infEvento>
                <cStat>135</cStat>
                <xMotivo>Evento registrado e vinculado a NF-e</xMotivo>
                <nProt>135260000000099</nProt>
                <dhRegEvento>2026-05-24T10:00:00-03:00</dhRegEvento>
              </infEvento>
            </retEvento>
          </retEnvEvento>
        </nfeResultMsg>
      </soap12:Body>
    </soap12:Envelope>
    """;

private const string SoapAceito155 = """
    <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
      <soap12:Body>
        <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4">
          <retEnvEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
            <retEvento>
              <infEvento>
                <cStat>155</cStat>
                <xMotivo>Cancelamento homologado fora de prazo</xMotivo>
                <nProt>155260000000042</nProt>
                <dhRegEvento>2026-05-24T10:30:00-03:00</dhRegEvento>
              </infEvento>
            </retEvento>
          </retEnvEvento>
        </nfeResultMsg>
      </soap12:Body>
    </soap12:Envelope>
    """;

private const string SoapRejeicao218 = """
    <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
      <soap12:Body>
        <nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4">
          <retEnvEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
            <retEvento>
              <infEvento>
                <cStat>218</cStat>
                <xMotivo>Rejeição: Prazo de Cancelamento Superior ao Prazo Limite</xMotivo>
              </infEvento>
            </retEvento>
          </retEnvEvento>
        </nfeResultMsg>
      </soap12:Body>
    </soap12:Envelope>
    """;

[Fact]
public void Parse_cStat135_DeveRetornarAceito()
{
    var result = CancelamentoRetornoParser.Parse(SoapAceito135);
    result.IsSuccess.ShouldBeTrue();
    result.Value.Aceito.ShouldBeTrue();
    result.Value.CStat.ShouldBe("135");
    result.Value.NProtCancelamento.ShouldBe("135260000000099");
}

[Fact]
public void Parse_cStat155_DeveRetornarAceito()
{
    // cStat=155: "Cancelamento homologado fora de prazo" — SEFAZ registra mesmo
    // após o prazo SEFAZ (distinto do prazo de 30 min da regra de negócio local).
    var result = CancelamentoRetornoParser.Parse(SoapAceito155);
    result.IsSuccess.ShouldBeTrue();
    result.Value.Aceito.ShouldBeTrue();
    result.Value.CStat.ShouldBe("155");
}

[Fact]
public void Parse_cStatRejeicao_DeveRetornarNaoAceito()
{
    var result = CancelamentoRetornoParser.Parse(SoapRejeicao218);
    result.IsSuccess.ShouldBeTrue(); // Parse ok, mas cancelamento não aceito
    result.Value.Aceito.ShouldBeFalse();
    result.Value.CStat.ShouldBe("218");
}

[Fact]
public void Parse_XmlMalformado_DeveRetornarFalhaSemExcecao()
{
    var result = CancelamentoRetornoParser.Parse("<broken xml");
    result.IsFailure.ShouldBeTrue();
}
```

### 7.5 Unitários de Infraestrutura — `CancelamentoEventoBuilderTests`

Usar `DocumentoFiscalBuilder.Autorizado(FixedNow)` para criar documento com `Protocolo` e `ChaveAcesso` preenchidos.

```csharp
private static DocumentoFiscal CriarDocumentoParaBuilder()
    => DocumentoFiscalBuilder.Autorizado(FixedNow);

[Fact]
public void ConstruirEvento_DeveConterTpEvento110111()
{
    var doc = CriarDocumentoParaBuilder();
    var xml = CancelamentoEventoBuilder.ConstruirEvento(
        doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
        FixedNow, idLote: "202605241000000");

    xml.SelectSingleNode("//nfe:tpEvento", NfNs())!.InnerText.ShouldBe("110111");
}

[Fact]
public void ConstruirEvento_DeveConterNProtDaAutorizacao()
{
    var doc = CriarDocumentoParaBuilder();
    var xml = CancelamentoEventoBuilder.ConstruirEvento(
        doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
        FixedNow, "202605241000000");

    xml.SelectSingleNode("//nfe:nProt", NfNs())!.InnerText.ShouldBe("PROT001");
}

[Fact]
public void ConstruirEvento_DeveConterXJust()
{
    const string just = "Justificativa de teste com tamanho suficiente";
    var doc = CriarDocumentoParaBuilder();
    var xml = CancelamentoEventoBuilder.ConstruirEvento(
        doc, CriarTenant(), just, "PROT001", FixedNow, "202605241000000");

    xml.SelectSingleNode("//nfe:xJust", NfNs())!.InnerText.ShouldBe(just);
}

[Fact]
public void ConstruirEvento_IdInfEvento_DeveSerID110111MaisChaveAcesso()
{
    var doc = CriarDocumentoParaBuilder();
    var xml = CancelamentoEventoBuilder.ConstruirEvento(
        doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
        FixedNow, "202605241000000");

    var id = xml.SelectSingleNode("//nfe:infEvento", NfNs())!.Attributes!["Id"]!.Value;
    id.ShouldBe($"ID110111{doc.ChaveAcesso.Valor}01");
}

[Fact]
public void ConstruirEvento_DeveConterDescEventoCancelamento()
{
    var doc = CriarDocumentoParaBuilder();
    var xml = CancelamentoEventoBuilder.ConstruirEvento(
        doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
        FixedNow, "202605241000000");

    xml.SelectSingleNode("//nfe:descEvento", NfNs())!.InnerText.ShouldBe("Cancelamento");
}
```

### 7.6 Testes de Integração — `CancelamentoTests`

Padrão: invocar jobs diretamente via DI (não polling), conforme `DocumentLifecycleTests` existente.

```csharp
// Emite um NFC-e, executa NfceProcessingJob diretamente (mesmo padrão de DocumentLifecycleTests),
// e retorna cliente HTTP + documentoId prontos para o teste de cancelamento.
private async Task<(HttpClient Http, Guid DocumentoId)> EmitirEAutorizarAsync()
{
    SefazFake.SimularAutorizado(); // necessário antes de ProcessarAsync
    var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
    var token = await ObterTokenAsync(clientId, clientSecret);
    var tenantId = await CriarTenantAsync(clienteAppId);
    var http = CriarClienteAutenticado(token, tenantId.Value);

    var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
    {
        Content = JsonContent.Create(BodyValido()),
        Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
    };
    using var emitirResponse = await http.SendAsync(request);
    emitirResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
    var docId = emitirBody!.DocumentoId.Value;

    // Executa o job de processamento sincronamente (sem Hangfire em execução)
    using var scope = Factory.Services.CreateScope();
    var job = scope.ServiceProvider.GetRequiredService<NfceProcessingJob>();
    await job.ExecuteAsync(new DocumentoFiscalId(docId), CancellationToken.None);

    return (http, docId);
}

// Versão sem processamento — retorna documento em status Enfileirado
private async Task<(HttpClient Http, Guid DocumentoId)> EmitirSemProcessarAsync()
{
    var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
    var token = await ObterTokenAsync(clientId, clientSecret);
    var tenantId = await CriarTenantAsync(clienteAppId);
    var http = CriarClienteAutenticado(token, tenantId.Value);

    var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
    {
        Content = JsonContent.Create(BodyValido()),
        Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
    };
    using var emitirResponse = await http.SendAsync(request);
    emitirResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
    return (http, emitirBody!.DocumentoId.Value);
}

[Fact]
public async Task PostCancelar_DocumentoAutorizado_Retorna202ComStatusCancelando()
{
    var (http, id) = await EmitirEAutorizarAsync();
    var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

    var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

    response.StatusCode.ShouldBe(HttpStatusCode.Accepted); // 202
    var status = await http.GetFromJsonAsync<StatusResponse>($"/api/v1/documentos/{id}/status");
    status!.Status.ShouldBe((int)StatusDocumento.Cancelando);
}

[Fact]
public async Task PostCancelar_DocumentoEnfileirado_Retorna422()
{
    // Documento emitido mas job de processamento não executado → status Enfileirado
    // → IniciarCancelamento retorna TransicaoInvalida → 422
    var (http, id) = await EmitirSemProcessarAsync();
    var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

    var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

    response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // 422
}

[Fact]
public async Task PostCancelar_DocumentoJaCancelando_Retorna422()
{
    // Segunda requisição de cancelamento enquanto status = Cancelando
    var (http, id) = await EmitirEAutorizarAsync();
    var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };
    await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body); // primeira
    var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body); // segunda

    response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // 422 TransicaoInvalida
}

[Fact]
public async Task PostCancelar_SemJustificativa_Retorna422()
{
    var (http, id) = await EmitirEAutorizarAsync();
    var body = new { justificativa = "" };

    var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

    response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // 422
}

[Fact]
public async Task PostCancelar_JustificativaMuitoCurta_Retorna422()
{
    var (http, id) = await EmitirEAutorizarAsync();
    var body = new { justificativa = "abc de fghij n" }; // 14 chars (mínimo = 15)

    var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

    response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // 422
}

[Fact]
public async Task PostCancelar_DocumentoDeOutroClienteApp_Retorna403()
{
    var (_, id) = await EmitirEAutorizarAsync(); // ClienteApp-1
    // Cria ClienteApp-2 com token diferente
    var (_, clientId2, clientSecret2) = await CriarClienteAppAsync();
    var token2 = await ObterTokenAsync(clientId2, clientSecret2);
    // ClienteApp-2 não tem tenant; sem X-Tenant-Id a requisição ainda chega ao handler
    // (o tenantId não é obrigatório no cancelamento — o doc é encontrado por DocumentoId)
    var http2 = CriarClienteAutenticado(token2);
    var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

    var response = await http2.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

    response.StatusCode.ShouldBe(HttpStatusCode.Forbidden); // 403
}

[Fact]
public async Task PostCancelar_DocumentoNaoEncontrado_Retorna404()
{
    var (_, clientId, clientSecret) = await CriarClienteAppAsync();
    var token = await ObterTokenAsync(clientId, clientSecret);
    var http = CriarClienteAutenticado(token);
    var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

    var response = await http.PostAsJsonAsync($"/api/v1/documentos/{Guid.NewGuid()}/cancelar", body);

    response.StatusCode.ShouldBe(HttpStatusCode.NotFound); // 404
}
```

---

## 8. Self-Review

### Cobertura

| Requisito | Coberto por |
|---|---|
| Estado intermediário `Cancelando` | Seção 3.1 + 3.3 |
| Validação 30 min no domínio | `IniciarCancelamento` + testes 7.2 |
| `MotivoRejeicao` limpo em retentativa | `IniciarCancelamento` + teste `IniciarCancelamento_AposaRejeicao_LimpaMotivo` |
| `CanceladoAt` na entidade e response | `ConfirmarCancelamento` seção 3.3 + migration 5.9 + teste 7.2 |
| `XmlSigner` com referenceUri completa | Seção 5.1 + call sites atualizados (SefazClient, CancelamentoJob, XmlSignerTests) |
| `idLote` gerado pelo job | Seção 5.6 step 5 + assinatura `ConstruirEvento` seção 5.4 |
| Conversão UTC→fuso da UF no builder | Seção 5.4 (`CancelamentoEventoBuilder` responsável) |
| XML evento `tpEvento=110111` com `xJust` e `nProt` | `CancelamentoEventoBuilder` seção 5.4 + testes 7.5 |
| Assinatura do XML de evento | `XmlSigner.Assinar` com URI `#ID110111{chave}01` |
| Webservice `NfeRecepcaoEvento4` com mTLS | `SefazHttpClient.PostSoapAsync` + `SefazEndpointResolver.ResolveEvento` |
| Todas as 27 UFs mapeadas | Seção 5.2 |
| cStat=135 e 155 → `Cancelado` | `CancelamentoRetornoParser` + `CancelamentoJob` seção 5.6 |
| cStat=155 semanticamente documentado | Comentário em `CancelamentoRetornoParser` + teste 7.4 |
| Falha de parse → exceção → retry | `CancelamentoJob` step 10 |
| Rejeição SEFAZ → reverter para `Autorizado` | `documento.RejeitarCancelamento` |
| `TipoTentativa.Cancelamento` | Seção 3.2 + `CancelamentoJob` step 13 |
| `versaoDados="1.00"` no envelope | Seção 5.3 |
| `AutomaticRetry(Attempts=3)` com 2 delays | Seção 5.6 |
| `ResolveEvento` com throw para UF inválida | Seção 5.2 |
| Registrar `DeliveryAttempt` | `CancelamentoJob` step 13 |
| Endpoint 202 substituindo 501 | Seção 6.1 |
| `TenantErrors.NaoPertenceAoClienteApp` → 403 | Handler seção 4.3 + teste 7.3 |
| Testes de guarda de transição (4 casos) | Seção 7.2 |
| `PostCancelar_DocumentoJaCancelando_Retorna422` | Seção 7.6 |
| Padrão de invocação direta do job nos testes | Seção 7.6 (baseado em `DocumentLifecycleTests`) |
| `DocumentoFiscalConfiguration` mapeando `CanceladoAt` | Seção 5.9 |
| 4 testes `Cancelar_*` removidos | Seção 2 (file map) |
| `XmlSignerTests.cs` atualizado para nova assinatura | Seção 2 (file map) + 5.1 |
| Condição `>=` correta em `IniciarCancelamento` | Seção 3.3 |
| `SefazFake.SimularAutorizado()` antes de ProcessarAsync | Seção 7.6 `EmitirEAutorizarAsync` |
| `ObterTokenAsync(clientId, clientSecret)` correto | Seção 7.6 |
| `CriarClienteAutenticado(token, tenantId.Value)` correto | Seção 7.6 |
| `status.Status.ShouldBe((int)StatusDocumento.Cancelando)` correto | Seção 7.6 |
| `dhRegEvento` parseado com `RoundtripKind` | Seção 5.5 |
| `ConstruirEvento` não assina (double-signing explicitado) | Seção 5.4 |

### Gaps conscientemente fora deste spec

| Gap | Motivo |
|---|---|
| Teste de integração end-to-end do `CancelamentoJob` | Exigiria Testcontainers + SEFAZ real ou mock HTTP; escopo separado |
| `ReconciliacaoJobProcessor` para documentos presos em `Cancelando` | Documentos em `Cancelando` há muito tempo precisariam de reconciliação; escopo de robustez futuro. Extensão de `GetProcessandoAntigoAsync` para incluir `Cancelando` fica pendente |
| `nSeqEvento > 1` (segundo cancelamento após rejeição) | O schema permite múltiplos eventos; a implementação usa sempre `nSeqEvento=1`. Segundo cancelamento dentro do prazo de 30 min (após rejeição) reenvia com `nSeqEvento=1` e `Id` sufixo `"01"` — comportamento SEFAZ indefinido por UF (algumas aceitam, outras rejeitam por evento duplicado) |
| Cancelamento de NF-e (mod=55) | Escopo restrito a NFC-e (mod=65); NF-e tem fluxo similar mas endpoint diferente |
| `FakeSefazClient` para cancelamento | `CancelamentoJob` usa `SefazHttpClient` diretamente, não via `ISefazClient` — `FakeSefazClient` não participa do fluxo de cancelamento. Testes de integração testam o endpoint 202/422/403/404, não o job |
