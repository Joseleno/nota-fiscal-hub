# Fase 11 — Cancelamento de NFC-e — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the full NFC-e cancellation flow: `POST /api/v1/documentos/{id}/cancelar` transitions `Autorizado → Cancelando → Cancelado` (confirmed by SEFAZ) or `Cancelando → Autorizado` (SEFAZ rejection), with asynchronous Hangfire job sending event `110111` to `NfeRecepcaoEvento4`.

**Architecture:** Domain state machine gains three new methods (`IniciarCancelamento`, `ConfirmarCancelamento`, `RejeitarCancelamento`) replacing `Cancelar`. The API handler transitions the document synchronously to `Cancelando` and enqueues a Hangfire job; the job constructs, signs, and sends the cancellation XML, then transitions to `Cancelado` or reverts to `Autorizado`. `XmlSigner.Assinar` is refactored to accept a full `referenceUri` so it can sign both NFC-e (`#NFe{chave}`) and cancellation events (`#ID110111{chave}01`).

**Tech Stack:** .NET 10 · EF Core + Npgsql · Hangfire · `System.Security.Cryptography.Xml.SignedXml` · SOAP 1.2 · xUnit + Shouldly + NSubstitute · FluentValidation · Mediator.SourceGenerator

---

## File Map

| File | Action |
|---|---|
| `src/VisuFiscalHub.Domain/Enums/StatusDocumento.cs` | Add `Cancelando = 9` |
| `src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs` | Add `Cancelamento = 4` |
| `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs` | Add `CanceladoAt`, replace `Cancelar` with 3 new methods |
| `src/VisuFiscalHub.Infrastructure/Fiscal/XmlSigner.cs` | Replace `chaveAcesso` param with `referenceUri` |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs` | Update `XmlSigner.Assinar` call site |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` | Add `ResolveEvento` method |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs` | Add `BuildEvento` method |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoEventoBuilder.cs` | **Create** — builds unsigned cancellation XML |
| `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoRetornoParser.cs` | **Create** — parses `NfeRecepcaoEvento4` response |
| `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs` | **Create** — Hangfire job |
| `src/VisuFiscalHub.Infrastructure/Jobs/HangfireCancelamentoJobQueue.cs` | **Create** — ICancelamentoJobQueue impl |
| `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs` | Map `cancelado_at` column |
| `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs` | Register `ICancelamentoJobQueue` + `CancelamentoJob` |
| `src/VisuFiscalHub.Application/Common/Interfaces/ICancelamentoJobQueue.cs` | **Create** — interface |
| `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommand.cs` | **Create** |
| `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandHandler.cs` | **Create** |
| `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandValidator.cs` | **Create** |
| `src/VisuFiscalHub.Application/Common/Models/CancelarDocumentoResponse.cs` | **Create** |
| `src/VisuFiscalHub.Api/Program.cs` | Replace 501 stub with real endpoint |
| `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/…` | `dotnet ef migrations add AddCancelando` |
| `tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs` | Add `Autorizado()` + `Cancelando()` helpers |
| `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs` | Remove 4 `Cancelar_*` tests; add 11 new tests |
| `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/XmlSignerTests.cs` | Update 5 tests for new `referenceUri` param |
| `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoRetornoParserTests.cs` | **Create** |
| `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoEventoBuilderTests.cs` | **Create** |
| `tests/VisuFiscalHub.Tests/Application/CancelarDocumentoCommandHandlerTests.cs` | **Create** |
| `tests/VisuFiscalHub.Tests/Integration/CancelamentoTests.cs` | **Create** |

---

## Task 1: Domain enums — `Cancelando` and `Cancelamento`

**Files:**
- Modify: `src/VisuFiscalHub.Domain/Enums/StatusDocumento.cs`
- Modify: `src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs`

These are pure enum additions — no tests needed beyond compilation check.

- [ ] **Step 1: Add `Cancelando = 9` to StatusDocumento**

Open `src/VisuFiscalHub.Domain/Enums/StatusDocumento.cs` (currently ends at `Denegado = 8`). Add:

```csharp
public enum StatusDocumento
{
    Criado = 1,
    Enfileirado = 2,
    Processando = 3,
    Autorizado = 4,
    Rejeitado = 5,
    Cancelado = 6,
    Falhou = 7,
    Denegado = 8,
    Cancelando = 9   // aguardando confirmação do SEFAZ
}
```

- [ ] **Step 2: Add `Cancelamento = 4` to TipoTentativa**

Open `src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs` (currently has 3 values). Add:

```csharp
public enum TipoTentativa
{
    Envio = 1,
    Consulta = 2,
    Retry = 3,
    Cancelamento = 4   // tentativa de cancelamento via NfeRecepcaoEvento4
}
```

- [ ] **Step 3: Build to verify no compilation errors**

Run:
```powershell
dotnet build src/VisuFiscalHub.Domain/VisuFiscalHub.Domain.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add src/VisuFiscalHub.Domain/Enums/StatusDocumento.cs `
        src/VisuFiscalHub.Domain/Enums/TipoTentativa.cs
git commit -m "feat: adicionar StatusDocumento.Cancelando=9 e TipoTentativa.Cancelamento=4"
```

---

## Task 2: DocumentoFiscalBuilder — helpers `Autorizado` and `Cancelando`

**Files:**
- Modify: `tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs`

These builders are needed by domain tests in Task 3.

- [ ] **Step 1: Add `Autorizado` and `Cancelando` to builder**

Open `tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs`. After the `Processando` method (line ~98), add two new methods:

```csharp
/// <summary>
/// Cria um DocumentoFiscal em estado Autorizado com AuthorizedAt e Protocolo preenchidos.
/// Requer FixedTimeProvider — garante valores determinísticos sem depender do relógio do sistema.
/// </summary>
internal static DocumentoFiscal Autorizado(DateTimeOffset authorizedAt, long numero = 1)
{
    var doc = Processando(numero);
    var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc123", "https://exemplo.com").Value;
    doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, new FixedTimeProvider(authorizedAt))
       .IsSuccess.ShouldBeTrue("Autorizar() falhou no builder — verifique as invariantes.");
    return doc;
}

/// <summary>
/// Cria um DocumentoFiscal em estado Cancelando.
/// IniciarCancelamento é chamado 1 minuto após authorizedAt (dentro do prazo de 30 min).
/// </summary>
internal static DocumentoFiscal Cancelando(DateTimeOffset authorizedAt, long numero = 1)
{
    var doc = Autorizado(authorizedAt, numero);
    doc.IniciarCancelamento(new FixedTimeProvider(authorizedAt.AddMinutes(1)))
       .IsSuccess.ShouldBeTrue("IniciarCancelamento() falhou no builder — verifique as invariantes.");
    return doc;
}
```

Add the required using if not present (it uses `FixedTimeProvider`):
```csharp
using VisuFiscalHub.Tests.Helpers; // FixedTimeProvider is already in this namespace
```

**Note:** `FixedTimeProvider` is already used elsewhere in the test project. Check it's accessible — if it's in `VisuFiscalHub.Tests.Helpers`, no using needed since the builder is in the same namespace.

- [ ] **Step 2: Verify it compiles**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```
Expected: Build succeeded.

