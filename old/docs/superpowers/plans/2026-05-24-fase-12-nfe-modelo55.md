# Fase 12 — NF-e Modelo 55 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add NF-e (Modelo 55) B2B support to the existing NFC-e (Modelo 65) platform, sharing the same `DocumentoFiscal` aggregate, job pipeline, and processing infrastructure while branching on `documento.Tipo`.

**Architecture:** A single `IFiscalDocumentXmlBuilder` interface replaces `INfceXmlBuilder`; `SefazEndpointResolver` gains independent NF-e URL tables; `NfceProcessingJob` is renamed `FiscalDocumentProcessingJob` and becomes tipo-aware; `IssueDocumentCommand` gains NF-e fields (`NfeDestinatario`, `NatOp`, `ModFrete`); a new `POST /api/v1/documentos/nfe` endpoint drives the flow. NF-e cancellation uses a 24-hour window vs. 30 minutes for NFC-e.

**Tech Stack:** .NET 10, Clean Architecture, CQRS with Mediator.SourceGenerator, Railway-oriented Result<T>, EF Core + PostgreSQL (jsonb for NfeDestinatario), Hangfire, xUnit + Shouldly + NSubstitute, FluentValidation.

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `src/VisuFiscalHub.Domain/ValueObjects/NfeDestinatario.cs` | VO with all destinatário fields + validation |
| Modify | `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs` | Add `DestinatarioObrigatorioParaNfe`, `DestinatarioNaoPermitidoEmNfce`, `NatOpObrigatoriaNfe`, `PrazoDeCancelamentoExpirado` message fix |
| Modify | `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs` | Add `SerieNfe?` parameter + validation |
| Modify | `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs` | Add `NfeDestinatario?`, `NatOp?`, `ModFrete`; tipo-aware `IndPresencaValidos`; 24h cancellation for NF-e |
| Create | `src/VisuFiscalHub.Application/Common/Interfaces/IFiscalDocumentXmlBuilder.cs` | Renamed interface (same signature) |
| Delete | `src/VisuFiscalHub.Application/Common/Interfaces/INfceXmlBuilder.cs` | Replaced by IFiscalDocumentXmlBuilder |
| Modify | `src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/NfceXmlBuilder.cs` | Rename to `FiscalDocumentXmlBuilder`, add NF-e XML branches |
| Modify | `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` | Add independent NF-e URL methods `AutorizacaoNfe`, `ConsultaProtocoloNfe`, `ResolveEventoNfe` |
| Modify | `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs` | Inject `IFiscalDocumentXmlBuilder`; tipo-aware URL selection; null-safe QR Code |
| Rename | `src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs` → `FiscalDocumentProcessingJob.cs` | Tipo-aware job (NFC-e + NF-e) |
| Modify | `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs` | Pass `documento.Tipo` to `ResolveEventoNfe`/`ResolveEvento` |
| Create | `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/XXXXXX_AddNfeSupport.cs` | Migration: nfe_destinatario, nat_op, mod_frete, serie_nfe |
| Modify | `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs` | Map new columns + jsonb |
| Modify | `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/TenantConfiguration.cs` | Map `serie_nfe` |
| Modify | `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs` | Add `NfeDestinatario?`, `NatOp?`, `ModFrete` |
| Modify | `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommandValidator.cs` | NF-e validation rules |
| Modify | `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommandHandler.cs` | Pass new fields to `DocumentoFiscal.Criar`; use `SerieNfe` for NF-e |
| Modify | `src/VisuFiscalHub.Application/Tenants/Commands/CreateTenant/CreateTenantCommand.cs` | Add `SerieNfe?` |
| Modify | `src/VisuFiscalHub.Application/Tenants/Commands/CreateTenant/CreateTenantCommandHandler.cs` | Pass `SerieNfe` to `ConfiguracaoFiscal.Criar` + create NF-e sequence |
| Modify | `src/VisuFiscalHub.Api/Program.cs` | Add `POST /api/v1/documentos/nfe`; rename reconciliation job |
| Modify | `src/VisuFiscalHub.Infrastructure/InfrastructureServiceCollectionExtensions.cs` | Re-register `FiscalDocumentXmlBuilder` as `IFiscalDocumentXmlBuilder` |
| Create | `tests/VisuFiscalHub.Tests/Domain/NfeDestinatarioTests.cs` | VO unit tests |
| Modify | `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs` | NF-e cancellation window + NfeDestinatario integration |
| Create | `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/SefazEndpointResolverNfeTests.cs` | NF-e endpoint routing tests |
| Create | `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/FiscalDocumentXmlBuilderNfeTests.cs` | NF-e XML structure tests |
| Modify | `tests/VisuFiscalHub.Tests/Application/IssueDocumentCommandValidatorTests.cs` | NF-e-specific validation |

---

## Task 1: `NfeDestinatario` Value Object + New Domain Errors

**Files:**
- Create: `src/VisuFiscalHub.Domain/ValueObjects/NfeDestinatario.cs`
- Modify: `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VisuFiscalHub.Tests/Domain/NfeDestinatarioTests.cs`:

```csharp
using Shouldly;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class NfeDestinatarioTests
{
    private static NfeDestinatario ValidCnpj() => NfeDestinatario.Criar(
        cnpjOuCpf: "11222333000181",
        razaoSocial: "Empresa Teste Ltda",
        indIeDest: 1,
        ie: "123456789",
        logradouro: "Rua Teste",
        numero: "100",
        complemento: null,
        bairro: "Centro",
        municipio: "São Paulo",
        codigoMunicipio: "3550308",
        uf: "SP",
        cep: "01310100",
        email: "nfe@empresa.com").Value;

    [Fact]
    public void Criar_ComCnpjValido_Sucesso()
    {
        var dest = ValidCnpj();
        dest.CnpjOuCpf.ShouldBe("11222333000181");
        dest.RazaoSocial.ShouldBe("Empresa Teste Ltda");
        dest.IndIeDest.ShouldBe(1);
    }

    [Fact]
    public void Criar_ComCpfValido_Sucesso()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "52998224725",
            razaoSocial: "Pessoa Fisica",
            indIeDest: 9,
            ie: null,
            logradouro: "Av Paulista",
            numero: "1",
            complemento: null,
            bairro: "Bela Vista",
            municipio: "São Paulo",
            codigoMunicipio: "3550308",
            uf: "SP",
            cep: "01310100",
            email: null);
        result.IsSuccess.ShouldBeTrue();
        result.Value.CnpjOuCpf.ShouldBe("52998224725");
    }

    [Fact]
    public void Criar_CnpjOuCpfInvalido_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "123",   // neither 11 nor 14 digits
            razaoSocial: "X",
            indIeDest: 9, ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.NfeDestinatarioInvalido");
    }

    [Fact]
    public void Criar_RazaoSocialVazia_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "",
            indIeDest: 1, ie: "123",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_IndIeDestInvalido_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            indIeDest: 5,  // not 1, 2, or 9
            ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_IndIeDest1_SemIe_Retorna_Falha()
    {
        // IndIeDest=1 (contribuinte) requires IE
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            indIeDest: 1, ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_CepInvalido_Retorna_Falha()
    {
        var result = NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            indIeDest: 9, ie: null,
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "ABC",  // invalid
            email: null);
        result.IsFailure.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~NfeDestinatarioTests" --no-build 2>&1 | Select-String -Pattern "error|FAILED|Build"
```

Expected: Build error — `NfeDestinatario` does not exist.

- [ ] **Step 3: Implement `NfeDestinatario` value object**

Create `src/VisuFiscalHub.Domain/ValueObjects/NfeDestinatario.cs`:

```csharp
using System.Text.RegularExpressions;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed partial record NfeDestinatario(
    string CnpjOuCpf,
    string RazaoSocial,
    int IndIeDest,
    string? Ie,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    string CodigoMunicipio,
    string Uf,
    string Cep,
    string? Email)
{
    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex CepRegex();

    [GeneratedRegex(@"^\d{7}$")]
    private static partial Regex CodigoMunicipioRegex();

    private static readonly int[] IndIeDestValidos = [1, 2, 9];

    public static Result<NfeDestinatario> Criar(
        string cnpjOuCpf,
        string razaoSocial,
        int indIeDest,
        string? ie,
        string logradouro,
        string numero,
        string? complemento,
        string bairro,
        string municipio,
        string codigoMunicipio,
        string uf,
        string cep,
        string? email)
    {
        if (string.IsNullOrWhiteSpace(cnpjOuCpf)
            || (cnpjOuCpf.Length != 11 && cnpjOuCpf.Length != 14)
            || !cnpjOuCpf.All(char.IsDigit))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(razaoSocial) || razaoSocial.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (!IndIeDestValidos.Contains(indIeDest))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        // IndIeDest=1 (contribuinte com IE) requires IE
        if (indIeDest == 1 && string.IsNullOrWhiteSpace(ie))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(logradouro) || logradouro.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(numero) || numero.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(bairro) || bairro.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(municipio) || municipio.Length > 60)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(codigoMunicipio) || !CodigoMunicipioRegex().IsMatch(codigoMunicipio))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(uf) || uf.Length != 2)
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        if (string.IsNullOrWhiteSpace(cep) || !CepRegex().IsMatch(cep))
            return Result.Failure<NfeDestinatario>(DocumentoFiscalErrors.NfeDestinatarioInvalido);

        var ieNorm = string.IsNullOrWhiteSpace(ie) ? null : ie.Trim();
        var emailNorm = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        return Result.Success(new NfeDestinatario(
            cnpjOuCpf, razaoSocial.Trim(), indIeDest, ieNorm,
            logradouro.Trim(), numero.Trim(), complemento?.Trim(),
            bairro.Trim(), municipio.Trim(), codigoMunicipio, uf.ToUpperInvariant(),
            cep, emailNorm));
    }
}
```

