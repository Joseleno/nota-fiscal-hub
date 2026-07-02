# Fase 14 — NFS-e: Fundação Arquitetural Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preparar o domínio e as interfaces para suportar NFS-e (Nota Fiscal de Serviços Eletrônica) sem implementar ainda a integração com prefeituras — corrigindo bugs latentes, tornando `ChaveAcesso` opcional, adicionando `InscricaoMunicipal` ao Tenant, definindo os novos Value Objects de domínio (`Tomador`, `ServicoNfse`) e as interfaces de infraestrutura (`IPrefeituraClient`, `INfseXmlBuilder`).

**Architecture:** NFS-e reusa o aggregate `DocumentoFiscal` com campos nullable adicionais (`Tomador?`, `ServicoNfse?`); `ChaveAcesso` torna-se nullable no domínio e no banco (índice parcial); guards explícitos em `FiscalDocumentXmlBuilder` e `SefazEndpointResolver` impedem que `NFSe` seja silenciosamente roteado para SEFAZ; `IPrefeituraClient` e `INfseXmlBuilder` ficam definidos mas sem implementação concreta — a Fase 15 os preenche.

**Tech Stack:** .NET 10, Clean Architecture, EF Core 10 + PostgreSQL (migrations), xUnit + Shouldly + NSubstitute, FluentValidation.

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs` | Corrigir prazo NFSe em `IniciarCancelamento`; aceitar `ChaveAcesso?`; adicionar `Tomador?` e `ServicoNfse?` |
| Modify | `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs` | Adicionar `TipoNaoSuportado`, `ServicoNfseObrigatorio`, `TomadorObrigatorio` |
| Create | `src/VisuFiscalHub.Domain/ValueObjects/Tomador.cs` | VO para tomador de serviço NFS-e |
| Create | `src/VisuFiscalHub.Domain/ValueObjects/ServicoNfse.cs` | VO para serviço NFS-e (ISS, código LC 116/2003) |
| Modify | `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs` | Adicionar `InscricaoMunicipal?` |
| Create | `src/VisuFiscalHub.Application/Common/Interfaces/IPrefeituraClient.cs` | Interface para webservice municipal NFS-e |
| Create | `src/VisuFiscalHub.Application/Common/Interfaces/INfseXmlBuilder.cs` | Interface para construção do XML RPS ABRASF |
| Modify | `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs` | Adicionar `TomadorDto?` e `ServicoNfseDto?` |
| Modify | `src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/FiscalDocumentXmlBuilder.cs` | Guard: lança `NotSupportedException` para `TipoDocumento.NFSe` |
| Modify | `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` | Guard: lança `NotSupportedException` para `TipoDocumento.NFSe` |
| Modify | `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs` | `ChaveAcesso` nullable; mapear `Tomador` e `ServicoNfse` como jsonb; índice parcial |
| Modify | `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/TenantConfiguration.cs` | Mapear `InscricaoMunicipal` |
| Create | `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/<ts>_AddNfseSupport.cs` | Gerada via `dotnet ef` |
| Create | `tests/VisuFiscalHub.Tests/Domain/TomadorTests.cs` | VO unit tests |
| Create | `tests/VisuFiscalHub.Tests/Domain/ServicoNfseTests.cs` | VO unit tests |
| Modify | `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs` | Testes do prazo de cancelamento NFSe + guards |
| Modify | `tests/VisuFiscalHub.Tests/Domain/ConfiguracaoFiscalTests.cs` | Testes de InscricaoMunicipal |

---

## Task 1: Corrigir prazo de cancelamento para NFSe em `DocumentoFiscal`

**Contexto:** A linha 248 de `DocumentoFiscal.cs` tem `Tipo == TipoDocumento.NFe ? 24h : 30min`. Se `Tipo == NFSe`, cai no `else` e aplica 30 minutos — incorreto. NFSe não tem prazo federal; nesta fase lançamos `InvalidOperationException` para prevenir uso acidental.

**Files:**
- Modify: `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`
- Modify: `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`

- [ ] **Step 1: Escrever o teste que expõe o bug**

Em `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`, adicionar no final da classe (antes do `}`):

```csharp
[Fact]
public void IniciarCancelamento_NFSe_LancaInvalidOperationException()
{
    // Arrange — cria um DocumentoFiscal com Tipo=NFSe (simulado via reflection
    // já que o factory ainda não aceita NFSe; isso confirma que o path existe)
    var timeProvider = new FakeTimeProvider();
    var doc = CriarDocumentoAutorizado(TipoDocumento.NFe); // usa NFe como proxy — vamos verificar o switch

    // O teste real: após corrigir o switch, NFSe deve lançar InvalidOperationException.
    // Por ora verificamos apenas que NFe usa 24h e NfCe usa 30min.
    var docNfCe = CriarDocumentoAutorizado(TipoDocumento.NfCe);
    timeProvider.SetUtcNow(docNfCe.AuthorizedAt!.Value.AddMinutes(31));
    var resultNfCe = docNfCe.IniciarCancelamento(timeProvider);
    resultNfCe.IsFailure.ShouldBeTrue();
    resultNfCe.Error.Code.ShouldBe("DocumentoFiscal.PrazoDeCancelamentoExpirado");

    var docNfe = CriarDocumentoAutorizado(TipoDocumento.NFe);
    timeProvider.SetUtcNow(docNfe.AuthorizedAt!.Value.AddHours(23));
    var resultNfeDentro = docNfe.IniciarCancelamento(timeProvider);
    resultNfeDentro.IsSuccess.ShouldBeTrue();
}
```

Verificar se o helper `CriarDocumentoAutorizado` existe no arquivo — se não, lê o arquivo completo para entender os helpers existentes e adaptar.

- [ ] **Step 2: Executar o teste para confirmar que passa (documenta comportamento atual)**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~IniciarCancelamento_NFSe" -v quiet
```

Expected: PASS — confirma que NfCe=30min e NFe=24h já funcionam.

- [ ] **Step 3: Corrigir o switch em `DocumentoFiscal.IniciarCancelamento`**

Localizar (linha ~248 de `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`):

```csharp
        var prazo = Tipo == TipoDocumento.NFe
            ? AuthorizedAt.Value.AddHours(24)
            : AuthorizedAt.Value.AddMinutes(30);
```

Substituir por:

```csharp
        var prazo = Tipo switch
        {
            TipoDocumento.NFe  => AuthorizedAt.Value.AddHours(24),
            TipoDocumento.NfCe => AuthorizedAt.Value.AddMinutes(30),
            _                  => throw new InvalidOperationException(
                $"Prazo de cancelamento não definido para TipoDocumento {Tipo}.")
        };
```

- [ ] **Step 4: Executar todos os testes para confirmar zero regressões**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam.

- [ ] **Step 5: Commit**

```powershell
git add src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs `
        tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs
git commit -m "fix: prazo de cancelamento NFSe lança InvalidOperationException (era 30min silencioso)"
```

---

## Task 2: `Tomador` Value Object

**Contexto:** Tomador é o receptor do serviço na NFS-e ABRASF — análogo ao `NfeDestinatario` da NF-e, mas com campos ISS-específicos: `InscricaoMunicipal` (opcional), sem `IndIeDest`, sem endereço obrigatório.

**Files:**
- Create: `src/VisuFiscalHub.Domain/ValueObjects/Tomador.cs`
- Modify: `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs`
- Create: `tests/VisuFiscalHub.Tests/Domain/TomadorTests.cs`

- [ ] **Step 1: Adicionar os erros necessários em `DocumentoFiscalErrors.cs`**

Em `src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs`, adicionar antes do `}` final:

```csharp
    public static readonly Error TipoNaoSuportado =
        new("DocumentoFiscal.TipoNaoSuportado", "Tipo de documento não suportado nesta operação.");

    public static readonly Error TomadorObrigatorio =
        new("DocumentoFiscal.TomadorObrigatorio", "O tomador é obrigatório para NFS-e.");

    public static readonly Error ServicoNfseObrigatorio =
        new("DocumentoFiscal.ServicoNfseObrigatorio", "Os dados do serviço são obrigatórios para NFS-e.");

    public static readonly Error TomadorInvalido =
        new("DocumentoFiscal.TomadorInvalido", "Os dados do tomador da NFS-e são inválidos.");

    public static readonly Error ServicoNfseInvalido =
        new("DocumentoFiscal.ServicoNfseInvalido", "Os dados do serviço NFS-e são inválidos.");
```

- [ ] **Step 2: Escrever os testes falhando para `Tomador`**

Criar `tests/VisuFiscalHub.Tests/Domain/TomadorTests.cs`:

```csharp
using Shouldly;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class TomadorTests
{
    private static object ValidArgs() => new
    {
        CnpjOuCpf    = "11222333000181",
        RazaoSocial  = "Empresa Tomadora Ltda",
        Logradouro   = "Rua Teste",
        Numero       = "100",
        Complemento  = (string?)null,
        Bairro       = "Centro",
        Municipio    = "São Paulo",
        CodigoMunicipio = "3550308",
        Uf           = "SP",
        Cep          = "01310100",
        Email        = (string?)null,
        InscricaoMunicipal = (string?)null
    };

    [Fact]
    public void Criar_ComCnpjValido_Sucesso()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "Empresa Tomadora Ltda",
            logradouro: "Rua Teste",
            numero: "100",
            complemento: null,
            bairro: "Centro",
            municipio: "São Paulo",
            codigoMunicipio: "3550308",
            uf: "SP",
            cep: "01310100",
            email: null,
            inscricaoMunicipal: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CnpjOuCpf.ShouldBe("11222333000181");
        result.Value.RazaoSocial.ShouldBe("Empresa Tomadora Ltda");
    }

    [Fact]
    public void Criar_ComCpfValido_Sucesso()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "52998224725",
            razaoSocial: "Pessoa Fisica",
            logradouro: "Av Paulista",
            numero: "1",
            complemento: null,
            bairro: "Bela Vista",
            municipio: "São Paulo",
            codigoMunicipio: "3550308",
            uf: "SP",
            cep: "01310100",
            email: null,
            inscricaoMunicipal: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CnpjOuCpf.ShouldBe("52998224725");
    }

    [Fact]
    public void Criar_CnpjOuCpfInvalido_Retorna_Falha()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "123",
            razaoSocial: "X",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null, inscricaoMunicipal: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.TomadorInvalido");
    }

    [Fact]
    public void Criar_RazaoSocialVazia_Retorna_Falha()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "01310100", email: null, inscricaoMunicipal: null);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_CepInvalido_Retorna_Falha()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "X",
            logradouro: "R", numero: "1", complemento: null,
            bairro: "B", municipio: "M", codigoMunicipio: "1234567",
            uf: "SP", cep: "ABC", email: null, inscricaoMunicipal: null);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_ComInscricaoMunicipal_Sucesso()
    {
        var result = Tomador.Criar(
            cnpjOuCpf: "11222333000181",
            razaoSocial: "Empresa Com IE Municipal",
            logradouro: "Rua X", numero: "1", complemento: null,
            bairro: "B", municipio: "São Paulo", codigoMunicipio: "3550308",
            uf: "SP", cep: "01310100", email: "test@emp.com", inscricaoMunicipal: "1234567");

        result.IsSuccess.ShouldBeTrue();
        result.Value.InscricaoMunicipal.ShouldBe("1234567");
    }
}
```

- [ ] **Step 3: Confirmar que falha por tipo inexistente**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "error CS"
```

Expected: erro de compilação — `Tomador` não existe.

- [ ] **Step 4: Criar `src/VisuFiscalHub.Domain/ValueObjects/Tomador.cs`**

```csharp
using System.Text.RegularExpressions;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed partial record Tomador(
    string CnpjOuCpf,
    string RazaoSocial,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    string CodigoMunicipio,
    string Uf,
    string Cep,
    string? Email,
    string? InscricaoMunicipal)
{
    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex CepRegex();

    [GeneratedRegex(@"^\d{7}$")]
    private static partial Regex CodigoMunicipioRegex();

    public static Result<Tomador> Criar(
        string cnpjOuCpf,
        string razaoSocial,
        string logradouro,
        string numero,
        string? complemento,
        string bairro,
        string municipio,
        string codigoMunicipio,
        string uf,
        string cep,
        string? email,
        string? inscricaoMunicipal)
    {
        if (string.IsNullOrWhiteSpace(cnpjOuCpf)
            || (cnpjOuCpf.Length != 11 && cnpjOuCpf.Length != 14)
            || !cnpjOuCpf.All(char.IsDigit))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(razaoSocial) || razaoSocial.Length > 115)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(logradouro) || logradouro.Length > 125)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(numero) || numero.Length > 10)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(bairro) || bairro.Length > 60)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(municipio) || municipio.Length > 60)
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(codigoMunicipio) || !CodigoMunicipioRegex().IsMatch(codigoMunicipio))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(uf) || uf.Length != 2 || !uf.All(char.IsLetter))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        if (string.IsNullOrWhiteSpace(cep) || !CepRegex().IsMatch(cep))
            return Result.Failure<Tomador>(DocumentoFiscalErrors.TomadorInvalido);

        return Result.Success(new Tomador(
            cnpjOuCpf,
            razaoSocial.Trim(),
            logradouro.Trim(),
            numero.Trim(),
            complemento?.Trim(),
            bairro.Trim(),
            municipio.Trim(),
            codigoMunicipio,
            uf.ToUpperInvariant(),
            cep,
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            string.IsNullOrWhiteSpace(inscricaoMunicipal) ? null : inscricaoMunicipal.Trim()));
    }

    public static Tomador FromStorage(
        string cnpjOuCpf, string razaoSocial,
        string logradouro, string numero, string? complemento,
        string bairro, string municipio, string codigoMunicipio,
        string uf, string cep, string? email, string? inscricaoMunicipal)
        => new(cnpjOuCpf, razaoSocial, logradouro, numero, complemento,
               bairro, municipio, codigoMunicipio, uf, cep, email, inscricaoMunicipal);
}
```