Note: `IniciarCancelamento` doesn't exist yet (defined in Task 3). The build will **fail** until Task 3 is complete. This step verifies the builder syntax is correct once the domain is implemented. Skip this step now — come back after Task 3.

- [ ] **Step 3: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs
git commit -m "test: adicionar builders Autorizado() e Cancelando() para testes de cancelamento"
```

---

## Task 3: Domain entity — replace `Cancelar` with three methods + `CanceladoAt`

**Files:**
- Modify: `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`
- Modify: `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`

**TDD approach:** Write the failing tests first, then implement the methods.

- [ ] **Step 1: Remove 4 old `Cancelar_*` tests from DocumentoFiscalTests.cs**

Open `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`. Delete the following 4 test methods (lines 100–181 approximately):
- `Cancelar_QuandoCriado_DeveRetornarErro` (line ~100)
- `Cancelar_QuandoDentro30Minutos_DeveTransicionarParaCancelado` (line ~141)
- `Cancelar_QuandoFora30Minutos_DeveRetornarErro` (line ~155)
- `Cancelar_QuandoExatamente30Minutos_DeveRetornarErro` (line ~169)

These test the old `Cancelar()` method which is being removed.

- [ ] **Step 2: Add failing tests for the new state machine methods**

Add to `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs` (inside the class, after existing tests):

```csharp
// ──────────────────────────────────────────────────────────────
// IniciarCancelamento
// ──────────────────────────────────────────────────────────────

[Fact]
public void IniciarCancelamento_QuandoAutorizadoDentroDoPrazo_TransicionaParaCancelando()
{
    var documento = DocumentoFiscalBuilder.Autorizado(FixedNow.AddMinutes(-10));
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Cancelando);
    documento.MotivoRejeicao.ShouldBeNull();
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

// ──────────────────────────────────────────────────────────────
// ConfirmarCancelamento
// ──────────────────────────────────────────────────────────────

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

// ──────────────────────────────────────────────────────────────
// RejeitarCancelamento
// ──────────────────────────────────────────────────────────────

[Fact]
public void RejeitarCancelamento_QuandoCancelando_RevertaParaAutorizado()
{
    var documento = DocumentoFiscalBuilder.Cancelando(FixedNow.AddMinutes(-5));
    var result = documento.RejeitarCancelamento("Prazo encerrado no SEFAZ");
    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Autorizado);
    documento.MotivoRejeicao.ShouldBe("Prazo encerrado no SEFAZ");
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
    var authorizedAt = FixedNow.AddMinutes(-5);
    var documento = DocumentoFiscalBuilder.Cancelando(authorizedAt);
    documento.RejeitarCancelamento("Motivo anterior");
    var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
    result.IsSuccess.ShouldBeTrue();
    documento.MotivoRejeicao.ShouldBeNull();
}
```

- [ ] **Step 3: Run tests to verify they fail**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~DocumentoFiscalTests" --no-build
```
Expected: Compilation failure — `IniciarCancelamento`, `ConfirmarCancelamento`, `RejeitarCancelamento`, `CanceladoAt` not found. (You may need to run `dotnet build` first to see the errors clearly.)

- [ ] **Step 4: Implement new methods in DocumentoFiscal.cs**

Open `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`. 

**4a.** Add the `CanceladoAt` property after `AuthorizedAt`:
```csharp
public DateTimeOffset? CanceladoAt { get; private set; }
```

**4b.** Replace the existing `Cancelar(TimeProvider timeProvider)` method (lines ~210–231) with these three methods:

```csharp
public Result IniciarCancelamento(TimeProvider timeProvider)
{
    if (Status != StatusDocumento.Autorizado)
        return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

    if (AuthorizedAt is null)
        return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

    var utcNow = timeProvider.GetUtcNow();
    if (utcNow >= AuthorizedAt.Value.AddMinutes(30))
        return Result.Failure(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado);

    MotivoRejeicao = null;
    Status = StatusDocumento.Cancelando;
    return Result.Success();
}

public Result ConfirmarCancelamento(DateTimeOffset canceladoAt)
{
    if (Status != StatusDocumento.Cancelando)
        return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

    Status = StatusDocumento.Cancelado;
    CanceladoAt = canceladoAt;

    AddDomainEvent(new DocumentoFiscalCanceladoEvent(
        Id,
        TenantId,
        canceladoAt,
        Guid.CreateVersion7(),
        canceladoAt));

    return Result.Success();
}

public Result RejeitarCancelamento(string motivo)
{
    if (Status != StatusDocumento.Cancelando)
        return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

    Status = StatusDocumento.Autorizado;
    MotivoRejeicao = motivo;
    return Result.Success();
}
```

- [ ] **Step 5: Run tests to verify they pass**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~DocumentoFiscalTests"
```
Expected: All tests pass. (There will be compile errors elsewhere due to removed `Cancelar()` — ignore for now, fix in next step.)

- [ ] **Step 6: Fix NfceProcessingJob guard to include `Cancelando`**

Open `src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs`. Find the idempotency guard (lines ~58–67):

```csharp
if (documento.Status is StatusDocumento.Autorizado
    or StatusDocumento.Rejeitado
    or StatusDocumento.Denegado
    or StatusDocumento.Cancelado
    or StatusDocumento.Falhou)
```

Add `Cancelando`:
```csharp
if (documento.Status is StatusDocumento.Autorizado
    or StatusDocumento.Rejeitado
    or StatusDocumento.Denegado
    or StatusDocumento.Cancelado
    or StatusDocumento.Cancelando
    or StatusDocumento.Falhou)
```

- [ ] **Step 7: Build whole solution**

```powershell
dotnet build VisuFiscalHub.sln
```
Expected: Build succeeded (no references to old `Cancelar()` remain outside of the 4 deleted tests).

- [ ] **Step 8: Run all tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```
Expected: All previously passing tests pass.

- [ ] **Step 9: Commit**

```powershell
git add src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs `
        src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs `
        tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs `
        tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs
git commit -m "feat: substituir Cancelar() por IniciarCancelamento/ConfirmarCancelamento/RejeitarCancelamento + CanceladoAt"
```

---

## Task 4: XmlSigner — refactor `chaveAcesso` → `referenceUri`

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/XmlSigner.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs`
- Modify: `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/XmlSignerTests.cs`

- [ ] **Step 1: Update XmlSignerTests.cs to expect `referenceUri` param**

Open `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/XmlSignerTests.cs`.