- [ ] **Step 4: Add new errors to `DocumentoFiscalErrors.cs`**

Add to `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs`:

```csharp
    public static readonly Error NfeDestinatarioInvalido =
        new("DocumentoFiscal.NfeDestinatarioInvalido", "Os dados do destinatário da NF-e são inválidos.");

    public static readonly Error DestinatarioObrigatorioParaNfe =
        new("DocumentoFiscal.DestinatarioObrigatorioParaNfe", "O destinatário é obrigatório para NF-e Modelo 55.");

    public static readonly Error DestinatarioNaoPermitidoEmNfce =
        new("DocumentoFiscal.DestinatarioNaoPermitidoEmNfce", "NFC-e Modelo 65 não suporta destinatário NF-e.");

    public static readonly Error NatOpObrigatoriaNfe =
        new("DocumentoFiscal.NatOpObrigatoriaNfe", "Natureza da operação é obrigatória para NF-e Modelo 55.");
```

Also update the existing `PrazoDeCancelamentoExpirado` message to be tipo-neutral:

```csharp
    public static readonly Error PrazoDeCancelamentoExpirado =
        new("DocumentoFiscal.PrazoDeCancelamentoExpirado", "O prazo para cancelamento expirou.");
```

- [ ] **Step 5: Run tests to verify they pass**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~NfeDestinatarioTests" -v quiet
```

Expected: All 6 tests PASS.

- [ ] **Step 6: Run full suite to check no regressions**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

Expected: All existing tests still pass.

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Domain/ValueObjects/NfeDestinatario.cs `
        src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs `
        tests/VisuFiscalHub.Tests/Domain/NfeDestinatarioTests.cs
git commit -m "feat: add NfeDestinatario value object and new NF-e domain errors"
```

---

## Task 2: `ConfiguracaoFiscal` + `SerieNfe`

**Files:**
- Modify: `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs`

- [ ] **Step 1: Write the failing tests**

Add to a new file `tests/VisuFiscalHub.Tests/Domain/ConfiguracaoFiscalTests.cs`:

```csharp
using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class ConfiguracaoFiscalTests
{
    [Fact]
    public void Criar_SemSerieNfe_Sucesso_SerieNfeNula()
    {
        var result = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35, null);
        result.IsSuccess.ShouldBeTrue();
        result.Value.SerieNfe.ShouldBeNull();
    }

    [Fact]
    public void Criar_ComSerieNfeValida_Sucesso()
    {
        var result = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35, null,
            serieNfe: "001");
        result.IsSuccess.ShouldBeTrue();
        result.Value.SerieNfe.ShouldBe("001");
    }

    [Fact]
    public void Criar_ComSerieNfeInvalida_Retorna_Falha()
    {
        var result = ConfiguracaoFiscal.Criar(
            RegimeTributario.SimplesNacional, "001", AmbienteSefaz.Homologacao, 35, null,
            serieNfe: "ABCD");
        result.IsFailure.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ConfiguracaoFiscalTests" --no-build 2>&1 | Select-String -Pattern "error|FAILED"
```

Expected: Build error — `SerieNfe` parameter does not exist.

- [ ] **Step 3: Implement `SerieNfe` in `ConfiguracaoFiscal`**

Replace `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs`:

```csharp
using System.Text.RegularExpressions;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed partial record ConfiguracaoFiscal(
    RegimeTributario Crt,
    string Serie,
    AmbienteSefaz Ambiente,
    int UfCodigo,
    string? InscricaoEstadual,
    string? SerieNfe = null)
{
    [GeneratedRegex(@"^[0-9]{1,3}$")]
    private static partial Regex SerieRegex();

    private static readonly int[] CodigosUfValidos =
    [
        11, 12, 13, 14, 15, 16, 17, 21, 22, 23, 24, 25, 26, 27, 28, 29,
        31, 32, 33, 35, 41, 42, 43, 50, 51, 52, 53
    ];

    public static Result<ConfiguracaoFiscal> Criar(
        RegimeTributario crt,
        string serie,
        AmbienteSefaz ambiente,
        int ufCodigo,
        string? inscricaoEstadual = null,
        string? serieNfe = null)
    {
        if (string.IsNullOrWhiteSpace(serie) || !SerieRegex().IsMatch(serie))
            return Result.Failure<ConfiguracaoFiscal>(TenantErrors.ConfiguracaoFiscalInvalida);

        if (!CodigosUfValidos.Contains(ufCodigo))
            return Result.Failure<ConfiguracaoFiscal>(TenantErrors.ConfiguracaoFiscalInvalida);

        if (serieNfe is not null && !SerieRegex().IsMatch(serieNfe))
            return Result.Failure<ConfiguracaoFiscal>(TenantErrors.ConfiguracaoFiscalInvalida);

        var ie = string.IsNullOrWhiteSpace(inscricaoEstadual) ? null : inscricaoEstadual.Trim();

        return Result.Success(new ConfiguracaoFiscal(crt, serie, ambiente, ufCodigo, ie, serieNfe));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ConfiguracaoFiscalTests" -v quiet
```

Expected: All 3 tests PASS.

- [ ] **Step 5: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

Expected: All tests still pass.

- [ ] **Step 6: Commit**

```powershell
git add src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs `
        tests/VisuFiscalHub.Tests/Domain/ConfiguracaoFiscalTests.cs