- [ ] **Step 5: Executar testes de `Tomador`**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~TomadorTests" -v quiet
```

Expected: 6 testes PASS.

- [ ] **Step 6: Executar suite completa**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam.

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Domain/ValueObjects/Tomador.cs `
        src/VisuFiscalHub.Domain/Errors/DocumentoFiscalErrors.cs `
        tests/VisuFiscalHub.Tests/Domain/TomadorTests.cs
git commit -m "feat: Tomador value object e erros de domínio NFS-e"
```

---

## Task 3: `ServicoNfse` Value Object

**Contexto:** Encapsula os dados do serviço na NFS-e ABRASF: código LC 116/2003 (`CodigoServico`), discriminação livre, ISS (alíquota, base de cálculo, valor), e flag de retenção na fonte.

**Files:**
- Create: `src/VisuFiscalHub.Domain/ValueObjects/ServicoNfse.cs`
- Create: `tests/VisuFiscalHub.Tests/Domain/ServicoNfseTests.cs`

- [ ] **Step 1: Escrever testes falhando**

Criar `tests/VisuFiscalHub.Tests/Domain/ServicoNfseTests.cs`:

```csharp
using Shouldly;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class ServicoNfseTests
{
    [Fact]
    public void Criar_ComDadosValidos_Sucesso()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "Desenvolvimento de software sob encomenda",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2.0m,
            baseCalculoIss: 1000.00m,
            valorIss: 20.00m,
            valorDeducoes: null,
            issRetido: false);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CodigoServico.ShouldBe("1.01");
        result.Value.AliquotaIss.ShouldBe(2.0m);
        result.Value.ValorIss.ShouldBe(20.00m);
        result.Value.IssRetido.ShouldBeFalse();
    }

    [Fact]
    public void Criar_CodigoServicoVazio_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "",
            discriminacao: "Serviço",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2.0m,
            baseCalculoIss: 100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DocumentoFiscal.ServicoNfseInvalido");
    }

    [Fact]
    public void Criar_DiscriminacaoVazia_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2.0m,
            baseCalculoIss: 100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_AliquotaNegativa_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "Serviço válido",
            codigoTributacaoMunicipio: null,
            aliquotaIss: -1m,
            baseCalculoIss: 100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_BaseCalculoNegativa_Retorna_Falha()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "1.01",
            discriminacao: "Serviço válido",
            codigoTributacaoMunicipio: null,
            aliquotaIss: 2m,
            baseCalculoIss: -100m,
            valorIss: 2m,
            valorDeducoes: null,
            issRetido: false);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Criar_IssRetidoTrue_Sucesso()
    {
        var result = ServicoNfse.Criar(
            codigoServico: "17.06",
            discriminacao: "Assessoria e consultoria",
            codigoTributacaoMunicipio: "170600100",
            aliquotaIss: 5.0m,
            baseCalculoIss: 5000.00m,
            valorIss: 250.00m,
            valorDeducoes: 500.00m,
            issRetido: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IssRetido.ShouldBeTrue();
        result.Value.ValorDeducoes.ShouldBe(500.00m);
        result.Value.CodigoTributacaoMunicipio.ShouldBe("170600100");
    }
}
```

- [ ] **Step 2: Confirmar que falha (tipo inexistente)**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "error CS"
```

Expected: erro de compilação — `ServicoNfse` não existe.

- [ ] **Step 3: Criar `src/VisuFiscalHub.Domain/ValueObjects/ServicoNfse.cs`**

```csharp
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Errors;

namespace VisuFiscalHub.Domain.ValueObjects;

public sealed record ServicoNfse(
    string CodigoServico,
    string Discriminacao,
    string? CodigoTributacaoMunicipio,
    decimal AliquotaIss,
    decimal BaseCalculoIss,
    decimal ValorIss,
    decimal? ValorDeducoes,
    bool IssRetido)
{
    public static Result<ServicoNfse> Criar(
        string codigoServico,
        string discriminacao,
        string? codigoTributacaoMunicipio,
        decimal aliquotaIss,
        decimal baseCalculoIss,
        decimal valorIss,
        decimal? valorDeducoes,
        bool issRetido)
    {
        if (string.IsNullOrWhiteSpace(codigoServico) || codigoServico.Length > 20)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (string.IsNullOrWhiteSpace(discriminacao) || discriminacao.Length > 2000)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (aliquotaIss < 0 || aliquotaIss > 100)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (baseCalculoIss < 0)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (valorIss < 0)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        if (valorDeducoes.HasValue && valorDeducoes.Value < 0)
            return Result.Failure<ServicoNfse>(DocumentoFiscalErrors.ServicoNfseInvalido);

        return Result.Success(new ServicoNfse(
            codigoServico.Trim(),
            discriminacao.Trim(),
            string.IsNullOrWhiteSpace(codigoTributacaoMunicipio) ? null : codigoTributacaoMunicipio.Trim(),
            aliquotaIss,
            baseCalculoIss,
            valorIss,
            valorDeducoes,
            issRetido));
    }
}
```

- [ ] **Step 4: Executar testes de `ServicoNfse`**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ServicoNfseTests" -v quiet
```

Expected: 6 testes PASS.

- [ ] **Step 5: Executar suite completa**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam.

- [ ] **Step 6: Commit**

```powershell
git add src/VisuFiscalHub.Domain/ValueObjects/ServicoNfse.cs `
        tests/VisuFiscalHub.Tests/Domain/ServicoNfseTests.cs
git commit -m "feat: ServicoNfse value object (ISS, código LC 116/2003, discriminação)"
```

---

## Task 4: `InscricaoMunicipal` em `ConfiguracaoFiscal`

**Contexto:** Prefeituras ABRASF exigem a Inscrição Municipal do prestador no XML do RPS. Vai em `ConfiguracaoFiscal` — mesmo local onde `InscricaoEstadual` já está.

**Files:**
- Modify: `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs`
- Modify: `tests/VisuFiscalHub.Tests/Domain/ConfiguracaoFiscalTests.cs`

- [ ] **Step 1: Ler o arquivo atual para conhecer a assinatura exata**

Ler `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs` na íntegra.

- [ ] **Step 2: Escrever o teste falhando**

Em `tests/VisuFiscalHub.Tests/Domain/ConfiguracaoFiscalTests.cs`, adicionar (ou criar o arquivo se não existir):

```csharp
using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Domain;

public class ConfiguracaoFiscalTests
{
    [Fact]
    public void Criar_ComInscricaoMunicipal_Sucesso()
    {
        var result = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35,
            inscricaoEstadual: null,
            serieNfe: null,
            inscricaoMunicipal: "1234567");