The existing `ChaveAcesso` constant is `"35090614200167140065125001000001800100000097"`.

In all 5 test methods where `signer.Assinar(doc, cert, ChaveAcesso)` is called, replace with `signer.Assinar(doc, cert, $"#NFe{ChaveAcesso}")`.

Also update the `Assinar_ReferenceUri_DeveConterPrefixoNFe` test assertion:
```csharp
// BEFORE (line ~116):
uri.ShouldBe($"#NFe{ChaveAcesso}");

// AFTER (no change needed to assertion — the URI should still be #NFe{ChaveAcesso},
// but now it comes from the parameter directly, not internally constructed)
uri.ShouldBe($"#NFe{ChaveAcesso}");
```

The 5 call sites to change:
1. `Assinar_CanonicalizationMethod_DeveSerC14NInclusivo` — line ~46
2. `Assinar_Transform_DeveSerC14NInclusivo` — line ~67
3. `Assinar_SignatureMethod_DeveSerRSASHA1` — line ~90
4. `Assinar_ReferenceUri_DeveConterPrefixoNFe` — line ~108
5. `Assinar_XmlAssinado_DevePassarVerificacaoCheckSignature` — line ~126

Full updated test file (replace all call sites):
```csharp
// In each test method, change:
var signed = signer.Assinar(doc, cert, ChaveAcesso);
// to:
var signed = signer.Assinar(doc, cert, $"#NFe{ChaveAcesso}");
```

- [ ] **Step 2: Run XmlSignerTests to verify they fail**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~XmlSignerTests"
```
Expected: 5 tests fail — `Assinar` still takes `chaveAcesso`, not `referenceUri` with `#`.

- [ ] **Step 3: Refactor XmlSigner.cs**

Open `src/VisuFiscalHub.Infrastructure/Fiscal/XmlSigner.cs`. Replace the whole method body:

```csharp
internal sealed class XmlSigner
{
    public XmlDocument Assinar(XmlDocument xmlDoc, X509Certificate2 certificado, string referenceUri)
    {
        var signedXml = new SignedXml(xmlDoc);

        using var rsa = certificado.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Certificado não possui chave privada RSA.");

        signedXml.SigningKey = rsa;
        signedXml.SignedInfo!.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;

        var reference = new Reference
        {
            Uri = referenceUri,
            DigestMethod = SignedXml.XmlDsigSHA1Url
        };

        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());

        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificado));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();

        var xmlSignature = signedXml.GetXml()
            ?? throw new InvalidOperationException("ComputeSignature produziu elemento nulo.");

        xmlDoc.DocumentElement!.AppendChild(xmlDoc.ImportNode(xmlSignature, true));

        return xmlDoc;
    }
}
```

- [ ] **Step 4: Update SefazClient.cs call site**

Open `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs`. Find (line ~83):

```csharp
xmlAssinado = _signer.Assinar(
    xmlResult.Value,
    certificate,
    documento.ChaveAcesso.Valor);
```

Change to:
```csharp
xmlAssinado = _signer.Assinar(
    xmlResult.Value,
    certificate,
    $"#NFe{documento.ChaveAcesso.Valor}");
```

- [ ] **Step 5: Run XmlSignerTests to verify they pass**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~XmlSignerTests"
```
Expected: All 5 tests pass.

- [ ] **Step 6: Run all tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```
Expected: All tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/XmlSigner.cs `
        src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs `
        tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/XmlSignerTests.cs
git commit -m "refactor: XmlSigner.Assinar aceita referenceUri completa em vez de chaveAcesso"
```

---

## Task 5: SefazEndpointResolver — add `ResolveEvento`

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs`