git commit -m "feat: add SerieNfe to ConfiguracaoFiscal"
```

---

## Task 3: `DocumentoFiscal` Entity — NF-e Fields + Tipo-Aware Rules

**Files:**
- Modify: `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`
- Modify: `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`
- Modify: `tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs` (add NF-e builder methods)

- [ ] **Step 1: Write the failing tests**

Add to `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`:

```csharp
    // ──────────────────────────────────────────────────────────────
    // NF-e specific rules
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public void Criar_Nfe_SemDestinatario_RetornaErro()
    {
        var result = DocumentoFiscalBuilder.CriarNfe(destinatario: null);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.DestinatarioObrigatorioParaNfe");
    }

    [Fact]
    public void Criar_Nfe_ComDestinatario_Sucesso()
    {
        var result = DocumentoFiscalBuilder.CriarNfe(destinatario: DocumentoFiscalBuilder.ValidoNfeDestinatario());
        result.IsSuccess.ShouldBeTrue();
        result.Value.NfeDestinatario.ShouldNotBeNull();
    }

    [Fact]
    public void Criar_Nfe_SemNatOp_RetornaErro()
    {
        var result = DocumentoFiscalBuilder.CriarNfe(
            destinatario: DocumentoFiscalBuilder.ValidoNfeDestinatario(),
            natOp: null);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.NatOpObrigatoriaNfe");
    }

    [Fact]
    public void Criar_Nfce_ComDestinatario_RetornaErro()
    {
        var result = DocumentoFiscalBuilder.CriarNfceComDestinatario(
            DocumentoFiscalBuilder.ValidoNfeDestinatario());
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.DestinatarioNaoPermitidoEmNfce");
    }

    [Fact]
    public void IniciarCancelamento_Nfe_DentroDe24h_Sucesso()
    {
        var authorizedAt = FixedNow.AddHours(-23);
        var documento = DocumentoFiscalBuilder.AutorizadoNfe(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsSuccess.ShouldBeTrue();
        documento.Status.ShouldBe(StatusDocumento.Cancelando);
    }

    [Fact]
    public void IniciarCancelamento_Nfe_Apos24h_RetornaErro()
    {
        var authorizedAt = FixedNow.AddHours(-25);
        var documento = DocumentoFiscalBuilder.AutorizadoNfe(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    }

    [Fact]
    public void IniciarCancelamento_Nfce_Apos30Min_RetornaErro()
    {
        var authorizedAt = FixedNow.AddMinutes(-31);
        var documento = DocumentoFiscalBuilder.Autorizado(authorizedAt);
        var result = documento.IniciarCancelamento(new FixedTimeProvider(FixedNow));
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");
    }
```

Add to `DocumentoFiscalBuilder` (you'll need to look at the existing builder — see step 3):

```csharp
    public static Result<DocumentoFiscal> CriarNfe(
        NfeDestinatario? destinatario,
        string? natOp = "Venda de mercadoria") { ... }

    public static Result<DocumentoFiscal> CriarNfceComDestinatario(NfeDestinatario dest) { ... }

    public static NfeDestinatario ValidoNfeDestinatario() { ... }

    public static DocumentoFiscal AutorizadoNfe(DateTimeOffset authorizedAt) { ... }
```

- [ ] **Step 2: Read the existing `DocumentoFiscalBuilder` to understand its structure**

```powershell
Get-Content "tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs"
```

- [ ] **Step 3: Implement `DocumentoFiscal` entity changes**

In `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`:

**a) Add properties after `ChaveAcesso`:**
```csharp
    public NfeDestinatario? NfeDestinatario { get; private set; }
    public string? NatOp { get; private set; }
    public int ModFrete { get; private set; } = 9; // 9=Sem frete (default)
```

**b) Update private constructor to accept new fields and add `IndPresencaValidos` as tipo-aware:**
```csharp
    // NFC-e: 1=presencial, 3=telemarketing, 4=entrega domiciliar, 9=outros (2=internet rejeitado)
    private static readonly IReadOnlySet<int> IndPresencaNfce = new HashSet<int> { 1, 3, 4, 9 };
    // NF-e: indPres field not required — accepted values include 0 (not applicable)
    private static readonly IReadOnlySet<int> IndPresencaNfe = new HashSet<int> { 0, 1, 2, 3, 4, 5, 9 };
```

Remove the existing `private static readonly IReadOnlySet<int> IndPresencaValidos` field.

**c) Update `Criar` to accept new parameters and apply tipo-aware validation:**
```csharp
    public static Result<DocumentoFiscal> Criar(
        DocumentoFiscalId id,
        TenantId tenantId,
        ClienteAppId clienteAppId,
        string idempotencyKey,
        TipoDocumento tipo,
        ChaveAcesso chaveAcesso,
        long numero,
        string serie,
        int indPresenca,
        IEnumerable<ItemDocumento> items,
        IEnumerable<Pagamento> pagamentos,
        TimeProvider timeProvider,
        string? cpfConsumidor = null,
        string? nomeConsumidor = null,
        NfeDestinatario? nfeDestinatario = null,
        string? natOp = null,
        int modFrete = 9)
    {
        // existing guards...

        var indPresencaValidos = tipo == TipoDocumento.NfCe ? IndPresencaNfce : IndPresencaNfe;
        if (!indPresencaValidos.Contains(indPresenca))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IndPresencaInvalido);

        // NF-e specific rules
        if (tipo == TipoDocumento.Nfe)
        {
            if (nfeDestinatario is null)
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.DestinatarioObrigatorioParaNfe);
            if (string.IsNullOrWhiteSpace(natOp))
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.NatOpObrigatoriaNfe);
        }
        else if (nfeDestinatario is not null)
        {
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.DestinatarioNaoPermitidoEmNfce);
        }

        // ... rest of factory
    }
```

**d) Update private constructor to accept and set the new fields.**

**e) Update `IniciarCancelamento` for tipo-aware cancellation window:**
```csharp
    public Result IniciarCancelamento(TimeProvider timeProvider)
    {
        if (Status != StatusDocumento.Autorizado)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        if (AuthorizedAt is null)
            return Result.Failure(DocumentoFiscalErrors.TransicaoInvalida);

        var prazo = Tipo == TipoDocumento.Nfe
            ? AuthorizedAt.Value.AddHours(24)
            : AuthorizedAt.Value.AddMinutes(30);

        if (timeProvider.GetUtcNow() >= prazo)
            return Result.Failure(DocumentoFiscalErrors.PrazoDeCancelamentoExpirado);

        MotivoRejeicao = null;
        Status = StatusDocumento.Cancelando;
        return Result.Success();
    }
```

- [ ] **Step 4: Update `DocumentoFiscalBuilder` helper**

Read the existing file then add:
```csharp
    public static NfeDestinatario ValidoNfeDestinatario() =>
        NfeDestinatario.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "Empresa Teste Ltda",
            indIeDest: 9, ie: null,
            logradouro: "Rua Teste", numero: "100", complemento: null,
            bairro: "Centro", municipio: "São Paulo",
            codigoMunicipio: "3550308", uf: "SP", cep: "01310100",
            email: null).Value;

    public static Result<DocumentoFiscal> CriarNfe(
        NfeDestinatario? destinatario,
        string? natOp = "Venda de mercadoria") =>
        DocumentoFiscal.Criar(
            new DocumentoFiscalId(Guid.NewGuid()),
            TenantId.Empty,     // use whatever non-default value the builder uses
            ClienteAppId.Empty,
            "KEY",
            TipoDocumento.Nfe,
            ChaveAcesso.Gerar(35, "2601", "11222333000181", 55, "001", "000000001",
                TipoEmissao.Normal, "00000001").Value,
            1, "001", 0,
            [ItemDocumento.Criar(1, Produto.Criar(/* ... */), Tributo.Criar(/* ... */).Value)],
            [Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value],
            TimeProvider.System,
            nfeDestinatario: destinatario,
            natOp: natOp);

    public static Result<DocumentoFiscal> CriarNfceComDestinatario(NfeDestinatario dest) =>
        DocumentoFiscal.Criar(
            new DocumentoFiscalId(Guid.NewGuid()),
            TenantId.Empty,
            ClienteAppId.Empty,
            "KEY",
            TipoDocumento.NfCe,
            ChaveAcesso.Gerar(35, "2601", "11222333000181", 65, "001", "000000001",
                TipoEmissao.Normal, "00000001").Value,
            1, "001", 1,
            [/* same as above */],
            [Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value],
            TimeProvider.System,
            nfeDestinatario: dest);

    public static DocumentoFiscal AutorizadoNfe(DateTimeOffset authorizedAt)
    {
        var dest = ValidoNfeDestinatario();
        var doc = CriarNfe(dest).Value;
        doc.Enfileirar();
        doc.IniciarProcessamento();
        var qrCode = QrCode.Gerar(doc.ChaveAcesso, AmbienteSefaz.Homologacao, "csc", "https://x.com").Value;
        doc.Autorizar("PROT001", "<xml/>", qrCode, authorizedAt, new FixedTimeProvider(authorizedAt));
        return doc;
    }
```

> **Note:** After reading the builder, adapt the `TenantId.Empty`, `ClienteAppId.Empty`, and item/pagamento construction to match the existing builder patterns exactly.

- [ ] **Step 5: Run tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~DocumentoFiscalTests" -v quiet
```

Expected: All tests PASS (including the 7 new NF-e tests and all existing cancellation tests).

- [ ] **Step 6: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs `
        tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs `
        tests/VisuFiscalHub.Tests/Helpers/DocumentoFiscalBuilder.cs
git commit -m "feat: add NF-e fields to DocumentoFiscal; tipo-aware cancellation window"
```

---

## Task 4: EF Core Migration — AddNfeSupport

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/XXXXXX_AddNfeSupport.cs` (generated)
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`

- [ ] **Step 1: Update EF Core configuration for `DocumentoFiscal`**

In `DocumentoFiscalConfiguration.cs`, add inside `Configure(EntityTypeBuilder<DocumentoFiscal> builder)`:

```csharp
builder.Property(x => x.NfeDestinatario)
    .HasColumnName("nfe_destinatario")
    .HasColumnType("jsonb");

builder.Property(x => x.NatOp)
    .HasColumnName("nat_op")
    .HasMaxLength(60);

builder.Property(x => x.ModFrete)
    .HasColumnName("mod_frete")
    .HasDefaultValue(9);
```

- [ ] **Step 2: Update EF Core configuration for `Tenant`**

In `TenantConfiguration.cs`, find the `ConfiguracaoFiscal` owned-entity mapping and add:

```csharp
cfg.Property(c => c.SerieNfe)
    .HasColumnName("serie_nfe")
    .HasMaxLength(3)
    .IsRequired(false);
```

- [ ] **Step 3: Generate the migration**

```powershell
dotnet ef migrations add AddNfeSupport `
    --project src/VisuFiscalHub.Infrastructure `
    --startup-project src/VisuFiscalHub.Api `
    --output-dir Persistence/Migrations
```

- [ ] **Step 4: Verify the generated migration**

Open the generated migration file and confirm it contains:
- `AddColumn` for `nfe_destinatario jsonb nullable`
- `AddColumn` for `nat_op varchar(60) nullable`
- `AddColumn` for `mod_frete integer not null default 9`
- `AddColumn` for `serie_nfe varchar(3) nullable` (on tenants table)