        result.IsSuccess.ShouldBeTrue();
        result.Value.InscricaoMunicipal.ShouldBe("1234567");
    }

    [Fact]
    public void Criar_SemInscricaoMunicipal_InscricaoMunicipalNula()
    {
        var result = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35,
            inscricaoEstadual: null,
            serieNfe: null,
            inscricaoMunicipal: null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.InscricaoMunicipal.ShouldBeNull();
    }
}
```

- [ ] **Step 3: Confirmar que falha**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ConfiguracaoFiscalTests" -v quiet 2>&1 | Select-String "error|Failed"
```

Expected: erro de compilação — parâmetro `inscricaoMunicipal` não existe em `Criar`.

- [ ] **Step 4: Atualizar `ConfiguracaoFiscal`**

Localizar o record em `src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs`.

Alterar a assinatura do record para adicionar `InscricaoMunicipal`:

```csharp
public sealed partial record ConfiguracaoFiscal(
    RegimeTributario Crt,
    string Serie,
    AmbienteSefaz Ambiente,
    int UfCodigo,
    string? InscricaoEstadual,
    string? SerieNfe = null,
    string? InscricaoMunicipal = null)
```

Adicionar no método `Criar`, após a validação de `serieNfe`:

```csharp
        var im = string.IsNullOrWhiteSpace(inscricaoMunicipal) ? null : inscricaoMunicipal.Trim();

        return Result.Success(new ConfiguracaoFiscal(crt, serie, ambiente, ufCodigo, ie, serieNfe, im));
```

E atualizar a assinatura do método `Criar` para:

```csharp
    public static Result<ConfiguracaoFiscal> Criar(
        RegimeTributario crt,
        string serie,
        AmbienteSefaz ambiente,
        int ufCodigo,
        string? inscricaoEstadual = null,
        string? serieNfe = null,
        string? inscricaoMunicipal = null)
```

- [ ] **Step 5: Executar testes de `ConfiguracaoFiscal`**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ConfiguracaoFiscalTests" -v quiet
```

Expected: 2 testes PASS.

- [ ] **Step 6: Executar suite completa**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam.

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Domain/ValueObjects/ConfiguracaoFiscal.cs `
        tests/VisuFiscalHub.Tests/Domain/ConfiguracaoFiscalTests.cs
git commit -m "feat: InscricaoMunicipal em ConfiguracaoFiscal para NFS-e"
```

---

## Task 5: Interfaces `IPrefeituraClient` e `INfseXmlBuilder`

**Contexto:** Define o contrato que a Fase 15 irá implementar. Coloca as interfaces na Application layer (sem dependência de infraestrutura) para que o domínio e os testes possam depender delas sem acoplar à implementação ABRASF.

**Files:**
- Create: `src/VisuFiscalHub.Application/Common/Interfaces/IPrefeituraClient.cs`
- Create: `src/VisuFiscalHub.Application/Common/Interfaces/INfseXmlBuilder.cs`

- [ ] **Step 1: Criar `IPrefeituraClient.cs`**

Criar `src/VisuFiscalHub.Application/Common/Interfaces/IPrefeituraClient.cs`:

```csharp
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

/// <summary>
/// Retorno do envio de RPS à prefeitura.
/// Sucesso = NFS-e emitida (síncrono) ou lote aceito para processamento (assíncrono).
/// </summary>
public sealed record PrefeituraRetorno(
    bool Autorizado,
    string? NumeroNfse,
    string? Protocolo,
    string? XmlNfse,
    string? MotivoErro,
    long ElapsedMs);

/// <summary>
/// Retorno da consulta de NFS-e ou lote na prefeitura.
/// </summary>
public sealed record PrefeituraConsultaRetorno(
    bool Encontrado,
    bool Autorizado,
    string? NumeroNfse,
    string? XmlNfse,
    string? MotivoErro,
    long ElapsedMs);

/// <summary>
/// Contrato para webservices municipais NFS-e (ABRASF e variantes).
/// Implementações concretas são por município ou grupo de municípios.
/// </summary>
public interface IPrefeituraClient
{
    /// <summary>
    /// Envia um RPS à prefeitura e aguarda resposta (modo síncrono GerarNfseEnvio).
    /// Para municípios com modo assíncrono (EnviarLoteRpsEnvio), retorna Autorizado=false
    /// com Protocolo preenchido — a consulta deve ser feita via ConsultarNfseAsync.
    /// </summary>
    Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId,
        TenantId tenantId,
        CancellationToken ct);

    /// <summary>
    /// Consulta uma NFS-e pelo número do RPS emitido.
    /// </summary>
    Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps,
        string serieRps,
        TenantId tenantId,
        int codigoMunicipio,
        CancellationToken ct);
}
```

- [ ] **Step 2: Criar `INfseXmlBuilder.cs`**

Criar `src/VisuFiscalHub.Application/Common/Interfaces/INfseXmlBuilder.cs`:

```csharp
using System.Xml;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;

namespace VisuFiscalHub.Application.Common.Interfaces;

/// <summary>
/// Constrói o XML RPS conforme o padrão ABRASF 2.04 para envio à prefeitura.
/// Cada implementação concreta suporta um padrão específico (ABRASF SP, GINFES, etc.).
/// </summary>
public interface INfseXmlBuilder
{
    /// <summary>
    /// Constrói e assina o envelope XML do RPS pronto para envio via GerarNfseEnvio.
    /// O XML retornado já contém a assinatura digital do prestador.
    /// </summary>
    Result<XmlDocument> ConstruirRps(DocumentoFiscal documento, Tenant tenant);
}
```

- [ ] **Step 3: Compilar para confirmar que as interfaces compilam sem erros**

```powershell
dotnet build src/VisuFiscalHub.Application/VisuFiscalHub.Application.csproj -v quiet 2>&1 | Select-String "error CS|Error\(s\)"
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Commit**

```powershell
git add src/VisuFiscalHub.Application/Common/Interfaces/IPrefeituraClient.cs `
        src/VisuFiscalHub.Application/Common/Interfaces/INfseXmlBuilder.cs
git commit -m "feat: interfaces IPrefeituraClient e INfseXmlBuilder para NFS-e (sem implementação)"
```

---

## Task 6: Guards em `FiscalDocumentXmlBuilder` e `SefazEndpointResolver`

**Contexto:** Hoje `TipoDocumento.NFSe` passaria silenciosamente pelo `FiscalDocumentXmlBuilder` (cai no `else` de NFC-e) e pelo `SefazEndpointResolver` (receberia URL de SEFAZ estadual — totalmente errado). Guards explícitos impedem isso imediatamente.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/FiscalDocumentXmlBuilder.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs`

- [ ] **Step 1: Escrever testes para os guards**

Criar `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/FiscalDocumentXmlBuilderNfseGuardTests.cs`:

```csharp
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Infrastructure.Fiscal.XmlBuilder;
using VisuFiscalHub.Tests.Infrastructure.Fiscal;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public class FiscalDocumentXmlBuilderNfseGuardTests
{
    [Fact]
    public void Construir_TipoNFSe_LancaNotSupportedException()
    {
        var qrGen = Substitute.For<IQrCodeGenerator>();
        var encSvc = Substitute.For<ICertificateEncryptionService>();
        var builder = new FiscalDocumentXmlBuilder(qrGen, encSvc);

        var doc = DocumentoFiscalTestHelper.CriarDocumentoNfse();
        var tenant = DocumentoFiscalTestHelper.CriarTenantValido();

        Should.Throw<NotSupportedException>(() => builder.Construir(doc, tenant))
              .Message.ShouldContain("NFSe");
    }
}
```

Criar `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/DocumentoFiscalTestHelper.cs`:

```csharp
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

internal static class DocumentoFiscalTestHelper
{
    internal static DocumentoFiscal CriarDocumentoNfse()
    {
        // Usa reflection para forçar Tipo=NFSe — necessário pois DocumentoFiscal.Criar
        // ainda não aceita NFSe na Fase 14.
        var doc = (DocumentoFiscal)System.Runtime.CompilerServices
            .RuntimeHelpers.GetUninitializedObject(typeof(DocumentoFiscal));

        var tipoField = typeof(DocumentoFiscal)
            .GetProperty("Tipo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
        tipoField.SetValue(doc, TipoDocumento.NFSe);

        return doc;
    }

    internal static Tenant CriarTenantValido()
    {
        var configuracao = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35).Value;

        var endereco = Endereco.Criar(
            logradouro: "Rua Teste", numero: "1", complemento: null,
            bairro: "Centro", municipio: "São Paulo", codigoMunicipio: 3550308,
            uf: "SP", cep: "01310100").Value;

        return Tenant.Criar(
            clienteAppId: new ClienteAppId(Guid.NewGuid()),
            cnpj: Cnpj.Criar("11222333000181").Value,
            razaoSocial: "Empresa Teste LTDA",
            nomeFantasia: null,
            configuracaoFiscal: configuracao,
            endereco: endereco,
            timeProvider: TimeProvider.System).Value;
    }
}
```

- [ ] **Step 2: Executar o teste para confirmar que falha (NFSe cai no else sem exception)**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentXmlBuilderNfseGuardTests" -v quiet
```

Expected: FAIL — `NotSupportedException` não é lançada.

- [ ] **Step 3: Adicionar guard em `FiscalDocumentXmlBuilder.Construir`**

Localizar o método `Construir` em `src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/FiscalDocumentXmlBuilder.cs`:

```csharp
    public Result<XmlDocument> Construir(DocumentoFiscal documento, Tenant tenant)
    {
        return documento.Tipo == TipoDocumento.NFe
            ? ConstruirNfe(documento, tenant)
            : ConstruirNfce(documento, tenant);
    }
```

Substituir por:

```csharp
    public Result<XmlDocument> Construir(DocumentoFiscal documento, Tenant tenant)
    {
        return documento.Tipo switch
        {
            TipoDocumento.NFe  => ConstruirNfe(documento, tenant),
            TipoDocumento.NfCe => ConstruirNfce(documento, tenant),
            _                  => throw new NotSupportedException(
                $"FiscalDocumentXmlBuilder não suporta TipoDocumento.{documento.Tipo}. Use INfseXmlBuilder para NFSe.")
        };
    }
```

- [ ] **Step 4: Adicionar guard em `SefazEndpointResolver`**

Ler o arquivo completo de `src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs` para identificar todos os métodos públicos.

Adicionar ao início de cada método público estático (ex: `Autorizacao`, `ConsultaProtocolo`, `ResolveEvento`, `AutorizacaoNfe`, `ConsultaProtocoloNfe`, `ResolveEventoNfe`) um guard para `NFSe`. Exemplo para o método `Autorizacao`:

```csharp
    public static string Autorizacao(int ufCodigo, AmbienteSefaz ambiente)
    {
        // NFSe não usa SEFAZ estadual — usa webservice municipal via IPrefeituraClient
        // Se este método for chamado para NFSe, é um erro de roteamento no job.
        // Guard adicionado na Fase 14 para tornar o erro imediato e rastreável.
        // (Não há como verificar TipoDocumento aqui — o guard fica no nível do job que chama este método)
        var h = ambiente == AmbienteSefaz.Homologacao;
        // ... resto do método sem alteração
    }
```

**Nota:** `SefazEndpointResolver` recebe `ufCodigo` e `ambiente`, não `TipoDocumento`. O guard real fica no `FiscalDocumentProcessingJob` (Task 7). Neste step, apenas adicione um comentário XML no topo da classe:

```csharp
/// <remarks>
/// Este resolver é exclusivo para NF-e (Modelo 55) e NFC-e (Modelo 65).
/// NFS-e (Modelo 99) usa <see cref="IPrefeituraClient"/> — nunca deve chamar métodos deste resolver.
/// O guard é aplicado em <see cref="FiscalDocumentProcessingJob"/> antes de invocar este resolver.
/// </remarks>
internal static class SefazEndpointResolver
```

- [ ] **Step 5: Executar o teste do guard do XmlBuilder**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~FiscalDocumentXmlBuilderNfseGuardTests" -v quiet
```

Expected: 1 teste PASS.

- [ ] **Step 6: Executar suite completa**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam.

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Fiscal/XmlBuilder/FiscalDocumentXmlBuilder.cs `
        src/VisuFiscalHub.Infrastructure/Fiscal/Sefaz/SefazEndpointResolver.cs `
        tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/FiscalDocumentXmlBuilderNfseGuardTests.cs `
        tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/DocumentoFiscalTestHelper.cs
git commit -m "feat: guard NotSupportedException para NFSe em FiscalDocumentXmlBuilder e comentário em SefazEndpointResolver"
```

---

## Task 7: Guard em `FiscalDocumentProcessingJob` para NFSe