No unit tests for endpoint strings (they'd just be copy-paste of the data; the integration test will catch errors). The existing pattern in `Autorizacao` and `ConsultaProtocolo` is what to follow.

- [ ] **Step 1: Add `ResolveEvento` to SefazEndpointResolver.cs**

Open `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs`. Add this method after `RetAutorizacao`:

```csharp
/// <summary>
/// Retorna a URL do webservice de recepção de eventos NFC-e (NFeRecepcaoEvento4).
/// Mapeamento explícito por UF — nunca derivado por string.Replace de outro endpoint.
/// </summary>
public static string ResolveEvento(int ufCodigo, AmbienteSefaz ambiente)
{
    var h = ambiente == AmbienteSefaz.Homologacao;

    if (UfsSvrs.Contains(ufCodigo))
    {
        var svrs = h ? SvrsH : SvrsP;
        return $"{svrs}/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
    }

    return ufCodigo switch
    {
        13 => h ? "https://nfce-homologacao.sefaz.am.gov.br/services/NfeRecepcaoEvento4"
                : "https://nfce.sefaz.am.gov.br/services/NfeRecepcaoEvento4",               // AM
        15 => h ? "https://appnfce.sefa.pa.gov.br:444/nfce-homologacao/NFeRecepcaoEvento4"
                : "https://appnfce.sefa.pa.gov.br:444/nfce/NFeRecepcaoEvento4",              // PA
        21 => h ? "https://hom.sefaz.ma.gov.br/nfce/NFeRecepcaoEvento4"
                : "https://www.sefaz.ma.gov.br/nfce/NFeRecepcaoEvento4",                     // MA
        23 => h ? "https://nfceh.sefaz.ce.gov.br/nfce/NFeRecepcaoEvento4"
                : "https://nfce.sefaz.ce.gov.br/nfce/NFeRecepcaoEvento4",                    // CE
        26 => h ? "https://nfcehomolog.sefaz.pe.gov.br/nfce-server/services/NFeRecepcaoEvento4"
                : "https://nfce.sefaz.pe.gov.br/nfce-server/services/NFeRecepcaoEvento4",    // PE
        29 => h ? "https://hnfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
                : "https://nfe.sefaz.ba.gov.br/webservices/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx", // BA
        31 => h ? "https://hnfce.fazenda.mg.gov.br/nfce/services/NFeRecepcaoEvento4"
                : "https://nfce.fazenda.mg.gov.br/nfce/services/NFeRecepcaoEvento4",          // MG
        35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfceservice/services/NFeRecepcaoEvento4"
                : "https://nfe.fazenda.sp.gov.br/nfceservice/services/NFeRecepcaoEvento4",    // SP
        41 => h ? "https://homologacao.nfce.pr.gov.br/nfce/NFeRecepcaoEvento4"
                : "https://nfce.pr.gov.br/nfce/NFeRecepcaoEvento4",                           // PR
        43 => h ? "https://nfce-homologacao.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx"
                : "https://nfce.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx", // RS
        50 => h ? "https://homologacao.nfce.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4"
                : "https://nfce.fazenda.ms.gov.br/ws/NFeRecepcaoEvento4",                     // MS
        51 => h ? "https://homologacao.sefaz.mt.gov.br/nfce/NFeRecepcaoEvento4"
                : "https://nfce.sefaz.mt.gov.br/nfce/NFeRecepcaoEvento4",                     // MT
        52 => h ? "https://homologacao.nfce.sefaz.go.gov.br/nfce/NFeRecepcaoEvento4"
                : "https://nfce.sefaz.go.gov.br/nfce/NFeRecepcaoEvento4",                     // GO
        _ => throw new InvalidOperationException($"UF {ufCodigo} não mapeada em ResolveEvento.")
    };
}
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs
git commit -m "feat: SefazEndpointResolver.ResolveEvento() para NFeRecepcaoEvento4 (27 UFs)"
```

---

## Task 6: SoapEnvelopeBuilder — add `BuildEvento`

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs`

- [ ] **Step 1: Add `BuildEvento` to SoapEnvelopeBuilder.cs**

Open `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs`. Add after `BuildAutorizacao`:

```csharp
private const string WsEventoNs = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";

/// <summary>
/// Monta o envelope SOAP 1.2 para NFeRecepcaoEvento4.
/// versaoDados="1.00" — diferente do "4.00" de autorização.
/// xmlEvento: string do XmlDocument assinado de envEvento (já inclui a assinatura).
/// </summary>
public static string BuildEvento(string xmlEvento, int cUF)
{
    var doc = new XmlDocument();
    var envelope = doc.CreateElement("soap12", "Envelope", SoapNs);

    var header = doc.CreateElement("soap12", "Header", SoapNs);
    var nfeCabMsg = doc.CreateElement("nfeCabMsg", WsEventoNs);
    AddChild(doc, nfeCabMsg, WsEventoNs, "cUF", cUF.ToString());
    AddChild(doc, nfeCabMsg, WsEventoNs, "versaoDados", "1.00");
    header.AppendChild(nfeCabMsg);
    envelope.AppendChild(header);

    var body = doc.CreateElement("soap12", "Body", SoapNs);
    var nfeDadosMsg = doc.CreateElement("nfeDadosMsg", WsEventoNs);
    nfeDadosMsg.InnerXml = xmlEvento;
    body.AppendChild(nfeDadosMsg);
    envelope.AppendChild(body);

    doc.AppendChild(envelope);
    return doc.OuterXml;
}
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilder.cs
git commit -m "feat: SoapEnvelopeBuilder.BuildEvento() para NFeRecepcaoEvento4 (versaoDados=1.00)"
```

---

## Task 7: CancelamentoRetornoParser — parse NfeRecepcaoEvento4 response

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoRetornoParser.cs`
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoRetornoParserTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoRetornoParserTests.cs`:

```csharp
using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class CancelamentoRetornoParserTests
{
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
        result.Value.DhRegEvento.ShouldNotBeNull();
        result.Value.DhRegEvento!.Value.Offset.ShouldBe(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void Parse_cStat155_DeveRetornarAceito()
    {
        // cStat=155: "Cancelamento homologado fora de prazo" — SEFAZ aceita mesmo
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
        result.Value.XMotivo.ShouldContain("218");
    }

    [Fact]
    public void Parse_XmlMalformado_DeveRetornarFalhaSemExcecao()
    {
        var result = CancelamentoRetornoParser.Parse("<broken xml");
        result.IsFailure.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run to verify tests fail (compilation expected)**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-String "error"
```
Expected: `CancelamentoRetornoParser` does not exist.

- [ ] **Step 3: Implement CancelamentoRetornoParser.cs**

Create `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoRetornoParser.cs`:

```csharp
using System.Globalization;
using System.Xml;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

public sealed record CancelamentoRetorno(
    bool Aceito,
    string CStat,
    string XMotivo,
    string? NProtCancelamento,
    DateTimeOffset? DhRegEvento);

internal static class CancelamentoRetornoParser
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    // cStat=135: Evento registrado e vinculado a NF-e — aceito
    // cStat=155: Cancelamento homologado fora de prazo — aceito (SEFAZ registra mesmo após prazo SEFAZ;
    //            é distinto do prazo de 30 min validado no domínio)
    private static readonly IReadOnlySet<string> CStatAceito = new HashSet<string> { "135", "155" };

    public static Result<CancelamentoRetorno> Parse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfe", NfeNs);

            var infEvento = doc.SelectSingleNode("//nfe:retEvento/nfe:infEvento", ns);
            if (infEvento is null)
                return Result.Failure<CancelamentoRetorno>(
                    new Error("Sefaz.CancelamentoRetornoInvalido", "Resposta não contém infEvento."));

            var cStat   = infEvento.SelectSingleNode("nfe:cStat",   ns)?.InnerText ?? string.Empty;
            var xMotivo = infEvento.SelectSingleNode("nfe:xMotivo", ns)?.InnerText ?? string.Empty;
            var nProt   = infEvento.SelectSingleNode("nfe:nProt",   ns)?.InnerText;
            var dhRaw   = infEvento.SelectSingleNode("nfe:dhRegEvento", ns)?.InnerText;

            DateTimeOffset? dhRegEvento = null;
            if (!string.IsNullOrEmpty(dhRaw))
                dhRegEvento = DateTimeOffset.Parse(dhRaw, null, DateTimeStyles.RoundtripKind);

            return Result.Success(new CancelamentoRetorno(
                Aceito:             CStatAceito.Contains(cStat),
                CStat:              cStat,
                XMotivo:            xMotivo,
                NProtCancelamento:  nProt,
                DhRegEvento:        dhRegEvento));
        }
        catch (XmlException ex)
        {
            return Result.Failure<CancelamentoRetorno>(
                new Error("Sefaz.CancelamentoXmlInvalido", $"Falha ao parsear retorno: {ex.Message}"));
        }
    }
}
```

- [ ] **Step 4: Run parser tests to verify they pass**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~CancelamentoRetornoParserTests"
```
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoRetornoParser.cs `
        tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoRetornoParserTests.cs
git commit -m "feat: CancelamentoRetornoParser para NFeRecepcaoEvento4 (cStat 135/155)"
```

---

## Task 8: CancelamentoEventoBuilder — build unsigned cancellation XML

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoEventoBuilder.cs`
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoEventoBuilderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoEventoBuilderTests.cs`:

```csharp
using System.Xml;
using Shouldly;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class CancelamentoEventoBuilderTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static XmlNamespaceManager NfNs(XmlDocument doc)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
        return ns;
    }

    private static DocumentoFiscal CriarDocumentoParaBuilder()
        => DocumentoFiscalBuilder.Autorizado(FixedNow);

    private static Tenant CriarTenant()
    {
        var cnpj = Cnpj.Criar("11.222.333/0001-81").Value;
        var config = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35).Value;
        var endereco = Endereco.Criar(
            "Rua Teste", "100", null, "Centro", "São Paulo", 3550308, "SP", "01310100").Value;
        return Tenant.Criar(
            ClienteAppId.New(), cnpj, "Empresa Teste", null, config, endereco, TimeProvider.System).Value;
    }

    [Fact]
    public void ConstruirEvento_DeveConterTpEvento110111()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, idLote: "202605241000000");

        xml.SelectSingleNode("//nfe:tpEvento", NfNs(xml))!.InnerText.ShouldBe("110111");
    }

    [Fact]
    public void ConstruirEvento_DeveConterNProtDaAutorizacao()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        xml.SelectSingleNode("//nfe:nProt", NfNs(xml))!.InnerText.ShouldBe("PROT001");
    }

    [Fact]
    public void ConstruirEvento_DeveConterXJust()
    {
        const string just = "Justificativa de teste com tamanho suficiente";
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), just, "PROT001", FixedNow, "202605241000000");

        xml.SelectSingleNode("//nfe:xJust", NfNs(xml))!.InnerText.ShouldBe(just);
    }

    [Fact]
    public void ConstruirEvento_IdInfEvento_DeveSerID110111MaisChaveAcesso()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        var id = xml.SelectSingleNode("//nfe:infEvento", NfNs(xml))!.Attributes!["Id"]!.Value;
        id.ShouldBe($"ID110111{doc.ChaveAcesso.Valor}01");
    }

    [Fact]
    public void ConstruirEvento_DeveConterDescEventoCancelamento()
    {
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        xml.SelectSingleNode("//nfe:descEvento", NfNs(xml))!.InnerText.ShouldBe("Cancelamento");
    }

    [Fact]
    public void ConstruirEvento_NaoDeveConterElementoSignature()
    {
        // ConstruirEvento retorna XML NÃO assinado — assinatura é responsabilidade do CancelamentoJob
        var doc = CriarDocumentoParaBuilder();
        var xml = CancelamentoEventoBuilder.ConstruirEvento(
            doc, CriarTenant(), "Justificativa de teste com tamanho suficiente", "PROT001",
            FixedNow, "202605241000000");

        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        xml.SelectSingleNode("//ds:Signature", ns).ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run to verify tests fail**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-String "error"
```
Expected: `CancelamentoEventoBuilder` not found.

- [ ] **Step 3: Implement CancelamentoEventoBuilder.cs**

Create `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoEventoBuilder.cs`:

```csharp
using System.Xml;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

/// <summary>
/// Constrói o XML do evento de cancelamento NFC-e conforme NT 2014.002 v1.04 (tpEvento=110111).
/// Retorna XmlDocument NÃO assinado — a assinatura é aplicada pelo CancelamentoJob via XmlSigner.
/// </summary>
internal static class CancelamentoEventoBuilder
{
    private const string NfeNs = "http://www.portalfiscal.inf.br/nfe";

    public static XmlDocument ConstruirEvento(
        DocumentoFiscal documento,
        Tenant tenant,
        string justificativa,
        string nProt,
        DateTimeOffset utcNow,
        string idLote)
    {
        var chave     = documento.ChaveAcesso.Valor;
        var ufCodigo  = tenant.ConfiguracaoFiscal.UfCodigo;
        var tpAmb     = (int)tenant.ConfiguracaoFiscal.Ambiente;
        var cnpj      = tenant.Cnpj.NumeroLimpo;

        // Converte UTC para o fuso da UF para conformidade com o schema SEFAZ (dhEvento com offset correto)
        var fusoUf    = UfFusoHorario.Mapa[ufCodigo];
        var dhEvento  = new DateTimeOffset(utcNow.UtcDateTime, fusoUf)
                            .ToString("yyyy-MM-ddTHH:mm:sszzz");

        var doc = new XmlDocument();
        var envEvento = doc.CreateElement("envEvento", NfeNs);
        envEvento.SetAttribute("versao", "1.00");

        Add(doc, envEvento, "idLote", idLote);

        var evento = doc.CreateElement("evento", NfeNs);
        evento.SetAttribute("versao", "1.00");

        var infEvento = doc.CreateElement("infEvento", NfeNs);
        infEvento.SetAttribute("Id", $"ID110111{chave}01");

        Add(doc, infEvento, "cOrgao",     ufCodigo.ToString());
        Add(doc, infEvento, "tpAmb",      tpAmb.ToString());
        Add(doc, infEvento, "CNPJ",       cnpj);
        Add(doc, infEvento, "chNFe",      chave);
        Add(doc, infEvento, "dhEvento",   dhEvento);
        Add(doc, infEvento, "tpEvento",   "110111");
        Add(doc, infEvento, "nSeqEvento", "1");
        Add(doc, infEvento, "verEvento",  "1.00");

        var detEvento = doc.CreateElement("detEvento", NfeNs);
        detEvento.SetAttribute("versao", "1.00");
        Add(doc, detEvento, "descEvento", "Cancelamento");
        Add(doc, detEvento, "nProt",      nProt);
        Add(doc, detEvento, "xJust",      justificativa);

        infEvento.AppendChild(detEvento);
        evento.AppendChild(infEvento);
        envEvento.AppendChild(evento);
        doc.AppendChild(envEvento);

        return doc;
    }

    private static void Add(XmlDocument doc, XmlElement parent, string tag, string value)
    {
        var el = doc.CreateElement(tag, NfeNs);
        el.InnerText = value;
        parent.AppendChild(el);
    }
}
```

- [ ] **Step 4: Run builder tests to verify they pass**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~CancelamentoEventoBuilderTests"
```
Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/CancelamentoEventoBuilder.cs `
        tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/CancelamentoEventoBuilderTests.cs
git commit -m "feat: CancelamentoEventoBuilder — XML de evento 110111 (não assinado)"
```

---

## Task 9: Application layer — ICancelamentoJobQueue, Command, Handler, Validator, Response

**Files:**
- Create: `src/VisuFiscalHub.Application/Common/Interfaces/ICancelamentoJobQueue.cs`
- Create: `src/VisuFiscalHub.Application/Common/Models/CancelarDocumentoResponse.cs`
- Create: `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommand.cs`
- Create: `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandValidator.cs`
- Create: `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandHandler.cs`
- Create: `tests/VisuFiscalHub.Tests/Application/CancelarDocumentoCommandHandlerTests.cs`

- [ ] **Step 1: Write failing handler tests**

Create `tests/VisuFiscalHub.Tests/Application/CancelarDocumentoCommandHandlerTests.cs`:

```csharp
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Application;

public class CancelarDocumentoCommandHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static (CancelarDocumentoCommandHandler handler,
                    IDocumentoFiscalRepository docRepo,
                    IUnitOfWork unitOfWork,
                    ICancelamentoJobQueue jobQueue)
        CriarHandler(FixedTimeProvider timeProvider)
    {
        var docRepo    = Substitute.For<IDocumentoFiscalRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var jobQueue   = Substitute.For<ICancelamentoJobQueue>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));
        jobQueue.EnqueueCancelamentoAsync(
            Arg.Any<DocumentoFiscalId>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        await jobQueue.Received(1).EnqueueCancelamentoAsync(
            documento.Id, command.Justificativa, Arg.Any<CancellationToken>());
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
}
```

- [ ] **Step 2: Run to verify compilation fails**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj 2>&1 | Select-String "error"
```
Expected: Multiple types not found.

