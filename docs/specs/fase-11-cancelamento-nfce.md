# Spec Fase 11 — Cancelamento de NFC-e

**Versão:** 1.0
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
  │
  ├─ Persiste (status = Cancelando)
  ├─ Enfileira CancelamentoJob
  └─ Retorna 202 { documentoId, status: "Cancelando" }

CancelamentoJob (Hangfire)
  │
  ├─ Carrega documento (se não Cancelando → idempotência, return)
  ├─ Carrega Tenant + certificado mTLS
  ├─ Constrói XML de evento (CancelamentoEventoBuilder)
  ├─ Assina com XmlSigner
  ├─ Envia para NfeRecepcaoEvento4 (SefazHttpClient)
  │
  ├─ cStat=135 ou 155 (aceito)
  │     documento.ConfirmarCancelamento(utcNow)
  │     Cancelando → Cancelado
  │     Publica DocumentoFiscalCanceladoEvent
  │
  └─ Outros cStat (rejeitado)
        documento.RejeitarCancelamento(xMotivo)
        Cancelando → Autorizado
        Armazena xMotivo em MotivoRejeicao
```

### Decisões-chave

- **Estado intermediário `Cancelando`**: evita duplo cancelamento e dá visibilidade ao ClienteApp via `GET /status` durante o processamento assíncrono.
- **`DocumentoFiscalCanceladoEvent` só é publicado ao confirmar**: o evento existente não muda — a diferença é que agora ele é emitido por `ConfirmarCancelamento`, não por `IniciarCancelamento`.
- **`nProt` obrigatório**: o campo `Protocolo` armazenado na autorização original é a única fonte válida do número de protocolo para o XML de cancelamento.
- **Rejeição SEFAZ reverte para `Autorizado`**: o ClienteApp pode tentar novamente se ainda estiver dentro do prazo de 30 minutos.
- **`MotivoRejeicao` é sobrescrito**: o campo existente é reutilizado para registrar o motivo de rejeição do cancelamento. Na reconfirmação bem-sucedida, `RejeitarCancelamento` limpa o campo.

---

## 2. Mapa de Arquivos

| Arquivo | Ação | Responsabilidade |
|---|---|---|
| `Domain/Enums/StatusDocumento.cs` | Editar | Adicionar `Cancelando = 9` |
| `Domain/Entities/DocumentoFiscal.cs` | Editar | `IniciarCancelamento`, `ConfirmarCancelamento`, `RejeitarCancelamento`; remover `Cancelar` |
| `Domain/Errors/DocumentoFiscalErrors.cs` | Editar | `CancelamentoRejeitadoPeloSefaz` |
| `Application/Common/Interfaces/ICancelamentoJobQueue.cs` | Criar | Interface do job queue de cancelamento |
| `Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommand.cs` | Criar | Command + DTOs |
| `Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandHandler.cs` | Criar | Handler CQRS |
| `Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandValidator.cs` | Criar | Validação FluentValidation |
| `Application/Common/Models/CancelarDocumentoResponse.cs` | Criar | DTO de resposta 202 |
| `Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` | Editar | `ResolveEvento(ufCodigo, ambiente)` |
| `Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs` | Editar | `BuildEvento(xmlEvento, cUF)` |
| `Infrastructure/Fiscal/Sefaz/CancelamentoEventoBuilder.cs` | Criar | XML do evento `<infEvento>` |
| `Infrastructure/Fiscal/Sefaz/CancelamentoRetornoParser.cs` | Criar | Parse da resposta `NfeRecepcaoEvento4` |
| `Infrastructure/Jobs/CancelamentoJob.cs` | Criar | Job Hangfire de cancelamento |
| `Infrastructure/Jobs/HangfireCancelamentoJobQueue.cs` | Criar | Implementação de `ICancelamentoJobQueue` |
| `Infrastructure/DependencyInjection.cs` | Editar | Registrar `ICancelamentoJobQueue` |
| `Infrastructure/Persistence/Migrations/...AddCancelando.cs` | Criar | Migration para `Cancelando = 9` no enum |
| `Api/Program.cs` | Editar | Substituir stub 501 pelo handler real |
| `Api/ResultExtensions.cs` | Editar | Mapear `CancelamentoRejeitadoPeloSefaz` → 422 |
| `tests/.../Integration/Infrastructure/FakeSefazClient.cs` | Editar | `SimularCancelamentoAceito()`, `SimularCancelamentoRejeitado()` |
| `tests/.../Integration/CancelamentoTests.cs` | Criar | Testes de integração |
| `tests/.../Domain/DocumentoFiscalTests.cs` | Editar | Novos casos da state machine |
| `tests/.../Application/CancelarDocumentoCommandHandlerTests.cs` | Criar | Testes unitários do handler |
| `tests/.../Infrastructure/Services/CancelamentoRetornoParserTests.cs` | Criar | Testes unitários do parser |
| `tests/.../Infrastructure/Services/CancelamentoEventoBuilderTests.cs` | Criar | Testes unitários do builder de XML |

---

## 3. Domínio

### 3.1 `StatusDocumento`

Adicionar ao enum existente:

```csharp
Cancelando = 9   // aguardando confirmação do SEFAZ
```

O valor 9 não colide com nenhum status existente (1–8).

### 3.2 `DocumentoFiscal` — state machine

Substituir o método `Cancelar(TimeProvider)` existente por três métodos:

**`IniciarCancelamento(TimeProvider timeProvider)`**
- Transição: `Autorizado → Cancelando`
- Valida: `Status == Autorizado` → falha com `TransicaoInvalida` se não
- Valida: `timeProvider.GetUtcNow() < AuthorizedAt!.Value.AddMinutes(30)` → falha com `PrazoDeCancelamentoExpirado` se fora do prazo
- **Não** publica domain event (cancelamento ainda não confirmado pelo SEFAZ)
- Retorna `Result.Success()`

**`ConfirmarCancelamento(DateTimeOffset canceladoAt)`**
- Transição: `Cancelando → Cancelado`
- Valida: `Status == Cancelando` → falha com `TransicaoInvalida` se não
- Publica `DocumentoFiscalCanceladoEvent(Id, TenantId, canceladoAt, Guid.CreateVersion7(), canceladoAt)`
- Retorna `Result.Success()`

**`RejeitarCancelamento(string motivo)`**
- Transição: `Cancelando → Autorizado`
- Valida: `Status == Cancelando` → falha com `TransicaoInvalida` se não
- Armazena `MotivoRejeicao = motivo` (campo existente — reutilizado para registrar rejeição do cancelamento)
- **Não** publica domain event
- Retorna `Result.Success()`

> **Nota:** O método `Cancelar(TimeProvider)` existente é **removido**. Qualquer teste que o use deve ser migrado para `IniciarCancelamento`.

### 3.3 `DocumentoFiscalErrors`

Adicionar:

```csharp
public static readonly Error CancelamentoRejeitadoPeloSefaz =
    new("DocumentoFiscal.CancelamentoRejeitadoPeloSefaz",
        "O SEFAZ rejeitou o cancelamento. O documento retornou ao status Autorizado.");