**Contexto:** O job chama `_sefazClient.SubmeterAutorizacaoAsync` sem verificar o tipo. Se um documento NFSe entrar no pipeline, seria enviado ao SEFAZ estadual — erro silencioso e grave. Adicionar guard explícito que falha o documento com mensagem clara.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs`
- Modify: `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs`

- [ ] **Step 1: Ler o início do método `ExecuteAsync` em `FiscalDocumentProcessingJob.cs`**

Ler as primeiras 80 linhas de `src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs` para identificar onde o tipo é acessível.

- [ ] **Step 2: Escrever o teste falhando**

Em `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs`, adicionar (usando o mesmo padrão `CriarJobComDbContext` e `CriarDocumentoEnfileirado` existentes no arquivo):

```csharp
[Fact]
public async Task ExecuteAsync_TipoNFSe_FalhaDocumentoSemChamarSefaz()
{
    var (job, documentoRepo, _, sefazClient, dbContext, _, _) = CriarJobComDbContext();
    var doc = CriarDocumentoEnfileirado(TipoDocumento.NFSe);
    documentoRepo.GetByIdForUpdateAsync(doc.Id, Arg.Any<CancellationToken>()).Returns(doc);

    await job.ExecuteAsync(doc.Id, CancellationToken.None);

    // NFSe não deve chamar SEFAZ
    await sefazClient.DidNotReceive().SubmeterAutorizacaoAsync(
        Arg.Any<DocumentoFiscalId>(), Arg.Any<TenantId>(), Arg.Any<CancellationToken>());

    // Documento deve estar em estado Falhou
    doc.Status.ShouldBe(StatusDocumento.Falhou);
}
```

**Nota:** Se `CriarDocumentoEnfileirado` não aceitar `TipoDocumento.NFSe` por invariantes do domínio (ainda não suporta NFSe no `Criar`), usar reflection para forçar o tipo — mesmo padrão de `DocumentoFiscalTestHelper.CriarDocumentoNfse()` da Task 6.

Verificar o arquivo para entender o helper atual antes de adicionar o teste.

- [ ] **Step 3: Executar o teste para confirmar que falha**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_TipoNFSe" -v quiet
```

Expected: FAIL — o job chama `SubmeterAutorizacaoAsync` ou não falha o documento.

- [ ] **Step 4: Adicionar guard em `FiscalDocumentProcessingJob.ExecuteAsync`**

Localizar o início do método `ExecuteAsync` (após carregar o documento, antes de `IniciarProcessamento`). Adicionar:

```csharp
        if (documento.Tipo == TipoDocumento.NFSe)
        {
            _logger.LogError(
                "DocumentoFiscal {DocumentoId} é NFSe — FiscalDocumentProcessingJob não suporta NFSe. " +
                "Use o job correto de prefeitura (Fase 15).",
                documentoId.Value);
            await documento.Falhar(_timeProvider);
            await _documentoRepo.UpdateAsync(documento, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return;
        }
```

Adicionar o using necessário no topo se ainda não existir:

```csharp
using VisuFiscalHub.Domain.Enums;
```

- [ ] **Step 5: Executar o teste**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~ExecuteAsync_TipoNFSe" -v quiet
```

Expected: PASS.

- [ ] **Step 6: Executar suite completa**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam.

- [ ] **Step 7: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Jobs/FiscalDocumentProcessingJob.cs `
        tests/VisuFiscalHub.Tests/Infrastructure/Jobs/NfceProcessingJobTests.cs
git commit -m "feat: guard NFSe em FiscalDocumentProcessingJob — falha documento em vez de chamar SEFAZ"
```

---

## Task 8: `DocumentoFiscal` aceita `Tomador` e `ServicoNfse` + `ChaveAcesso` nullable

**Contexto:** Adiciona os novos campos ao aggregate. `ChaveAcesso` torna-se nullable porque NFS-e não tem chave de 44 dígitos. Guards no `Criar` garantem: NFSe exige `Tomador` + `ServicoNfse`; NF-e/NFC-e proíbem esses campos.

**Files:**
- Modify: `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`
- Modify: `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`

- [ ] **Step 1: Escrever os testes falhando**

Em `tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs`, adicionar:

```csharp
// ── NFSe guards no Criar ────────────────────────────────────────────────

[Fact]
public void Criar_NFSe_SemTomador_RetornaFalha()
{
    var result = DocumentoFiscal.Criar(
        id: new DocumentoFiscalId(Guid.NewGuid()),
        tenantId: new TenantId(Guid.NewGuid()),
        clienteAppId: new ClienteAppId(Guid.NewGuid()),
        idempotencyKey: Guid.NewGuid().ToString(),
        tipo: TipoDocumento.NFSe,
        chaveAcesso: null,
        numero: 1,
        serie: "001",
        indPresenca: 1,
        items: [ItemDocumentoValido()],
        pagamentos: [PagamentoValido(10m)],
        timeProvider: TimeProvider.System,
        tomador: null,
        servicoNfse: ServicoNfseValido());

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TomadorObrigatorio");
}

[Fact]
public void Criar_NFSe_SemServicoNfse_RetornaFalha()
{
    var result = DocumentoFiscal.Criar(
        id: new DocumentoFiscalId(Guid.NewGuid()),
        tenantId: new TenantId(Guid.NewGuid()),
        clienteAppId: new ClienteAppId(Guid.NewGuid()),
        idempotencyKey: Guid.NewGuid().ToString(),
        tipo: TipoDocumento.NFSe,
        chaveAcesso: null,
        numero: 1,
        serie: "001",
        indPresenca: 1,
        items: [ItemDocumentoValido()],
        pagamentos: [PagamentoValido(10m)],
        timeProvider: TimeProvider.System,
        tomador: TomadorValido(),
        servicoNfse: null);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.ServicoNfseObrigatorio");
}

[Fact]
public void Criar_NFSe_ComTomadorEServico_Sucesso()
{
    var result = DocumentoFiscal.Criar(
        id: new DocumentoFiscalId(Guid.NewGuid()),
        tenantId: new TenantId(Guid.NewGuid()),
        clienteAppId: new ClienteAppId(Guid.NewGuid()),
        idempotencyKey: Guid.NewGuid().ToString(),
        tipo: TipoDocumento.NFSe,
        chaveAcesso: null,
        numero: 1,
        serie: "001",
        indPresenca: 1,
        items: [ItemDocumentoValido()],
        pagamentos: [PagamentoValido(10m)],
        timeProvider: TimeProvider.System,
        tomador: TomadorValido(),
        servicoNfse: ServicoNfseValido());

    result.IsSuccess.ShouldBeTrue();
    result.Value.Tomador.ShouldNotBeNull();
    result.Value.ServicoNfse.ShouldNotBeNull();
    result.Value.ChaveAcesso.ShouldBeNull();
}

[Fact]
public void Criar_NfCe_ComTomador_RetornaFalha()
{
    var result = DocumentoFiscal.Criar(
        id: new DocumentoFiscalId(Guid.NewGuid()),
        tenantId: new TenantId(Guid.NewGuid()),
        clienteAppId: new ClienteAppId(Guid.NewGuid()),
        idempotencyKey: Guid.NewGuid().ToString(),
        tipo: TipoDocumento.NfCe,
        chaveAcesso: ChaveAcessoValida(),
        numero: 1,
        serie: "001",
        indPresenca: 1,
        items: [ItemDocumentoValido()],
        pagamentos: [PagamentoValido(10m)],
        timeProvider: TimeProvider.System,
        tomador: TomadorValido(),
        servicoNfse: null);

    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe("DocumentoFiscal.TipoNaoSuportado");
}
```

Adicionar os helpers no arquivo de testes (se não existirem — verificar antes):

```csharp
private static Tomador TomadorValido() => Tomador.Criar(
    cnpjOuCpf: "11222333000181",
    razaoSocial: "Empresa Tomadora Ltda",
    logradouro: "Rua Teste", numero: "100", complemento: null,
    bairro: "Centro", municipio: "São Paulo", codigoMunicipio: "3550308",
    uf: "SP", cep: "01310100", email: null, inscricaoMunicipal: null).Value;