- [ ] **Step 3: Create ICancelamentoJobQueue**

Create `src/VisuFiscalHub.Application/Common/Interfaces/ICancelamentoJobQueue.cs`:

```csharp
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface ICancelamentoJobQueue
{
    Task EnqueueCancelamentoAsync(
        DocumentoFiscalId id,
        string justificativa,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create CancelarDocumentoResponse**

Create `src/VisuFiscalHub.Application/Common/Models/CancelarDocumentoResponse.cs`:

```csharp
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Models;

public sealed record CancelarDocumentoResponse(
    DocumentoFiscalId DocumentoId,
    StatusDocumento Status);
```

- [ ] **Step 5: Create CancelarDocumentoCommand**

Create `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommand.cs`:

```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;

public sealed record CancelarDocumentoCommand : ICommand<Result<CancelarDocumentoResponse>>
{
    public DocumentoFiscalId DocumentoId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string Justificativa { get; init; } = string.Empty;
}
```

- [ ] **Step 6: Create CancelarDocumentoCommandValidator**

Create `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandValidator.cs`:

```csharp
using FluentValidation;

namespace VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;

public sealed class CancelarDocumentoCommandValidator : AbstractValidator<CancelarDocumentoCommand>
{
    public CancelarDocumentoCommandValidator()
    {
        RuleFor(x => x.Justificativa)
            .NotEmpty()
            .MinimumLength(15)
            .MaximumLength(255);
    }
}
```

- [ ] **Step 7: Create CancelarDocumentoCommandHandler**

Create `src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/CancelarDocumentoCommandHandler.cs`:

```csharp
using Mediator;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;