```

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

Pipeline:
1. Carrega documento via `IDocumentoFiscalRepository.GetByIdAsync`; retorna `NaoEncontrado` se null
2. Valida ownership: `documento.ClienteAppId != command.ClienteAppId` → retorna `NaoPertenceAoClienteApp`
3. Chama `documento.IniciarCancelamento(timeProvider)` → retorna falha se status inválido ou prazo expirado
4. Persiste via `IUnitOfWork.SaveChangesAsync`
5. Enfileira via `ICancelamentoJobQueue.EnqueueCancelamentoAsync(documentoId, justificativa, ct)`
6. Retorna `Result.Success(new CancelarDocumentoResponse(documento.Id, StatusDocumento.Cancelando))`

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

### 5.1 `SefazEndpointResolver` — `ResolveEvento`

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

### 5.2 `SoapEnvelopeBuilder` — `BuildEvento`

```csharp
// Namespace do WSDL NFeRecepcaoEvento4 — diferente do de autorização.
private const string WsEventoNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";

public static string BuildEvento(string xmlEvento, int cUF)
{
    // Estrutura idêntica ao BuildAutorizacao mas com WsEventoNs e sem idLote.
    // cUF e versaoDados="1.00" no cabeçalho.
    // Body: <nfeDadosMsg> contém o xmlEvento diretamente.
}
```

### 5.3 `CancelamentoEventoBuilder`

Constrói o XML do evento de cancelamento conforme NT 2014.002 v1.04 (evento `110111`).

Estrutura do XML resultante:
```xml
<envEvento versao="1.00" xmlns="http://www.portalfiscal.inf.br/nfe">
  <idLote>{gerado pelo caller via TimeProvider}</idLote>
  <evento versao="1.00">
    <infEvento Id="ID110111{chaveAcesso}01">
      <cOrgao>{ufCodigo}</cOrgao>
      <tpAmb>{1|2}</tpAmb>
      <CNPJ>{cnpjEmitente}</CNPJ>
      <chNFe>{chaveAcesso44digitos}</chNFe>
      <dhEvento>{dhEvento formato yyyy-MM-ddTHH:mm:sszzz}</dhEvento>
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