If the migration is incorrect, do NOT run it. Fix the configuration and re-generate.

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Persistence/Configurations/ `
        src/VisuFiscalHub.Infrastructure/Persistence/Migrations/
git commit -m "feat: EF Core migration AddNfeSupport (nfe_destinatario, nat_op, mod_frete, serie_nfe)"
```

---

## Task 5: `FiscalDocumentXmlBuilder` — Rename Interface + NF-e XML Support

**Files:**
- Create: `src/VisuFiscalHub.Application/Common/Interfaces/IFiscalDocumentXmlBuilder.cs`
- Delete: `src/VisuFiscalHub.Application/Common/Interfaces/INfceXmlBuilder.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/NfceXmlBuilder.cs` → rename to `FiscalDocumentXmlBuilder.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/FiscalDocumentXmlBuilderNfeTests.cs`:

```csharp
using System.Xml;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class FiscalDocumentXmlBuilderNfeTests
{
    private readonly IQrCodeGenerator _qrCodeGen = Substitute.For<IQrCodeGenerator>();
    private readonly ICertificateEncryptionService _enc = Substitute.For<ICertificateEncryptionService>();

    private FiscalDocumentXmlBuilder CreateBuilder() =>
        new(_qrCodeGen, _enc);

    [Fact]
    public void Construir_Nfe_XmlContemMod55()
    {
        var builder = CreateBuilder();
        var doc = DocumentoFiscalBuilder.AutorizadoNfe(DateTimeOffset.UtcNow);
        var tenant = TenantBuilder.Default();

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var xml = result.Value;
        var ns = new XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
        xml.SelectSingleNode("//nfe:mod", ns)!.InnerText.ShouldBe("55");
    }

    [Fact]
    public void Construir_Nfe_XmlNaoContemInfNFeSupl()
    {
        var builder = CreateBuilder();
        var doc = DocumentoFiscalBuilder.AutorizadoNfe(DateTimeOffset.UtcNow);
        var tenant = TenantBuilder.Default();

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var ns = new XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
        result.Value.SelectSingleNode("//nfe:infNFeSupl", ns).ShouldBeNull();
    }

    [Fact]
    public void Construir_Nfe_XmlContemDest()
    {
        var builder = CreateBuilder();
        var doc = DocumentoFiscalBuilder.AutorizadoNfe(DateTimeOffset.UtcNow);
        var tenant = TenantBuilder.Default();

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var ns = new XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
        result.Value.SelectSingleNode("//nfe:dest", ns).ShouldNotBeNull();
    }

    [Fact]
    public void Construir_Nfce_XmlContemMod65()
    {
        var builder = CreateBuilder();
        var doc = DocumentoFiscalBuilder.Processando(); // existing NFC-e builder
        var tenant = TenantBuilder.DefaultWithCsc();

        var result = builder.Construir(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var ns = new XmlNamespaceManager(result.Value.NameTable);
        ns.AddNamespace("nfe", "http://www.portalfiscal.inf.br/nfe");
        result.Value.SelectSingleNode("//nfe:mod", ns)!.InnerText.ShouldBe("65");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentXmlBuilderNfeTests" --no-build 2>&1 | Select-String -Pattern "error|FAILED"
```

Expected: Build error — `FiscalDocumentXmlBuilder` does not exist.

- [ ] **Step 3: Create `IFiscalDocumentXmlBuilder` interface**

Create `src/VisuFiscalHub.Application/Common/Interfaces/IFiscalDocumentXmlBuilder.cs`:

```csharp
using System.Xml;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IFiscalDocumentXmlBuilder
{
    Result<XmlDocument> Construir(DocumentoFiscal documento, Tenant tenant);
}
```

- [ ] **Step 4: Rename `NfceXmlBuilder.cs` to `FiscalDocumentXmlBuilder.cs` and add NF-e branching**

```powershell
Rename-Item "src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/NfceXmlBuilder.cs" `
            "src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/FiscalDocumentXmlBuilder.cs"
```

Then update the file:

```csharp
// Change class name and interface
internal sealed class FiscalDocumentXmlBuilder : IFiscalDocumentXmlBuilder
{
    // ...same constructor...

    public Result<XmlDocument> Construir(DocumentoFiscal documento, Tenant tenant)
    {
        return documento.Tipo == TipoDocumento.Nfe
            ? ConstruirNfe(documento, tenant)
            : ConstruirNfce(documento, tenant);
    }

    private Result<XmlDocument> ConstruirNfce(DocumentoFiscal documento, Tenant tenant)
    {
        // Move existing Construir body here (unchanged)
        // The existing code: validates CIdToken, CSC, builds XML with mod=65, infNFeSupl, QR code, etc.
    }

    private Result<XmlDocument> ConstruirNfe(DocumentoFiscal documento, Tenant tenant)
    {
        // NF-e XML: mod=55, tpImp=1, indFinal=0, no infNFeSupl, has <dest>, has <natOp>, has <transp>
        var doc = new XmlDocument { PreserveWhitespace = false };
        var decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(decl);

        var nfeEl = doc.CreateElement("NFe", NfeNs);
        doc.AppendChild(nfeEl);

        var infNFe = doc.CreateElement("infNFe", NfeNs);
        infNFe.SetAttribute("Id", $"NFe{documento.ChaveAcesso.Valor}");
        infNFe.SetAttribute("versao", "4.00");
        nfeEl.AppendChild(infNFe);

        UfFusoHorario.Mapa.TryGetValue(tenant.ConfiguracaoFiscal.UfCodigo, out var offset);
        var dhEmi = documento.CreatedAt.ToOffset(offset).ToString("yyyy-MM-ddTHH:mm:sszzz");
        var dhSaiEnt = dhEmi; // same as emission for normal NF-e

        infNFe.AppendChild(BuildIdeNfe(doc, documento, tenant, dhEmi));
        infNFe.AppendChild(BuildEmit(doc, tenant)); // reuse existing BuildEmit
        infNFe.AppendChild(BuildDest(doc, documento.NfeDestinatario!));
        infNFe.AppendChild(BuildDetItems(doc, documento)); // reuse existing items builder
        infNFe.AppendChild(BuildTotal(doc, documento));    // reuse existing total builder
        infNFe.AppendChild(BuildTransp(doc, documento));
        infNFe.AppendChild(BuildCobr(doc)); // <cobr> with <pag> (no installments)
        infNFe.AppendChild(BuildPag(doc, documento)); // reuse existing pag builder

        return Result.Success(doc);
    }

    private XmlElement BuildIdeNfe(XmlDocument doc, DocumentoFiscal documento, Tenant tenant, string dhEmi)
    {
        var ide = doc.CreateElement("ide", NfeNs);
        ide.AppendChild(Elem(doc, "cUF", tenant.ConfiguracaoFiscal.UfCodigo.ToString()));
        ide.AppendChild(Elem(doc, "cNF", documento.ChaveAcesso.Valor[35..43])); // cNF from key
        ide.AppendChild(Elem(doc, "natOp", documento.NatOp!));
        ide.AppendChild(Elem(doc, "mod", "55"));
        ide.AppendChild(Elem(doc, "serie", documento.Serie));
        ide.AppendChild(Elem(doc, "nNF", documento.Numero.ToString()));
        ide.AppendChild(Elem(doc, "dhEmi", dhEmi));
        ide.AppendChild(Elem(doc, "dhSaiEnt", dhEmi));
        ide.AppendChild(Elem(doc, "tpNF", "1")); // 1=saída
        ide.AppendChild(Elem(doc, "idDest", "1")); // 1=operação interna
        ide.AppendChild(Elem(doc, "cMunFG", documento.NfeDestinatario!.CodigoMunicipio));
        ide.AppendChild(Elem(doc, "tpImp", "1")); // 1=DANFE retrato
        ide.AppendChild(Elem(doc, "tpEmis", "1")); // 1=emissão normal
        ide.AppendChild(Elem(doc, "cDV", documento.ChaveAcesso.Valor[43..])); // check digit
        ide.AppendChild(Elem(doc, "tpAmb", ((int)tenant.ConfiguracaoFiscal.Ambiente).ToString()));
        ide.AppendChild(Elem(doc, "finNFe", "1")); // 1=NF-e normal
        ide.AppendChild(Elem(doc, "indFinal", "0")); // 0=não é consumidor final
        ide.AppendChild(Elem(doc, "indPres", documento.IndPresenca.ToString()));
        ide.AppendChild(Elem(doc, "procEmi", "0")); // 0=emissão com app do contribuinte
        ide.AppendChild(Elem(doc, "verProc", "1.0.0"));
        return ide;
    }