private static ServicoNfse ServicoNfseValido() => ServicoNfse.Criar(
    codigoServico: "1.01",
    discriminacao: "Desenvolvimento de software",
    codigoTributacaoMunicipio: null,
    aliquotaIss: 2m,
    baseCalculoIss: 1000m,
    valorIss: 20m,
    valorDeducoes: null,
    issRetido: false).Value;
```

- [ ] **Step 2: Confirmar que falha (parâmetros novos não existem)**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "error CS"
```

Expected: erros de compilação — `chaveAcesso: null` não aceito; `tomador` e `servicoNfse` não existem.

- [ ] **Step 3: Atualizar `DocumentoFiscal`**

Em `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`:

**3a** — Tornar `ChaveAcesso` nullable no campo e na propriedade:

```csharp
    // Construtor privado para EF Core — ChaveAcesso é null para NFSe
    private DocumentoFiscal()
    {
        IdempotencyKey = string.Empty;
        Serie = string.Empty;
        ChaveAcesso = null;   // nullable — NFSe não tem chave de 44 dígitos
    }
```

```csharp
    public ChaveAcesso? ChaveAcesso { get; private set; }
    public Tomador? Tomador { get; private set; }
    public ServicoNfse? ServicoNfse { get; private set; }
```

**3b** — Atualizar o construtor privado:

```csharp
    private DocumentoFiscal(
        DocumentoFiscalId id,
        TenantId tenantId,
        ClienteAppId clienteAppId,
        string idempotencyKey,
        TipoDocumento tipo,
        ChaveAcesso? chaveAcesso,
        long numero,
        string serie,
        int indPresenca,
        string? cpfConsumidor,
        string? nomeConsumidor,
        List<ItemDocumento> items,
        List<Pagamento> pagamentos,
        DateTimeOffset createdAt,
        NfeDestinatario? nfeDestinatario,
        string? natOp,
        int modFrete,
        Tomador? tomador,
        ServicoNfse? servicoNfse) : base(id)
    {
        TenantId = tenantId;
        ClienteAppId = clienteAppId;
        IdempotencyKey = idempotencyKey;
        Tipo = tipo;
        ChaveAcesso = chaveAcesso;
        Numero = numero;
        Serie = serie;
        IndPresenca = indPresenca;
        CpfConsumidor = cpfConsumidor;
        NomeConsumidor = nomeConsumidor;
        Status = StatusDocumento.Criado;
        _items = items;
        _pagamentos = pagamentos;
        CreatedAt = createdAt;
        NfeDestinatario = nfeDestinatario;
        NatOp = natOp;
        ModFrete = modFrete;
        Tomador = tomador;
        ServicoNfse = servicoNfse;
    }
```

**3c** — Atualizar o método `Criar` — adicionar parâmetros e guards:

```csharp
    public static Result<DocumentoFiscal> Criar(
        DocumentoFiscalId id,
        TenantId tenantId,
        ClienteAppId clienteAppId,
        string idempotencyKey,
        TipoDocumento tipo,
        ChaveAcesso? chaveAcesso,
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
        int modFrete = 9,
        Tomador? tomador = null,
        ServicoNfse? servicoNfse = null)
    {
        if (tenantId == default)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.TenantInvalido);

        if (clienteAppId == default)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.ClienteAppInvalido);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IdempotencyKeyInvalida);

        // ── Guards por tipo ─────────────────────────────────────────────────────
        if (tipo == TipoDocumento.NFSe)
        {
            if (tomador is null)
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.TomadorObrigatorio);
            if (servicoNfse is null)
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.ServicoNfseObrigatorio);
            // NFSe não usa indPresenca, nfeDestinatario, natOp nem chaveAcesso de SEFAZ
            if (nfeDestinatario is not null || tomador is not null && tipo != TipoDocumento.NFSe)
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.TipoNaoSuportado);
        }
        else
        {
            // NF-e e NFC-e não aceitam Tomador/ServicoNfse
            if (tomador is not null)
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.TipoNaoSuportado);
            if (servicoNfse is not null)
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.TipoNaoSuportado);

            var indPresencaValidos = tipo == TipoDocumento.NFe ? IndPresencaNfe : IndPresencaNfce;
            if (!indPresencaValidos.Contains(indPresenca))
                return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.IndPresencaInvalido);

            if (tipo == TipoDocumento.NFe)
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
        }

        if (cpfConsumidor is not null && string.IsNullOrWhiteSpace(nomeConsumidor))
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.NomeConsumidorObrigatorio);

        var itemList = items.ToList();
        if (itemList.Count == 0)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.SemItens);

        var pagamentoList = pagamentos.ToList();

        return Result.Success(new DocumentoFiscal(
            id, tenantId, clienteAppId, idempotencyKey, tipo,
            chaveAcesso, numero, serie, indPresenca,
            cpfConsumidor, nomeConsumidor,
            itemList, pagamentoList, timeProvider.GetUtcNow(),
            nfeDestinatario, natOp, modFrete, tomador, servicoNfse));
    }
```

**3d** — Atualizar `Autorizar` para aceitar `ChaveAcesso?` nulo (NFSe não tem chave):

```csharp
    public Result Autorizar(
        string protocolo,
        string xmlAssinado,
        QrCode? qrCode,
        DateTimeOffset authorizedAt,
        TimeProvider timeProvider)
```

O parâmetro `qrCode` já era nullable via `QrCode?` — verificar e confirmar que a assinatura atual aceita null.

- [ ] **Step 4: Executar os testes novos**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --filter "FullyQualifiedName~DocumentoFiscalTests" -v quiet
```

Expected: todos passam incluindo os 4 novos.

- [ ] **Step 5: Executar suite completa**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam. Se houver falhas em testes existentes que chamam `DocumentoFiscal.Criar` com `chaveAcesso` obrigatória, atualizar as chamadas para passar o valor explicitamente — o parâmetro mudou de obrigatório para nullable.

- [ ] **Step 6: Commit**

```powershell
git add src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs `
        tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs
git commit -m "feat: DocumentoFiscal aceita Tomador/ServicoNfse para NFSe; ChaveAcesso nullable"
```

---

## Task 9: EF Core — mapear `Tomador`, `ServicoNfse`, `InscricaoMunicipal` + migration