Assinatura: `XmlSigner.Assinar` com `Reference URI = "#ID110111{chaveAcesso}01"`.

```csharp
internal static class CancelamentoEventoBuilder
{
    public static XmlDocument ConstruirEvento(
        DocumentoFiscal documento,
        Tenant tenant,
        string justificativa,
        string nProt,
        DateTimeOffset dhEvento);
}
```

**Parâmetro `nProt`:** passado pelo `CancelamentoJob`, que o lê de `documento.Protocolo`. Se `Protocolo` for null, o job deve logar erro e abortar sem retentar (pré-condição violada — documento autorizado sem protocolo).

**Formato `dhEvento`:** `dhEvento.ToString("yyyy-MM-ddTHH:mm:sszzz")` com offset do fuso da UF via `UfFusoHorario.Mapa`.

**Formato `Id` do `infEvento`:** `"ID110111" + chaveAcesso.Valor + "01"` — 44 dígitos da chave + sufixo de sequência "01" (primeiro cancelamento).

### 5.4 `CancelamentoRetornoParser`

```csharp
public sealed record CancelamentoRetorno(
    bool Aceito,
    string CStat,
    string XMotivo,
    string? NProtCancelamento);

internal static class CancelamentoRetornoParser
{
    // cStat=135: "Evento registrado e vinculado a NF-e" — cancelamento aceito
    // cStat=155: "Cancelamento homologado fora de prazo" — aceito (SEFAZ registra mas avisa)
    // Qualquer outro cStat: rejeitado
    public static Result<CancelamentoRetorno> Parse(string soapResponse);
}
```

O XML de resposta do `NfeRecepcaoEvento4` contém `<retEnvEvento>` → `<retEvento>` → `<infEvento>` → `<cStat>` e `<xMotivo>`. O `<nProt>` do cancelamento está em `<infEvento>/<nProt>` (diferente da autorização, que fica em `<protNFe>/<infProt>/<nProt>`).

### 5.5 `CancelamentoJob`

```csharp
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800],
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class CancelamentoJob
{
    public async Task ExecuteAsync(DocumentoFiscalId documentoId, string justificativa, CancellationToken ct);
}
```