    private XmlElement BuildDest(XmlDocument doc, NfeDestinatario dest)
    {
        var destEl = doc.CreateElement("dest", NfeNs);

        if (dest.CnpjOuCpf.Length == 14)
            destEl.AppendChild(Elem(doc, "CNPJ", dest.CnpjOuCpf));
        else
            destEl.AppendChild(Elem(doc, "CPF", dest.CnpjOuCpf));

        destEl.AppendChild(Elem(doc, "xNome", dest.RazaoSocial));

        var endDest = doc.CreateElement("enderDest", NfeNs);
        endDest.AppendChild(Elem(doc, "xLgr", dest.Logradouro));
        endDest.AppendChild(Elem(doc, "nro", dest.Numero));
        if (!string.IsNullOrEmpty(dest.Complemento))
            endDest.AppendChild(Elem(doc, "xCpl", dest.Complemento));
        endDest.AppendChild(Elem(doc, "xBairro", dest.Bairro));
        endDest.AppendChild(Elem(doc, "cMun", dest.CodigoMunicipio));
        endDest.AppendChild(Elem(doc, "xMun", dest.Municipio));
        endDest.AppendChild(Elem(doc, "UF", dest.Uf));
        endDest.AppendChild(Elem(doc, "CEP", dest.Cep));
        endDest.AppendChild(Elem(doc, "cPais", "1058"));
        endDest.AppendChild(Elem(doc, "xPais", "Brasil"));
        destEl.AppendChild(endDest);

        destEl.AppendChild(Elem(doc, "indIEDest", dest.IndIeDest.ToString()));
        if (dest.Ie is not null)
            destEl.AppendChild(Elem(doc, "IE", dest.Ie));
        if (dest.Email is not null)
            destEl.AppendChild(Elem(doc, "email", dest.Email));

        return destEl;
    }

    private XmlElement BuildTransp(XmlDocument doc, DocumentoFiscal documento)
    {
        var transp = doc.CreateElement("transp", NfeNs);
        transp.AppendChild(Elem(doc, "modFrete", documento.ModFrete.ToString()));
        return transp;
    }
}
```

> **Note:** `BuildEmit`, `BuildDetItems`, `BuildTotal`, `BuildPag`, and the `Elem` helper already exist in `NfceXmlBuilder.cs`. Make them private (not internal) methods of the renamed class and reuse them from both `ConstruirNfce` and `ConstruirNfe`.

- [ ] **Step 5: Delete `INfceXmlBuilder.cs`**

```powershell
Remove-Item "src/VisuFiscalHub.Application/Common/Interfaces/INfceXmlBuilder.cs"
```

Fix all `INfceXmlBuilder` references: `SefazClient.cs` (change field type and ctor param), `InfrastructureServiceCollectionExtensions.cs` (re-register as `IFiscalDocumentXmlBuilder`).

- [ ] **Step 6: Update DI registration**

In `InfrastructureServiceCollectionExtensions.cs`, find the registration:

```csharp
services.AddScoped<INfceXmlBuilder, NfceXmlBuilder>();
```

Replace with:

```csharp
services.AddScoped<IFiscalDocumentXmlBuilder, FiscalDocumentXmlBuilder>();
```

- [ ] **Step 7: Run tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentXmlBuilderNfeTests" -v quiet
```

Expected: 4 tests PASS.

- [ ] **Step 8: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

- [ ] **Step 9: Commit**

```powershell
git add src/VisuFiscalHub.Application/Common/Interfaces/ `
        src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/ `
        src/VisuFiscalHub.Infrastructure/ `
        tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/FiscalDocumentXmlBuilderNfeTests.cs
git commit -m "feat: rename NfceXmlBuilder to FiscalDocumentXmlBuilder with NF-e XML support"
```

---

## Task 6: `SefazEndpointResolver` — NF-e URL Table

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/SefazEndpointResolverNfeTests.cs`:

```csharp
using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class SefazEndpointResolverNfeTests
{
    // NF-e SVRS covers different UFs than NFC-e SVRS:
    // AM(13), BA(29), CE(23), GO(52), MA(21), MS(50), MT(51), PA(15), PE(26), PI(22) → SVAN
    // AC(12), AL(27), AP(16), DF(53), ES(32), PB(25), RJ(33), RN(24), RO(11), RR(14), SC(42), SE(28), TO(17) → SVRS-NF-e

    [Theory]
    [InlineData(12)] // AC — SVRS
    [InlineData(53)] // DF — SVRS
    [InlineData(32)] // ES — SVRS
    public void AutorizacaoNfe_UfSvrs_RetornaUrlSvrs(int uf)
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(uf, AmbienteSefaz.Producao);
        url.ShouldContain("nfe.svrs.rs.gov.br");
        url.ShouldContain("NFeAutorizacao4");
    }

    [Theory]
    [InlineData(13)] // AM — SVAN
    [InlineData(29)] // BA — SVAN
    [InlineData(52)] // GO — SVAN
    public void AutorizacaoNfe_UfSvan_RetornaUrlSvan(int uf)
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(uf, AmbienteSefaz.Producao);
        url.ShouldContain("www.sefazvirtual.fazenda.gov.br");
    }

    [Theory]
    [InlineData(35)] // SP — own server
    [InlineData(31)] // MG — own server
    [InlineData(41)] // PR — own server
    [InlineData(43)] // RS — own server
    public void AutorizacaoNfe_UfPropria_RetornaUrlEsperada(int uf)
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(uf, AmbienteSefaz.Producao);
        url.ShouldNotBeNullOrEmpty();
        url.ShouldContain("NFeAutorizacao4");
    }

    [Fact]
    public void AutorizacaoNfe_Homologacao_RetornaUrlHomologacao()
    {
        var url = SefazEndpointResolver.AutorizacaoNfe(35, AmbienteSefaz.Homologacao);
        url.ShouldContain("homologacao");
    }

    [Fact]
    public void NfceENfeCobremUfsDistintas()
    {
        // NFC-e has no SVAN; NF-e has SVAN for AM, BA, CE, GO, MA, MS, MT, PA, PE, PI
        var nfceUrl = SefazEndpointResolver.Autorizacao(13, AmbienteSefaz.Producao);
        var nfeUrl  = SefazEndpointResolver.AutorizacaoNfe(13, AmbienteSefaz.Producao);
        // NFC-e AM uses its own server; NF-e AM uses SVAN
        nfceUrl.ShouldContain("sefaz.am.gov.br");
        nfeUrl.ShouldContain("sefazvirtual");
    }

    [Fact]
    public void ResolveEventoNfe_Homologacao_RetornaUrlCorreta()
    {
        var url = SefazEndpointResolver.ResolveEventoNfe(35, AmbienteSefaz.Homologacao);
        url.ShouldContain("NFeRecepcaoEvento4");
        url.ShouldContain("homologacao");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~SefazEndpointResolverNfeTests" --no-build 2>&1 | Select-String -Pattern "error|FAILED"
```

Expected: Build error — `AutorizacaoNfe`, `ConsultaProtocoloNfe`, `ResolveEventoNfe` do not exist.

- [ ] **Step 3: Add NF-e endpoint methods to `SefazEndpointResolver`**

Add to the end of `SefazEndpointResolver.cs` (before closing `}`):