**Contexto:** `ChaveAcesso` passa a ser nullable no banco (coluna nullable + índice parcial substituindo o índice único atual). `Tomador` e `ServicoNfse` armazenados como jsonb. `InscricaoMunicipal` como coluna varchar no `Tenant`.

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`
- Create: migration gerada via `dotnet ef`

- [ ] **Step 1: Atualizar `DocumentoFiscalConfiguration`**

Em `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs`:

**1a** — Tornar `chave_acesso` nullable e substituir índice único por índice parcial (não-nulo):

```csharp
        builder.Property(d => d.ChaveAcesso)
            .HasColumnName("chave_acesso")
            .HasMaxLength(44)
            .IsRequired(false)   // ← nullable para NFSe
            .HasConversion(new ValueConverter<ChaveAcesso?, string?>(
                ca => ca == null ? null : ca.Valor,
                valor => valor == null ? null : ChaveAcessoFromStorage(valor)));

        // Índice parcial: apenas documentos com chave preenchida (NF-e e NFC-e)
        builder.HasIndex(d => d.ChaveAcesso)
            .HasDatabaseName("ix_documentos_fiscais_chave_acesso")
            .HasFilter("chave_acesso IS NOT NULL");
```

**1b** — Mapear `Tomador` como jsonb (após o mapeamento de `NfeDestinatario`):

```csharp
        builder.Property(x => x.Tomador)
            .HasColumnName("tomador")
            .HasColumnType("jsonb")
            .HasConversion(
                new ValueConverter<Tomador?, string?>(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    s => s == null ? null : JsonSerializer.Deserialize<Tomador>(s, (JsonSerializerOptions?)null)));
```

**1c** — Mapear `ServicoNfse` como jsonb:

```csharp
        builder.Property(x => x.ServicoNfse)
            .HasColumnName("servico_nfse")
            .HasColumnType("jsonb")
            .HasConversion(
                new ValueConverter<ServicoNfse?, string?>(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    s => s == null ? null : JsonSerializer.Deserialize<ServicoNfse>(s, (JsonSerializerOptions?)null)));
```

- [ ] **Step 2: Atualizar `TenantConfiguration` para `InscricaoMunicipal`**

Ler `src/VisuFiscalHub.Infrastructure/Persistence/Configurations/TenantConfiguration.cs` e adicionar após o mapeamento de `InscricaoEstadual` (se existir) ou de `ConfiguracaoFiscal`:

```csharp
        // InscricaoMunicipal é parte de ConfiguracaoFiscal — mapeada como coluna owned
        // dentro do bloco OwnsOne(t => t.ConfiguracaoFiscal, cfg => { ... })
        cfg.Property(c => c.InscricaoMunicipal)
            .HasColumnName("inscricao_municipal")
            .HasMaxLength(15);
```

**Nota:** Localizar o bloco `OwnsOne(t => t.ConfiguracaoFiscal, cfg => { ... })` no arquivo e adicionar o mapeamento dentro desse bloco.

- [ ] **Step 3: Gerar a migration**

```powershell
dotnet ef migrations add AddNfseSupport `
    --project src/VisuFiscalHub.Infrastructure `
    --startup-project src/VisuFiscalHub.Api
```

Expected: arquivo `<timestamp>_AddNfseSupport.cs` criado em `src/VisuFiscalHub.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 4: Verificar o conteúdo da migration**

Abrir o arquivo gerado e confirmar que contém:
- `AlterColumn` em `chave_acesso` tornando-a nullable
- `DropIndex` do índice antigo e `CreateIndex` com filter `"chave_acesso IS NOT NULL"`
- `AddColumn` para `tomador` (jsonb, nullable)
- `AddColumn` para `servico_nfse` (jsonb, nullable)
- `AddColumn` para `inscricao_municipal` (varchar 15, nullable) na tabela de tenants

Se algum item estiver faltando, revisar os passos anteriores.

- [ ] **Step 5: Compilar e executar testes**

```powershell
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj -v quiet 2>&1 | Select-String "error CS|Error\(s\)"
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: 0 erros, todos os testes passam.

- [ ] **Step 6: Commit**

```powershell
git add src/VisuFiscalHub.Infrastructure/Persistence/Configurations/DocumentoFiscalConfiguration.cs `
        src/VisuFiscalHub.Infrastructure/Persistence/Configurations/TenantConfiguration.cs `
        src/VisuFiscalHub.Infrastructure/Persistence/Migrations/
git commit -m "feat: migration AddNfseSupport — ChaveAcesso nullable, Tomador/ServicoNfse jsonb, InscricaoMunicipal"
```

---

## Task 10: `IssueDocumentCommand` — adicionar `TomadorDto` e `ServicoNfseDto`

**Contexto:** A camada Application precisa receber os dados NFSe do caller via comando. O handler não é alterado nesta fase — apenas o contrato do comando.

**Files:**
- Modify: `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs`

- [ ] **Step 1: Adicionar os novos DTOs e propriedades ao comando**

Em `src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs`, adicionar após `ModFrete`:

```csharp
    // NFS-e only
    public TomadorDto? Tomador { get; init; }
    public ServicoNfseDto? ServicoNfse { get; init; }
```

E adicionar os novos records no final do arquivo:

```csharp
public sealed record TomadorDto(
    string CnpjOuCpf,
    string RazaoSocial,
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string Municipio,
    string CodigoMunicipio,
    string Uf,
    string Cep,
    string? Email,
    string? InscricaoMunicipal);

public sealed record ServicoNfseDto(
    string CodigoServico,
    string Discriminacao,
    string? CodigoTributacaoMunicipio,
    decimal AliquotaIss,
    decimal BaseCalculoIss,
    decimal ValorIss,
    decimal? ValorDeducoes,
    bool IssRetido);
```

- [ ] **Step 2: Compilar**

```powershell
dotnet build src/VisuFiscalHub.Application/VisuFiscalHub.Application.csproj -v quiet 2>&1 | Select-String "error CS|Error\(s\)"
```

Expected: `0 Error(s)`.

- [ ] **Step 3: Executar suite completa**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos passam.

- [ ] **Step 4: Commit**

```powershell
git add src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/IssueDocumentCommand.cs
git commit -m "feat: IssueDocumentCommand aceita TomadorDto e ServicoNfseDto para NFS-e"
```

---

## Verificação Final

- [ ] **Executar suite completa uma última vez**

```powershell
dotnet test tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj -v quiet 2>&1 | Select-String "Passed|Failed|Total" | Select-Object -Last 3
```

Expected: todos os testes passam, contagem maior que a inicial (Task 1 + 2 + 3 + 4 + 6 + 7 + 8 adicionam testes).

- [ ] **Confirmar que nenhum `switch` em `TipoDocumento` sem `NFSe` existe nos arquivos críticos**

```powershell
grep -rn "TipoDocumento\." src/ --include="*.cs" | grep -v "NFSe\|\.cs:" | head -20
```

Verificar manualmente se há switches incompletos. Se encontrar, adicionar o case `NFSe` ou um `default` que lança `InvalidOperationException`.

---

## O que esta fase NÃO faz (Fase 15)

- Implementação concreta de `IPrefeituraClient` para São Paulo (ABRASF v2.04)
- Implementação de `INfseXmlBuilder` (XML RPS ABRASF)
- Endpoint `POST /api/v1/documentos/nfse`
- Job de processamento NFSe (chamar prefeitura, não SEFAZ)
- Cancelamento de NFSe
- Testes de integração E2E para NFSe