Pipeline do `ExecuteAsync`:
1. Carrega documento; se `Status != Cancelando` → return (idempotência — job já processado)
2. Se `documento.Protocolo` é null → log error, return sem retry (pré-condição: autorizado sem protocolo)
3. Carrega tenant; se null/inativo → lança exceção (Hangfire faz retry)
4. Carrega certificado via `ITenantCertificateProvider`; se falha → lança exceção (Hangfire faz retry)
5. Constrói XML do evento: `CancelamentoEventoBuilder.ConstruirEvento(documento, tenant, justificativa, documento.Protocolo!, utcNow)`
6. Assina: `XmlSigner.Assinar(xmlEvento, certificate, $"ID110111{documento.ChaveAcesso.Valor}01")`
7. Monta envelope SOAP: `SoapEnvelopeBuilder.BuildEvento(xmlAssinado.OuterXml, ufCodigo)`
8. Envia: `SefazHttpClient.PostSoapAsync(SefazEndpointResolver.ResolveEvento(ufCodigo, ambiente), envelope, certificate, ct)`
9. Parseia: `CancelamentoRetornoParser.Parse(soapResponse)`
10. Se aceito (cStat 135/155):
    - `documento.ConfirmarCancelamento(utcNow)` → `Cancelando → Cancelado`
    - Persiste via `IUnitOfWork.SaveChangesAsync`
    - Log information
11. Se rejeitado:
    - `documento.RejeitarCancelamento(xMotivo)` → `Cancelando → Autorizado`
    - Persiste via `IUnitOfWork.SaveChangesAsync`
    - Log warning com cStat e xMotivo
12. Registra `DeliveryAttempt(TipoTentativa.Envio, success, responseCode, responseMessage, elapsedMs)`

> **Sem retry em rejeição fiscal**: cStat de rejeição definitiva (não 135/155) não deve ser retentado — por isso o job não lança exceção em rejeição, apenas persiste o resultado.

### 5.6 `HangfireCancelamentoJobQueue`

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

### 5.7 `DependencyInjection`

Adicionar no método `AddInfrastructure`:
```csharp
services.AddScoped<ICancelamentoJobQueue, HangfireCancelamentoJobQueue>();
```

### 5.8 Migration

A migration adiciona `Cancelando = 9` ao enum de status. Em EF Core com PostgreSQL/InMemory sem enum nativo, `StatusDocumento` é mapeado como `int` — a migration é um `AlterColumn` adicionando comentário ou simplesmente um `migrationBuilder.Sql` de documentação. Verificar o mapeamento atual em `DocumentoFiscalConfiguration` antes de gerar a migration:

```powershell
dotnet ef migrations add AddCancelando `
  --project src/VisuFiscalHub.Infrastructure `
  --startup-project src/VisuFiscalHub.Api
```

Se o enum for mapeado como `int` (padrão), a migration pode ser um no-op com apenas metadados do snapshot. Confirmar e commitar.

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
            DocumentoId  = new DocumentoFiscalId(id),
            ClienteAppId = userContext.ClienteAppId,
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

Adicionar ao bloco de 422:
```csharp
|| code.EndsWith("CancelamentoRejeitadoPeloSefaz")
```

---

## 7. Testes

### 7.1 Unitários de Domínio — `DocumentoFiscalTests`

Adicionar nos testes existentes da state machine:

```csharp
[Fact]
public void IniciarCancelamento_QuandoAutorizadoDentroDoPrazo_TransicionaParaCancelando()
{
    // Arrange: documento Autorizado há 10 minutos
    var authorizedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
    var documento = CriarDocumentoAutorizado(authorizedAt);
    var timeProvider = TimeProvider.Fixed(authorizedAt.AddMinutes(10));

    // Act
    var result = documento.IniciarCancelamento(timeProvider);

    // Assert
    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Cancelando);
    documento.DomainEvents.ShouldBeEmpty(); // nenhum evento antes da confirmação
}

[Fact]
public void IniciarCancelamento_QuandoPrazoExpirado_RetornaErro()
{
    var authorizedAt = DateTimeOffset.UtcNow.AddMinutes(-31);
    var documento = CriarDocumentoAutorizado(authorizedAt);
    var timeProvider = TimeProvider.Fixed(authorizedAt.AddMinutes(31));

    var result = documento.IniciarCancelamento(timeProvider);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    documento.Status.ShouldBe(StatusDocumento.Autorizado);
}

[Fact]
public void IniciarCancelamento_QuandoExatamente30Minutos_RetornaErro()
{
    // Boundary estrito: >= 30min não pode cancelar
    var authorizedAt = DateTimeOffset.UtcNow.AddHours(-1);
    var documento = CriarDocumentoAutorizado(authorizedAt);
    var prazoExato = authorizedAt.AddMinutes(30);
    var timeProvider = TimeProvider.Fixed(prazoExato);

    var result = documento.IniciarCancelamento(timeProvider);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
}

[Fact]
public void IniciarCancelamento_QuandoNaoAutorizado_RetornaErro()
{
    var documento = CriarDocumentoEnfileirado();
    var timeProvider = TimeProvider.System;

    var result = documento.IniciarCancelamento(timeProvider);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TransicaoInvalida");
}

[Fact]
public void ConfirmarCancelamento_QuandoCancelando_TransicionaParaCancelado()
{
    var documento = CriarDocumentoCancelando();
    var canceladoAt = DateTimeOffset.UtcNow;

    var result = documento.ConfirmarCancelamento(canceladoAt);

    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Cancelado);
    documento.DomainEvents.ShouldContain(e => e is DocumentoFiscalCanceladoEvent);
}

[Fact]
public void RejeitarCancelamento_QuandoCancelando_RevertaParaAutorizado()
{
    var documento = CriarDocumentoCancelando();

    var result = documento.RejeitarCancelamento("Cancelamento fora de prazo no SEFAZ");

    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Autorizado);
    documento.MotivoRejeicao.ShouldBe("Cancelamento fora de prazo no SEFAZ");
    documento.DomainEvents.ShouldBeEmpty();
}
```

### 7.2 Unitários de Application — `CancelarDocumentoCommandHandlerTests`

```csharp
[Fact]
public async Task Handle_QuandoDocumentoAutorizado_EnfileirarJobERetornar202()
// Verifica: IniciarCancelamento chamado, SaveChangesAsync chamado,
// ICancelamentoJobQueue.EnqueueCancelamentoAsync chamado, retorna Status=Cancelando

[Fact]
public async Task Handle_QuandoDocumentoDeOutroClienteApp_RetornarErro403()
// Verifica: ClienteAppId mismatch → NaoPertenceAoClienteApp

[Fact]
public async Task Handle_QuandoPrazoExpirado_RetornarErro422()
// Usa TimeProvider.Fixed com horário além de 30 min

[Fact]
public async Task Handle_QuandoDocumentoNaoEncontrado_RetornarErro404()
// _documentoRepo.GetByIdAsync retorna null
```

### 7.3 Unitários de Infraestrutura — `CancelamentoRetornoParserTests`

```csharp
// Fixture de resposta SOAP para cStat=135:
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
              </infEvento>
            </retEvento>
          </retEnvEvento>
        </nfeResultMsg>
      </soap12:Body>
    </soap12:Envelope>
    """;

[Fact]
public void Parse_cStat135_DeveRetornarAceito()
// Aceito=true, CStat="135", NProtCancelamento="135260000000099"

[Fact]
public void Parse_cStat155_DeveRetornarAceito()
// cStat=155 também é aceito (homologado fora de prazo)

[Fact]
public void Parse_cStatRejeicao_DeveRetornarNaoAceito()
// cStat="218" → Aceito=false, XMotivo preservado

[Fact]
public void Parse_XmlMalformado_DeveRetornarFalhaSemExcecao()
// Não lança exceção — retorna Result.Failure
```

### 7.4 Unitários de Infraestrutura — `CancelamentoEventoBuilderTests`

```csharp
[Fact]
public void ConstruirEvento_DeveConterTpEvento110111()

[Fact]
public void ConstruirEvento_DeveConterNProtDaAutorizacao()

[Fact]
public void ConstruirEvento_DeveConterXJust()

[Fact]
public void ConstruirEvento_IdInfEvento_DeveSerID110111MaisChaveAcesso()
// Id = "ID110111" + chave44digitos + "01"

[Fact]
public void ConstruirEvento_DeveConterDescEventoCancelamento()
```

### 7.5 Testes de Integração — `CancelamentoTests`