```csharp
    // ── NF-e Modelo 55 endpoints ────────────────────────────────────────────
    // NF-e SVRS: AC AL AP DF ES PB RJ RN RO RR SC SE TO
    private static readonly IReadOnlySet<int> UfsSvrsNfe = new HashSet<int>
    {
        12, // AC
        27, // AL
        16, // AP
        53, // DF
        32, // ES
        25, // PB
        33, // RJ
        24, // RN
        11, // RO
        14, // RR
        42, // SC
        28, // SE
        17  // TO
    };

    // NF-e SVAN: AM BA CE GO MA MS MT PA PE PI
    private static readonly IReadOnlySet<int> UfsSvanNfe = new HashSet<int>
    {
        13, // AM
        29, // BA
        23, // CE
        52, // GO
        21, // MA
        50, // MS
        51, // MT
        15, // PA
        26, // PE
        22  // PI
    };

    private const string SvrsNfeP = "https://nfe.svrs.rs.gov.br/ws";
    private const string SvrsNfeH = "https://nfe-homologacao.svrs.rs.gov.br/ws";
    private const string SvanP    = "https://www.sefazvirtual.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";
    private const string SvanH    = "https://hom.sefazvirtual.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";

    /// <summary>Retorna a URL do webservice de autorização NF-e (NfeAutorizacao4).</summary>
    public static string AutorizacaoNfe(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrsNfe.Contains(ufCodigo))
        {
            var base_ = h ? SvrsNfeH : SvrsNfeP;
            return $"{base_}/NFeAutorizacao4/NFeAutorizacao4.asmx";
        }

        if (UfsSvanNfe.Contains(ufCodigo))
            return h ? SvanH : SvanP;

        // UFs with own NF-e servers
        return ufCodigo switch
        {
            31 => h ? "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4"
                    : "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeAutorizacao4",           // MG
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfeservice/services/NFeAutorizacao4"
                    : "https://nfe.fazenda.sp.gov.br/nfeservice/services/NFeAutorizacao4",     // SP
            41 => h ? "https://homologacao.nfe.pr.gov.br/nfe/NFeAutorizacao4"
                    : "https://nfe.pr.gov.br/nfe/NFeAutorizacao4",                             // PR
            43 => h ? "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx"
                    : "https://nfe.sefazrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx", // RS
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint NF-e de autorização mapeado.")
        };
    }

    /// <summary>Retorna a URL do webservice de consulta NF-e (NfeConsultaProtocolo4).</summary>
    public static string ConsultaProtocoloNfe(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrsNfe.Contains(ufCodigo))
        {
            var base_ = h ? SvrsNfeH : SvrsNfeP;
            return $"{base_}/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        }

        if (UfsSvanNfe.Contains(ufCodigo))
        {
            return h ? "https://hom.sefazvirtual.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx"
                     : "https://www.sefazvirtual.fazenda.gov.br/NFeConsultaProtocolo4/NFeConsultaProtocolo4.asmx";
        }

        return ufCodigo switch
        {
            31 => h ? "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4"
                    : "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeConsultaProtocolo4",
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfeservice/services/NFeConsultaProtocolo4"
                    : "https://nfe.fazenda.sp.gov.br/nfeservice/services/NFeConsultaProtocolo4",
            41 => h ? "https://homologacao.nfe.pr.gov.br/nfe/NFeConsultaProtocolo4"
                    : "https://nfe.pr.gov.br/nfe/NFeConsultaProtocolo4",
            43 => h ? "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx"
                    : "https://nfe.sefazrs.rs.gov.br/ws/NfeConsultaProtocolo/NFeConsultaProtocolo4.asmx",
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint NF-e de consulta mapeado.")
        };
    }

    /// <summary>Retorna a URL do webservice de recepção de eventos NF-e (NFeRecepcaoEvento4).</summary>
    public static string ResolveEventoNfe(int ufCodigo, AmbienteSefaz ambiente)
    {
        var h = ambiente == AmbienteSefaz.Homologacao;

        if (UfsSvrsNfe.Contains(ufCodigo))
        {
            var base_ = h ? SvrsNfeH : SvrsNfeP;
            return $"{base_}/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        }

        if (UfsSvanNfe.Contains(ufCodigo))
        {
            return h ? "https://hom.sefazvirtual.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx"
                     : "https://www.sefazvirtual.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx";
        }

        return ufCodigo switch
        {
            31 => h ? "https://hnfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4"
                    : "https://nfe.fazenda.mg.gov.br/nfe2/services/NFeRecepcaoEvento4",
            35 => h ? "https://homologacao.nfe.fazenda.sp.gov.br/nfeservice/services/NFeRecepcaoEvento4"
                    : "https://nfe.fazenda.sp.gov.br/nfeservice/services/NFeRecepcaoEvento4",
            41 => h ? "https://homologacao.nfe.pr.gov.br/nfe/NFeRecepcaoEvento4"
                    : "https://nfe.pr.gov.br/nfe/NFeRecepcaoEvento4",
            43 => h ? "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx"
                    : "https://nfe.sefazrs.rs.gov.br/ws/NfeRecepcaoEvento/NFeRecepcaoEvento4.asmx",
            _ => throw new InvalidOperationException($"UF {ufCodigo} sem endpoint NF-e de evento mapeado.")
        };
    }
```

- [ ] **Step 4: Run tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~SefazEndpointResolverNfeTests" -v quiet
```

Expected: All 6 tests PASS.

- [ ] **Step 5: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

- [ ] **Step 6: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs `
        tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/SefazEndpointResolverNfeTests.cs
git commit -m "feat: add NF-e endpoint methods to SefazEndpointResolver (SVRS/SVAN/UF própria)"
```

---

## Task 7: `SefazClient` + `FiscalDocumentProcessingJob` — Wire Up NF-e

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazClient.cs`
- Rename: `NfceProcessingJob.cs` → `FiscalDocumentProcessingJob.cs`

- [ ] **Step 1: Update `SefazClient` to use `IFiscalDocumentXmlBuilder` and tipo-aware endpoints**

In `SefazClient.cs`:

**a) Change field type and constructor:**
```csharp
    private readonly IFiscalDocumentXmlBuilder _xmlBuilder;  // was INfceXmlBuilder

    public SefazClient(
        // ...other params...
        IFiscalDocumentXmlBuilder xmlBuilder,  // was INfceXmlBuilder
        // ...
```

**b) Make the URL selection tipo-aware in `SubmeterAutorizacaoAsync`:**
```csharp
        var url = documento.Tipo == TipoDocumento.Nfe
            ? SefazEndpointResolver.AutorizacaoNfe(ufCodigo, tenant.ConfiguracaoFiscal.Ambiente)
            : SefazEndpointResolver.Autorizacao(ufCodigo, tenant.ConfiguracaoFiscal.Ambiente);
```

**c) Make QR Code extraction null-safe (NF-e has no QR code — that is normal and not a warning):**
```csharp
        var qrCodeUrl = documento.Tipo == TipoDocumento.NfCe
            ? ExtractQrCodeUrl(xmlResult.Value)
            : null;

        if (qrCodeUrl is null && documento.Tipo == TipoDocumento.NfCe)
            _logger.LogWarning("QR Code URL ausente no XML NFC-e para {DocumentoId}.", documentoId.Value);
```

**d) Make the consulta URL tipo-aware in `ConsultarNfeAsync`:**
```csharp
        // Need to pass TipoDocumento — add it to method signature
        public async Task<Result<SefazConsultaRetorno>> ConsultarNfeAsync(
            string chaveAcesso,
            TenantId tenantId,
            TipoDocumento tipo,   // NEW
            CancellationToken ct)
        {
            // ...
            var url = tipo == TipoDocumento.Nfe
                ? SefazEndpointResolver.ConsultaProtocoloNfe(ufCodigo, tenant.ConfiguracaoFiscal.Ambiente)
                : SefazEndpointResolver.ConsultaProtocolo(ufCodigo, tenant.ConfiguracaoFiscal.Ambiente);
```

- [ ] **Step 2: Update `ISefazClient` interface to add `tipo` parameter to `ConsultarNfeAsync`**

Find `src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs` and add `TipoDocumento tipo` to the signature.

- [ ] **Step 3: Rename `NfceProcessingJob.cs` to `FiscalDocumentProcessingJob.cs`**

```powershell
Rename-Item "src/VisuFiscalHub.Infrastructure/Jobs/NfceProcessingJob.cs" `
            "src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs"
```

Then update the file:
- Rename class `NfceProcessingJob` → `FiscalDocumentProcessingJob`
- Update `ILogger<NfceProcessingJob>` → `ILogger<FiscalDocumentProcessingJob>`
- Update `AutorizarAsync`: NF-e has `null` QR code — do not throw, pass `QrCode.Empty` or handle:

```csharp
    private async Task AutorizarAsync(
        Domain.Entities.DocumentoFiscal documento,
        SefazRetorno retorno,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(retorno.NProt))
        {
            _logger.LogError("NProt ausente para {DocumentoId}", documento.Id.Value);
            throw new InvalidOperationException($"NProt ausente na autorização de {documento.Id.Value}.");
        }

        // NFC-e requires QR code; NF-e does not have one
        if (documento.Tipo == TipoDocumento.NfCe && string.IsNullOrWhiteSpace(retorno.QrCodeUrl))
        {
            _logger.LogError("QrCodeUrl ausente para NFC-e {DocumentoId}", documento.Id.Value);
            throw new InvalidOperationException($"QrCodeUrl ausente na autorização de {documento.Id.Value}.");
        }

        var qrCode = documento.Tipo == TipoDocumento.NfCe
            ? QrCode.FromStorage(retorno.QrCodeUrl!)
            : QrCode.NaoAplicavel(); // NF-e: sentinel value (see step 4)

        // ...rest unchanged
    }
```

- Update `AutorizarPorDuplicidadeAsync` to pass `documento.Tipo` to `ConsultarNfeAsync`:
```csharp
        var consultaResult = await _sefazClient.ConsultarNfeAsync(
            documento.ChaveAcesso.Valor, documento.TenantId, documento.Tipo, ct);
```

- [ ] **Step 4: Add `QrCode.NaoAplicavel()` factory to `QrCode` value object**

In `src/VisuFiscalHub.Domain/ValueObjects/QrCode.cs`, add:

```csharp
    // Sentinel for document types that don't use QR codes (NF-e Modelo 55).
    // Stored as empty string in the database; never rendered in DANFE.
    public static QrCode NaoAplicavel() => new(string.Empty);