public sealed class CancelarDocumentoCommandHandler
    : ICommandHandler<CancelarDocumentoCommand, Result<CancelarDocumentoResponse>>
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICancelamentoJobQueue _jobQueue;
    private readonly TimeProvider _timeProvider;

    public CancelarDocumentoCommandHandler(
        IDocumentoFiscalRepository documentoRepo,
        IUnitOfWork unitOfWork,
        ICancelamentoJobQueue jobQueue,
        TimeProvider timeProvider)
    {
        _documentoRepo = documentoRepo;
        _unitOfWork    = unitOfWork;
        _jobQueue      = jobQueue;
        _timeProvider  = timeProvider;
    }

    public async ValueTask<Result<CancelarDocumentoResponse>> Handle(
        CancelarDocumentoCommand command,
        CancellationToken cancellationToken)
    {
        var documento = await _documentoRepo.GetByIdAsync(command.DocumentoId, cancellationToken);
        if (documento is null)
            return Result.Failure<CancelarDocumentoResponse>(DocumentoFiscalErrors.NaoEncontrado);

        if (documento.ClienteAppId != command.ClienteAppId)
            return Result.Failure<CancelarDocumentoResponse>(TenantErrors.NaoPertenceAoClienteApp);

        var iniciarResult = documento.IniciarCancelamento(_timeProvider);
        if (iniciarResult.IsFailure)
            return Result.Failure<CancelarDocumentoResponse>(iniciarResult.Error);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _jobQueue.EnqueueCancelamentoAsync(documento.Id, command.Justificativa, cancellationToken);

        return Result.Success(new CancelarDocumentoResponse(documento.Id, documento.Status));
    }
}
```

- [ ] **Step 8: Run handler tests to verify they pass**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~CancelarDocumentoCommandHandlerTests"
```
Expected: All 4 tests pass.

- [ ] **Step 9: Run all tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```
Expected: All tests pass.

- [ ] **Step 10: Commit**

```powershell
git add src/VisuFiscalHub.Application/Common/Interfaces/ICancelamentoJobQueue.cs `
        src/VisuFiscalHub.Application/Common/Models/CancelarDocumentoResponse.cs `
        src/VisuFiscalHub.Application/Documents/Commands/CancelarDocumento/ `
        tests/VisuFiscalHub.Tests/Application/CancelarDocumentoCommandHandlerTests.cs
git commit -m "feat: CancelarDocumentoCommand handler + validator + ICancelamentoJobQueue"
```

---