`FakeSefazClient` recebe dois novos métodos:
```csharp
public void SimularCancelamentoAceito(string nProtCancelamento = "135260000000099")
// _cancelamentoHandler retorna CancelamentoRetorno(Aceito: true, CStat: "135", ...)

public void SimularCancelamentoRejeitado(string cStat = "218", string motivo = "Rejeição simulada")
// _cancelamentoHandler retorna CancelamentoRetorno(Aceito: false, CStat: cStat, XMotivo: motivo, ...)
```

`ISefazClient` precisa de novo método `EnviarEventoCancelamentoAsync` **ou** o `CancelamentoJob` usa `SefazHttpClient` diretamente (sem passar por `ISefazClient`). **Decisão de design:** o job chama `SefazHttpClient` diretamente para manter `ISefazClient` focado em autorização. O `FakeSefazClient` não precisa implementar cancelamento — o fake é substituído apenas para `ISefazClient`. O teste de integração **não testa o job end-to-end** (isso exigiria Testcontainers); testa apenas o endpoint 202 e o estado `Cancelando` no banco.

```csharp
[Fact]
public async Task PostCancelar_DocumentoAutorizado_Retorna202ComStatusCancelando()
// Emite → POST /cancelar → verifica 202, GET /status → verifica Cancelando

[Fact]
public async Task PostCancelar_DocumentoNaoAutorizado_Retorna422()
// Emite mas não processa → status Enfileirado → POST /cancelar → 422

[Fact]
public async Task PostCancelar_SemJustificativa_Retorna422()
// body = { justificativa: "" } → 422

[Fact]
public async Task PostCancelar_JustificativaMuitoCurta_Retorna422()
// 14 chars → 422 (mínimo é 15)

[Fact]
public async Task PostCancelar_DocumentoDeOutroClienteApp_Retorna403()
// ClienteApp-2 tenta cancelar documento do ClienteApp-1 → 403

[Fact]
public async Task PostCancelar_DocumentoNaoEncontrado_Retorna404()
// GUID aleatório → 404
```

---

## 8. Self-Review

### Cobertura

| Requisito | Coberto por |
|---|---|
| Estado intermediário `Cancelando` | Seção 3.2 + Seção 3.1 |
| Validação 30 min no domínio | `IniciarCancelamento` + testes 7.1 |
| XML evento `tpEvento=110111` com `xJust` e `nProt` | `CancelamentoEventoBuilder` seção 5.3 + testes 7.4 |
| Assinatura do XML de evento | `XmlSigner` reutilizado no `CancelamentoJob` |
| Webservice `NfeRecepcaoEvento4` com mTLS | `SefazHttpClient.PostSoapAsync` + `SefazEndpointResolver.ResolveEvento` |
| Todas as 27 UFs mapeadas | Seção 5.1 |
| cStat=135 e 155 → `Cancelado` | `CancelamentoRetornoParser` + `CancelamentoJob` seção 5.5 |
| Rejeição SEFAZ → reverter para `Autorizado` | `documento.RejeitarCancelamento` |
| Registrar `DeliveryAttempt` | `CancelamentoJob` step 12 |
| Endpoint 202 substituindo 501 | Seção 6.1 |
| `NaoPertenceAoClienteApp` → 403 | Handler seção 4.3 + teste 7.2 |
| Testes unitários domain, application, infra | Seções 7.1–7.4 |
| Testes de integração | Seção 7.5 |

### Gaps conscientemente fora deste spec

| Gap | Motivo |
|---|---|
| Teste de integração end-to-end do `CancelamentoJob` | Exigiria Testcontainers + SEFAZ real ou mock HTTP; escopo separado |
| `ReconciliacaoJobProcessor` para documentos presos em `Cancelando` | Documentos em `Cancelando` há muito tempo precisariam de reconciliação; escopo de robustez futuro |
| `nSeqEvento > 1` (segundo cancelamento após rejeição) | O schema permite múltiplos eventos; a implementação atual usa sempre `nSeqEvento=1` |
| Cancelamento de NF-e (mod=55) | Escopo restrito a NFC-e (mod=65); NF-e tem fluxo similar mas endpoint diferente |