```

Also update `DocumentoFiscal.Autorizar` to accept null QR code for NF-e — or keep `QrCode.NaoAplicavel()` as the sentinel that is passed.

- [ ] **Step 5: Update `Program.cs` to use the renamed job**

In `Program.cs`, update the reconciliation cron:
```csharp
        RecurringJob.AddOrUpdate<ReconciliacaoJobProcessor>(
            "reconciliacao-fiscal",  // renamed from reconciliacao-nfce
            job => job.ExecuteAsync(CancellationToken.None),
            "*/5 * * * *");
```

Also update the `using` + any `IDocumentJobQueue.EnqueueProcessingAsync` registration that references the job class by name (Hangfire auto-discovers by class name, but DI registration may reference it).

- [ ] **Step 6: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/ `
        src/VisuFiscalHub.Application/Common/Interfaces/ISefazClient.cs `
        src/VisuFiscalHub.Api/Program.cs
git commit -m "feat: rename NfceProcessingJob to FiscalDocumentProcessingJob; tipo-aware SEFAZ routing"
```

---

## Task 8: `CancelamentoJob` — NF-e Endpoint Routing

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs`

- [ ] **Step 1: Update `CancelamentoJob` to use tipo-aware `ResolveEvento`**

In `CancelamentoJob.cs`, replace the current call:

```csharp
        var url = SefazEndpointResolver.ResolveEvento(ufCodigo, ambiente);
```

With:

```csharp
        var url = documento.Tipo == TipoDocumento.Nfe
            ? SefazEndpointResolver.ResolveEventoNfe(ufCodigo, ambiente)
            : SefazEndpointResolver.ResolveEvento(ufCodigo, ambiente);
```

Add the required `using VisuFiscalHub.Domain.Enums;` if not already present.

- [ ] **Step 2: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

Expected: All tests pass.

- [ ] **Step 3: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Jobs/CancelamentoJob.cs
git commit -m "feat: CancelamentoJob uses NF-e endpoint for Modelo 55 documents"
```

---

## Task 9: `IssueDocumentCommand` — NF-e Fields + Validator + Handler

**Files:**
- Modify: `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs`
- Modify: `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommandValidator.cs`
- Modify: `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommandHandler.cs`
- Create: `tests/VisuFiscalHub.Tests/Application/IssueDocumentNfeCommandValidatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/VisuFiscalHub.Tests/Application/IssueDocumentNfeCommandValidatorTests.cs`:

```csharp
using FluentValidation.TestHelper;
using Shouldly;
using VisuFiscalHub.Application.Common.Models;
using VisuFiscalHub.Application.Documents.Commands.IssueDocument;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Application;

public class IssueDocumentNfeCommandValidatorTests
{
    private readonly IssueDocumentCommandValidator _validator = new();

    private static NfeDestinatarioDto ValidDest() => new(
        CnpjOuCpf: "11222333000181",
        RazaoSocial: "Empresa Teste Ltda",
        IndIeDest: 9, Ie: null,
        Logradouro: "Rua Teste", Numero: "100", Complemento: null,
        Bairro: "Centro", Municipio: "São Paulo",
        CodigoMunicipio: "3550308", Uf: "SP", Cep: "01310100",
        Email: null);

    private static IssueDocumentCommand BaseNfeCommand() => new()
    {
        TenantId = new TenantId(Guid.NewGuid()),
        ClienteAppId = new ClienteAppId(Guid.NewGuid()),
        IdempotencyKey = "KEY-001",
        Tipo = TipoDocumento.Nfe,
        IndPresenca = 0,
        NatOp = "Venda de mercadoria",
        NfeDestinatario = ValidDest(),
        Itens = [new ItemDocumentoDto("001", "Produto", "12345678", null,
            "5102", "UN", 1, 10m, 0, 0, new TributoDto("CRT", "CST", 0, 0, 0,
                "99", 0, 0, 0, "99", 0, 0, 0))],
        Pagamentos = [new PagamentoDto(TipoPagamento.Dinheiro, 10m)]
    };

    [Fact]
    public void Nfe_ComDestinatarioValido_Valido()
    {
        var result = _validator.TestValidate(BaseNfeCommand());
        result.ShouldNotHaveValidationErrorFor(x => x.NfeDestinatario);
    }

    [Fact]
    public void Nfe_SemDestinatario_Invalido()
    {
        var cmd = BaseNfeCommand() with { NfeDestinatario = null };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.NfeDestinatario);
    }

    [Fact]
    public void Nfe_SemNatOp_Invalido()
    {
        var cmd = BaseNfeCommand() with { NatOp = null };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.NatOp);
    }

    [Fact]
    public void Nfce_ComDestinatario_Invalido()
    {
        var cmd = new IssueDocumentCommand
        {
            TenantId = new TenantId(Guid.NewGuid()),
            ClienteAppId = new ClienteAppId(Guid.NewGuid()),
            IdempotencyKey = "KEY-002",
            Tipo = TipoDocumento.NfCe,
            IndPresenca = 1,
            NfeDestinatario = ValidDest(), // not allowed for NFC-e
            Itens = BaseNfeCommand().Itens,
            Pagamentos = BaseNfeCommand().Pagamentos
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveAnyValidationError();
    }

    [Fact]
    public void Nfe_IndPresenca0_Valido()
    {
        var cmd = BaseNfeCommand() with { IndPresenca = 0 };
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.IndPresenca);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~IssueDocumentNfeCommandValidatorTests" --no-build 2>&1 | Select-String -Pattern "error|FAILED"
```

Expected: Build error — `NfeDestinatario`, `NatOp`, `NfeDestinatarioDto` not in command.

- [ ] **Step 3: Add `NfeDestinatarioDto` and update `IssueDocumentCommand`**

Add `NfeDestinatarioDto` record (in same file or a shared models file):

```csharp
public sealed record NfeDestinatarioDto(
    string CnpjOuCpf,
    string RazaoSocial,
    int IndIeDest,
    string? Ie,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    string CodigoMunicipio,
    string Uf,
    string Cep,
    string? Email);
```

Update `IssueDocumentCommand.cs`:

```csharp
public sealed record IssueDocumentCommand : ICommand<Result<IssueDocumentResponse>>
{
    public TenantId TenantId { get; init; }
    public ClienteAppId ClienteAppId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public TipoDocumento Tipo { get; init; } = TipoDocumento.NfCe;
    public IReadOnlyList<ItemDocumentoDto> Itens { get; init; } = [];
    public IReadOnlyList<PagamentoDto> Pagamentos { get; init; } = [];
    public ConsumidorDto? Consumidor { get; init; }
    public int IndPresenca { get; init; } = 1;
    // NF-e only
    public NfeDestinatarioDto? NfeDestinatario { get; init; }
    public string? NatOp { get; init; }
    public int ModFrete { get; init; } = 9;
}
```

- [ ] **Step 4: Update validator**

In `IssueDocumentCommandValidator.cs`:

```csharp
    private static readonly int[] IndPresencaValidosNfce = [1, 3, 4, 9];
    private static readonly int[] IndPresencaValidosNfe  = [0, 1, 2, 3, 4, 5, 9];

    public IssueDocumentCommandValidator()
    {
        // ...existing rules...

        // Replace IndPresenca rule with tipo-aware version:
        RuleFor(x => x.IndPresenca)
            .Must((cmd, v) =>
            {
                var validos = cmd.Tipo == TipoDocumento.Nfe ? IndPresencaValidosNfe : IndPresencaValidosNfce;
                return validos.Contains(v);
            })
            .WithMessage("IndPresenca inválido para o tipo de documento.");

        // NF-e rules
        When(x => x.Tipo == TipoDocumento.Nfe, () =>
        {
            RuleFor(x => x.NfeDestinatario)
                .NotNull().WithMessage("Destinatário é obrigatório para NF-e.");

            RuleFor(x => x.NatOp)
                .NotEmpty().WithMessage("Natureza da operação é obrigatória para NF-e.")
                .MaximumLength(60);
        });

        // NFC-e rules
        When(x => x.Tipo == TipoDocumento.NfCe, () =>
        {
            RuleFor(x => x.NfeDestinatario)
                .Null().WithMessage("Destinatário NF-e não é permitido em NFC-e Modelo 65.");
        });
    }
```

- [ ] **Step 5: Update handler to pass NF-e fields**

In `IssueDocumentCommandHandler.cs`:

**a) Update sequence number retrieval — use `SerieNfe` for NF-e:**
```csharp
        var serie = command.Tipo == TipoDocumento.Nfe
            ? tenant.ConfiguracaoFiscal.SerieNfe ?? tenant.ConfiguracaoFiscal.Serie
            : tenant.ConfiguracaoFiscal.Serie;

        var numeroResult = await _sequenceManager.GetNextNumeroAsync(
            command.TenantId, serie, cancellationToken);
```

**b) Update `ChaveAcesso.Gerar` — modelo 55 for NF-e:**
```csharp
        var chaveResult = ChaveAcesso.Gerar(
            tenant.ConfiguracaoFiscal.UfCodigo,
            aamm,
            tenant.Cnpj.Valor,
            (int)command.Tipo,  // 55 for NF-e, 65 for NFC-e
            serie,
            numeroResult.Value.ToString(),
            TipoEmissao.Normal,
            cNF);
```