## Task 10: Infrastructure — CancelamentoJob + HangfireCancelamentoJobQueue + DI

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs`
- Create: `src/VisuFiscalHub.Infrastructure/Jobs/HangfireCancelamentoJobQueue.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`

CancelamentoJob is tested via integration tests in Task 13. Here we focus on correctness of the job structure and DI wiring.

- [ ] **Step 1: Create HangfireCancelamentoJobQueue.cs**

Create `src/VisuFiscalHub.Infrastructure/Jobs/HangfireCancelamentoJobQueue.cs`:

```csharp
using Hangfire;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class HangfireCancelamentoJobQueue : ICancelamentoJobQueue
{
    private readonly IBackgroundJobClient _jobClient;

    public HangfireCancelamentoJobQueue(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public Task EnqueueCancelamentoAsync(DocumentoFiscalId id, string justificativa, CancellationToken ct = default)
    {
        _jobClient.Enqueue<CancelamentoJob>(
            job => job.ExecuteAsync(id, justificativa, CancellationToken.None));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Create CancelamentoJob.cs**

Create `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs`:

```csharp
using System.Diagnostics;
using Hangfire;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Fiscal;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300],
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class CancelamentoJob
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly ITenantCertificateProvider _certProvider;
    private readonly XmlSigner _signer;
    private readonly SefazHttpClient _httpClient;
    private readonly ApplicationDbContext _dbContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CancelamentoJob> _logger;

    public CancelamentoJob(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        ITenantCertificateProvider certProvider,
        XmlSigner signer,
        SefazHttpClient httpClient,
        ApplicationDbContext dbContext,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<CancelamentoJob> logger)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo    = tenantRepo;
        _certProvider  = certProvider;
        _signer        = signer;
        _httpClient    = httpClient;
        _dbContext     = dbContext;
        _unitOfWork    = unitOfWork;
        _timeProvider  = timeProvider;
        _logger        = logger;
    }

    public async Task ExecuteAsync(DocumentoFiscalId documentoId, string justificativa, CancellationToken ct)
    {
        var documento = await _documentoRepo.GetByIdAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("CancelamentoJob: Documento {DocumentoId} não encontrado.", documentoId.Value);
            return;
        }

        if (documento.Status != StatusDocumento.Cancelando)
        {
            _logger.LogInformation(
                "CancelamentoJob: Documento {DocumentoId} em status {Status} — idempotência, ignorado.",
                documentoId.Value, documento.Status);
            return;
        }

        if (documento.Protocolo is null)
        {
            _logger.LogError(
                "CancelamentoJob: Documento {DocumentoId} sem Protocolo — pré-condição violada, abortando sem retry.",
                documentoId.Value);
            return;
        }

        var tenant = await _tenantRepo.GetByIdAsync(documento.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new InvalidOperationException(
                $"Tenant {documento.TenantId.Value} não encontrado ou inativo — Hangfire fará retry.");

        var certResult = await _certProvider.GetCertificateAsync(documento.TenantId, ct);
        if (certResult.IsFailure)
            throw new InvalidOperationException(
                $"Certificado indisponível para Tenant {documento.TenantId.Value}: {certResult.Error.Code}");

        using var certificate = certResult.Value;

        var utcNow  = _timeProvider.GetUtcNow();
        var idLote  = utcNow.ToString("yyyyMMddHHmmss") + (utcNow.Millisecond / 100).ToString();
        var ufCodigo = tenant.ConfiguracaoFiscal.UfCodigo;
        var ambiente = tenant.ConfiguracaoFiscal.Ambiente;

        var xmlEvento = CancelamentoEventoBuilder.ConstruirEvento(
            documento, tenant, justificativa, documento.Protocolo, utcNow, idLote);

        var xmlAssinado = _signer.Assinar(
            xmlEvento, certificate, $"#ID110111{documento.ChaveAcesso.Valor}01");

        var envelope = SoapEnvelopeBuilder.BuildEvento(xmlAssinado.OuterXml, ufCodigo);
        var url      = SefazEndpointResolver.ResolveEvento(ufCodigo, ambiente);

        var sw = Stopwatch.StartNew();
        string? responseCode = null;
        string? responseMessage = null;
        bool success = false;

        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, envelope, certificate, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "CancelamentoJob: Falha HTTP ao cancelar {DocumentoId}. Hangfire fará retry.",
                documentoId.Value);
            await RegistrarAttemptAsync(documentoId, false, null, ex.Message[..Math.Min(ex.Message.Length, 500)],
                sw.ElapsedMilliseconds, ct);
            throw;
        }

        var parseResult = CancelamentoRetornoParser.Parse(soapResponse);
        if (parseResult.IsFailure)
        {
            sw.Stop();
            _logger.LogError(
                "CancelamentoJob: Falha ao parsear resposta SEFAZ para {DocumentoId}: {Error}. Hangfire fará retry.",
                documentoId.Value, parseResult.Error.Code);
            await RegistrarAttemptAsync(documentoId, false, null, parseResult.Error.Code,
                sw.ElapsedMilliseconds, ct);
            throw new InvalidOperationException($"Parse falhou: {parseResult.Error.Code}");
        }

        var retorno = parseResult.Value;
        responseCode = retorno.CStat;

        if (retorno.Aceito)
        {
            var canceladoAt = retorno.DhRegEvento ?? utcNow;
            documento.ConfirmarCancelamento(canceladoAt);
            await _unitOfWork.SaveChangesAsync(ct);
            success = true;
            _logger.LogInformation(
                "CancelamentoJob: Documento {DocumentoId} cancelado (cStat={CStat}).",
                documentoId.Value, retorno.CStat);
        }
        else
        {
            documento.RejeitarCancelamento(retorno.XMotivo);
            await _unitOfWork.SaveChangesAsync(ct);
            responseMessage = retorno.XMotivo;
            _logger.LogWarning(
                "CancelamentoJob: SEFAZ rejeitou cancelamento de {DocumentoId} (cStat={CStat}): {XMotivo}.",
                documentoId.Value, retorno.CStat, retorno.XMotivo);
        }

        sw.Stop();
        await RegistrarAttemptAsync(documentoId, success, responseCode, responseMessage,
            sw.ElapsedMilliseconds, ct);
    }

    private async Task RegistrarAttemptAsync(
        DocumentoFiscalId documentoId,
        bool success,
        string? responseCode,
        string? responseMessage,
        long elapsedMs,
        CancellationToken ct)
    {
        try
        {
            var attempt = DeliveryAttempt.Criar(
                documentoId,
                TipoTentativa.Cancelamento,
                _timeProvider.GetUtcNow(),
                success,
                responseCode,
                responseMessage,
                elapsedMs);

            await _dbContext.DeliveryAttempts.AddAsync(attempt, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CancelamentoJob: Falha ao registrar DeliveryAttempt para {DocumentoId}", documentoId.Value);
        }
    }
}
```

- [ ] **Step 3: Register in DependencyInjection.cs**

Open `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`. After the line `services.AddScoped<ReconciliacaoJobProcessor>();` (line ~119), add:

```csharp
services.AddScoped<CancelamentoJob>();
services.AddScoped<ICancelamentoJobQueue, HangfireCancelamentoJobQueue>();
```

Add the required using at the top if not already present:
```csharp
using VisuFiscalHub.Application.Common.Interfaces;
```

- [ ] **Step 4: Build the whole solution**

```powershell
dotnet build VisuFiscalHub.sln
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs `
        src/VisuFiscalHub.Infrastructure/Jobs/HangfireCancelamentoJobQueue.cs `
        src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
git commit -m "feat: CancelamentoJob Hangfire + HangfireCancelamentoJobQueue + DI"
```

---

## Task 11: Persistence — map `CanceladoAt` and create migration

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs`
- Create: `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/…AddCancelando.cs` (auto-generated)

- [ ] **Step 1: Add `CanceladoAt` mapping to DocumentoFiscalConfiguration.cs**

Open `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs`. After the `AuthorizedAt` mapping (line ~138):

```csharp
builder.Property(d => d.AuthorizedAt)
    .HasColumnName("authorized_at");
```

Add:
```csharp
builder.Property(d => d.CanceladoAt)
    .HasColumnName("cancelado_at");
```

- [ ] **Step 2: Generate migration**

```powershell
dotnet ef migrations add AddCancelando `
  --project src/VisuFiscalHub.Infrastructure `
  --startup-project src/VisuFiscalHub.Api
```
Expected: Migration created in `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 3: Verify the generated migration**

Open the generated migration file. Verify it contains:
- `AddColumn` for `cancelado_at` (nullable `timestamp with time zone`)
- Does NOT drop or alter the `status` column (StatusDocumento enum maps as `int` — no schema change needed for new enum value)

If the migration looks correct, proceed.

- [ ] **Step 4: Build to verify**

```powershell
dotnet build VisuFiscalHub.sln
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs
git add src/VisuFiscalHub.Infrastructure/Persistence/Migrations/
git commit -m "feat: adicionar coluna cancelado_at em documentos_fiscais (migration AddCancelando)"
```

---

## Task 12: API endpoint — replace 501 stub with real handler

**Files:**
- Modify: `src/VisuFiscalHub.Api/Program.cs`

- [ ] **Step 1: Replace the stub in Program.cs**

Open `src/VisuFiscalHub.Api/Program.cs`. Find (line ~495):

```csharp
documentos.MapPost("/{id:guid}/cancelar",
    (Guid id) => TypedResults.StatusCode(StatusCodes.Status501NotImplemented))
    .RequireRateLimiting("api");
```

Replace with:

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

- [ ] **Step 2: Add `CancelarDocumentoRequest` record**

In the same `Program.cs`, near other request records (search for `record IssueDocumentoRequest` or similar), add:

```csharp
public sealed record CancelarDocumentoRequest(string Justificativa);
```

- [ ] **Step 3: Add required usings to Program.cs**

Ensure these are in the using block at the top of Program.cs:
```csharp
using VisuFiscalHub.Application.Documents.Commands.CancelarDocumento;
```

- [ ] **Step 4: Build the whole solution**

```powershell
dotnet build VisuFiscalHub.sln
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Api/Program.cs
git commit -m "feat: endpoint POST /cancelar — substituir 501 stub pelo handler real"
```

---

## Task 13: Integration tests — `CancelamentoTests`

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Integration/CancelamentoTests.cs`

These tests use `IntegrationTestBase` which provides `CriarClienteAppAsync`, `ObterTokenAsync(clientId, clientSecret)`, `CriarTenantAsync`, `CriarClienteAutenticado(token, tenantId)`, `SefazFake`, and `Factory`. Pattern mirrors `DocumentLifecycleTests.cs`.

- [ ] **Step 1: Create CancelamentoTests.cs**

Create `tests/VisuFiscalHub.Tests/Integration/CancelamentoTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class CancelamentoTests : IntegrationTestBase
{
    // Emite um NFC-e, executa NfceProcessingJob diretamente (mesmo padrão de DocumentLifecycleTests),
    // e retorna cliente HTTP + documentoId prontos para o teste de cancelamento.
    private async Task<(HttpClient Http, Guid DocumentoId)> EmitirEAutorizarAsync()
    {
        SefazFake.SimularAutorizado(); // necessário antes de ProcessarAsync
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token    = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http     = CriarClienteAutenticado(token, tenantId.Value);

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
        var token    = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http     = CriarClienteAutenticado(token, tenantId.Value);

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

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var status = await http.GetFromJsonAsync<StatusResponse>($"/api/v1/documentos/{id}/status");
        status!.Status.ShouldBe((int)StatusDocumento.Cancelando);
    }

    [Fact]
    public async Task PostCancelar_DocumentoEnfileirado_Retorna422()
    {
        var (http, id) = await EmitirSemProcessarAsync();
        var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_DocumentoJaCancelando_Retorna422()
    {
        var (http, id) = await EmitirEAutorizarAsync();
        var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };
        await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body); // primeira
        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body); // segunda

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_SemJustificativa_Retorna422()
    {
        var (http, id) = await EmitirEAutorizarAsync();
        var body = new { justificativa = "" };

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_JustificativaMuitoCurta_Retorna422()
    {
        var (http, id) = await EmitirEAutorizarAsync();
        var body = new { justificativa = "abc de fghij n" }; // 14 chars (mínimo = 15)

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_DocumentoDeOutroClienteApp_Retorna403()
    {
        var (_, id) = await EmitirEAutorizarAsync(); // ClienteApp-1
        var (_, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);
        var http2  = CriarClienteAutenticado(token2); // sem tenantId — não importa para cancelamento
        var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

        var response = await http2.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostCancelar_DocumentoNaoEncontrado_Retorna404()
    {
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var http  = CriarClienteAutenticado(token);
        var body  = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{Guid.NewGuid()}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Run integration tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj `
  --filter "FullyQualifiedName~CancelamentoTests"
```
Expected: All 7 tests pass.

- [ ] **Step 4: Run the full test suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj
```
Expected: All tests pass, including all pre-existing tests.

- [ ] **Step 5: Commit**

```powershell
git add tests/VisuFiscalHub.Tests/Integration/CancelamentoTests.cs
git commit -m "test: testes de integração CancelamentoTests (202/422/403/404)"
```

---

## Self-Review

### Spec Coverage Check

| Spec requirement | Covered by task |
|---|---|
| `StatusDocumento.Cancelando = 9` | Task 1 |
| `TipoTentativa.Cancelamento = 4` | Task 1 |
| `IniciarCancelamento` / `ConfirmarCancelamento` / `RejeitarCancelamento` | Task 3 |
| `CanceladoAt` field + domain event | Task 3 |
| Remove `Cancelar()`, remove 4 old tests | Task 3 |
| `NfceProcessingJob` guard includes `Cancelando` | Task 3 |
| `XmlSigner` refactor to `referenceUri` | Task 4 |
| `SefazClient` call site updated | Task 4 |
| `XmlSignerTests` updated (5 tests) | Task 4 |
| `SefazEndpointResolver.ResolveEvento` (27 UFs) | Task 5 |
| `SoapEnvelopeBuilder.BuildEvento` (versaoDados=1.00) | Task 6 |
| `CancelamentoRetornoParser` (cStat 135/155/other) | Task 7 |
| `CancelamentoEventoBuilder` (unsigned XML) | Task 8 |
| No double-signing (builder does NOT call XmlSigner) | Task 8 step 3 |
| `ICancelamentoJobQueue` interface | Task 9 |
| `CancelarDocumentoCommand` + Handler + Validator | Task 9 |
| `CancelarDocumentoResponse` | Task 9 |
| `CancelamentoJob` Hangfire (pipeline 13 steps) | Task 10 |
| `HangfireCancelamentoJobQueue` | Task 10 |
| DI registration | Task 10 |
| `DocumentoFiscalConfiguration` maps `cancelado_at` | Task 11 |
| EF Core migration `AddCancelando` | Task 11 |
| API endpoint replaces 501 stub | Task 12 |
| Integration tests (7 scenarios) | Task 13 |
| Builder helpers `Autorizado()` + `Cancelando()` | Task 2 |

### Type Consistency

- `CancelamentoJob.ExecuteAsync(DocumentoFiscalId, string, CancellationToken)` — matches `HangfireCancelamentoJobQueue.Enqueue` call site ✓
- `CancelamentoRetorno` record — used in `CancelamentoJob` and tests ✓
- `CancelamentoEventoBuilder.ConstruirEvento` parameters match test calls ✓
- `CancelarDocumentoCommandHandler` ctor params match test's `CriarHandler` ✓
- `DocumentoFiscalBuilder.Autorizado(DateTimeOffset)` matches domain test calls ✓

### No Placeholder Check

All code blocks contain complete implementation. No TBD, TODO, or "similar to" references.