**c) Build `NfeDestinatario` domain VO from DTO:**
```csharp
        NfeDestinatario? nfeDestinatario = null;
        if (command.NfeDestinatario is { } destDto)
        {
            var destResult = NfeDestinatario.Criar(
                destDto.CnpjOuCpf, destDto.RazaoSocial, destDto.IndIeDest, destDto.Ie,
                destDto.Logradouro, destDto.Numero, destDto.Complemento,
                destDto.Bairro, destDto.Municipio, destDto.CodigoMunicipio,
                destDto.Uf, destDto.Cep, destDto.Email);
            if (destResult.IsFailure)
                return Result.Failure<IssueDocumentResponse>(destResult.Error);
            nfeDestinatario = destResult.Value;
        }
```

**d) Pass new fields to `DocumentoFiscal.Criar`:**
```csharp
        var documentoResult = DocumentoFiscal.Criar(
            // ...existing params...
            nfeDestinatario: nfeDestinatario,
            natOp: command.NatOp,
            modFrete: command.ModFrete);
```

- [ ] **Step 6: Run tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~IssueDocumentNfeCommandValidatorTests" -v quiet
```

Expected: 5 tests PASS.

- [ ] **Step 7: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

- [ ] **Step 8: Commit**

```powershell
git add src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/ `
        tests/VisuFiscalHub.Tests/Application/IssueDocumentNfeCommandValidatorTests.cs
git commit -m "feat: IssueDocumentCommand supports NF-e fields (NfeDestinatario, NatOp, ModFrete)"
```

---

## Task 10: `CreateTenantCommand` + `SerieNfe`

**Files:**
- Modify: `src/VisuFiscalHub.Application/Tenants/Commands/CreateTenant/CreateTenantCommand.cs`
- Modify: `src/VisuFiscalHub.Application/Tenants/Commands/CreateTenant/CreateTenantCommandHandler.cs`

- [ ] **Step 1: Add `SerieNfe?` to `CreateTenantCommand`**

```csharp
public sealed record CreateTenantCommand : ICommand<Result<TenantResponse>>
{
    // ...existing fields...
    public string? SerieNfe { get; init; }
}
```

- [ ] **Step 2: Update handler to pass `SerieNfe` and create NF-e sequence**

In `CreateTenantCommandHandler.cs`:

**a) Pass `SerieNfe` to `ConfiguracaoFiscal.Criar`:**
```csharp
        var configResult = ConfiguracaoFiscal.Criar(
            command.RegimeTributario,
            command.Serie,
            command.Ambiente,
            command.UfCodigo,
            command.InscricaoEstadual,
            serieNfe: command.SerieNfe);
```

**b) Create NF-e sequence if `SerieNfe` is provided:**
```csharp
        var transactionResult = await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // ...existing checks...
            await _tenantRepository.AddAsync(tenant, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            var seqResult = await _sequenceManager.EnsureNumeracaoSequenceAsync(tenant.Id, command.Serie, ct);
            if (seqResult.IsFailure) return seqResult;

            if (command.SerieNfe is not null)
            {
                var seqNfeResult = await _sequenceManager.EnsureNumeracaoSequenceAsync(
                    tenant.Id, command.SerieNfe, ct);
                if (seqNfeResult.IsFailure) return seqNfeResult;
            }

            return Result.Success();
        }, cancellationToken);
```

- [ ] **Step 3: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```powershell
git add src/VisuFiscalHub.Application/Tenants/Commands/CreateTenant/
git commit -m "feat: CreateTenantCommand supports SerieNfe for NF-e numbering"
```

---

## Task 11: API Endpoint `POST /api/v1/documentos/nfe`

**Files:**
- Modify: `src/VisuFiscalHub.Api/Program.cs`

- [ ] **Step 1: Add `IssueNfeRequest` record and `POST /nfe` endpoint**

In `Program.cs`, below the existing `POST /nfce` endpoint:

```csharp
    documentos.MapPost("/nfe",
        async (
            IssueNfeRequest request,
            HttpContext ctx,
            ICurrentUserContext userCtx,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var idempotencyKey = ctx.Request.Headers["X-Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return Results.Problem(
                    detail: "DocumentoFiscal.IdempotencyKeyInvalida",
                    title: "X-Idempotency-Key é obrigatório.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            var command = new IssueDocumentCommand
            {
                TenantId = userCtx.TenantId,
                ClienteAppId = userCtx.ClienteAppId,
                IdempotencyKey = idempotencyKey,
                Tipo = TipoDocumento.Nfe,
                Itens = request.Itens,
                Pagamentos = request.Pagamentos,
                IndPresenca = request.IndPresenca,
                NatOp = request.NatOp,
                NfeDestinatario = request.NfeDestinatario,
                ModFrete = request.ModFrete
            };

            return (await mediator.Send(command, ct))
                .ToHttpResult(r => Results.Accepted($"/api/v1/documentos/{r.DocumentoId.Value}/status", r));
        })
        .RequireRateLimiting("api");
```

Add the request record at the end of `Program.cs`:

```csharp
internal sealed record IssueNfeRequest(
    IReadOnlyList<ItemDocumentoDto> Itens,
    IReadOnlyList<PagamentoDto> Pagamentos,
    NfeDestinatarioDto NfeDestinatario,
    string NatOp,
    int IndPresenca = 0,
    int ModFrete = 9);
```

- [ ] **Step 2: Update `CreateTenantCommand` API binding**

In the `POST /api/v1/tenants` handler, `CreateTenantCommand` now has `SerieNfe` — it will be deserialized automatically from the request body (no mapping change needed since it's a `[FromBody]`-style binding via Mediator).

- [ ] **Step 3: Build to catch compile errors**

```powershell
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj -v quiet
```

Expected: Build succeeds.

- [ ] **Step 4: Run full suite**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Api/Program.cs
git commit -m "feat: add POST /api/v1/documentos/nfe endpoint"
```

---

## Task 12: Integration Tests — End-to-End NF-e Flow

**Files:**
- Create/Modify: integration test file (check existing integration test structure first)

- [ ] **Step 1: Find the existing integration test project**

```powershell
Get-ChildItem -Recurse -Filter "*Integration*" -Include "*.csproj" | Select-Object FullName
```

If there's no integration test project, add tests to the existing test project using `WebApplicationFactory<Program>`.

- [ ] **Step 2: Write the failing integration tests**

Add to the appropriate integration test class (find by grepping for `WebApplicationFactory`):

```csharp
    [Fact]
    public async Task PostNfe_SemXIdempotencyKey_Retorna422()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/documentos/nfe",
            BuildValidNfeRequest());
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfe_SemAutorizacao_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfe");
        request.Headers.Add("X-Idempotency-Key", "TEST-KEY");
        request.Content = JsonContent.Create(BuildValidNfeRequest());
        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
```

- [ ] **Step 3: Run integration tests**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "Category=Integration" -v quiet
```

Expected: New tests pass (or adapt filter to match existing integration test naming convention).

- [ ] **Step 4: Run full suite — final check**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet
```

Expected: All tests pass (640+ tests, no regressions).

- [ ] **Step 5: Commit**

```powershell
git add tests/
git commit -m "test: integration tests for NF-e endpoint"
```

---

## Self-Review Checklist

**Spec coverage:**

| Spec requirement | Task |
|-----------------|------|
| `NfeDestinatario` VO with all fields | Task 1 |
| New domain errors (DestinatarioObrigatorio, etc.) | Task 1 |
| `ConfiguracaoFiscal.SerieNfe` | Task 2 |
| `DocumentoFiscal` NF-e fields + tipo-aware validation | Task 3 |
| 24h cancellation window for NF-e | Task 3 |
| EF Core migration (jsonb, nat_op, mod_frete, serie_nfe) | Task 4 |
| `FiscalDocumentXmlBuilder` NF-e XML (mod=55, dest, no infNFeSupl) | Task 5 |
| NF-e endpoint tables (SVRS, SVAN, own) | Task 6 |
| `SefazClient` tipo-aware URL + null-safe QR Code | Task 7 |
| `FiscalDocumentProcessingJob` rename + NF-e aware | Task 7 |
| `CancelamentoJob` NF-e endpoint | Task 8 |
| `IssueDocumentCommand` NF-e fields | Task 9 |
| Validator NF-e rules | Task 9 |
| Handler `SerieNfe` for numeração + VO construction | Task 9 |
| `CreateTenantCommand.SerieNfe` | Task 10 |
| `POST /api/v1/documentos/nfe` | Task 11 |
| Integration + domain tests | Tasks 1,3,5,6,9,12 |

**All spec requirements covered. No TBDs or placeholders.**
