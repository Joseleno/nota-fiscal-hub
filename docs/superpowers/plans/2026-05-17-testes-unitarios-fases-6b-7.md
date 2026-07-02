# Testes Unitários — Fases 6b e 7 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar testes unitários para os componentes críticos das fases 6b e 7, cobrindo os invariantes de segurança (SSRF), lógica fiscal (cStat SEFAZ), retry de webhooks e resolução de tipos do outbox.

**Architecture:** Testes puros sem banco de dados ou rede — os componentes-alvo (`SefazRetornoParser`, `SoapEnvelopeBuilder`, `WebhookDeliveryService.IsInBlockedRange/EnqueueRetry`, `OutboxRelayJob.ResolveEventType`) são todos métodos estáticos ou lógica isolável. Para `WebhookDeliveryService` e `OutboxRelayJob` usamos NSubstitute para substituir dependências externas. Testes organizados em `tests/VisuFiscalHub.Tests/Infrastructure/` seguindo a estrutura de pastas existente.

**Tech Stack:** xUnit 2.9.3, NSubstitute 5.3.0, Shouldly 4.3.0, .NET 10

---

## File Structure

```
tests/VisuFiscalHub.Tests/Infrastructure/
  Fiscal/
    Sefaz/
      SefazRetornoParserTests.cs      — Parse(), IsDuplicidade(), IsDenegado(), IsNaoEncontrado(), IsRecuperavel()
      SoapEnvelopeBuilderTests.cs     — BuildAutorizacao() com idLote válido/inválido, estrutura XML
  Services/
    WebhookSsrfTests.cs               — IsInBlockedRange() via reflexão para todos os blocos RFC1918/IPv6
    WebhookRetryTests.cs              — EnqueueRetryIfApplicable() via DeliverInternalAsync com HTTP mock
  Jobs/
    OutboxRelayJobResolveTypeTests.cs — ResolveEventType() via reflexão: tipo válido, não-INotification, desconhecido
```

> **Nota sobre acesso a membros `internal`/`private`:** `SefazRetornoParser`, `SoapEnvelopeBuilder` e `WebhookDeliveryService` são `internal` ou têm métodos `private static`. Estratégia:
> - Para métodos `internal static` (SefazRetornoParser, SoapEnvelopeBuilder): adicionar `InternalsVisibleTo` ao projeto de infraestrutura.
> - Para métodos `private static` (IsInBlockedRange, IsInSubnet, ResolveEventType): acessar via `PrivateType` ou reflexão com `BindingFlags.NonPublic | BindingFlags.Static`.
> - Alternativa preferida quando possível: testar via comportamento público (DeliverAsync retorna sem chamar HTTP quando URL é bloqueada).

---

## Task 1: InternalsVisibleTo + estrutura de pastas

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj`
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/Sefaz/.gitkeep` (diretório)

- [ ] **Step 1: Adicionar InternalsVisibleTo ao projeto de infraestrutura**

Edite `src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj` e adicione dentro de `<Project>`:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="VisuFiscalHub.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Criar estrutura de pastas**

```powershell
New-Item -ItemType Directory -Force "tests\VisuFiscalHub.Tests\Infrastructure\Fiscal\Sefaz"
New-Item -ItemType Directory -Force "tests\VisuFiscalHub.Tests\Infrastructure\Services"
New-Item -ItemType Directory -Force "tests\VisuFiscalHub.Tests\Infrastructure\Jobs"
```

- [ ] **Step 3: Verificar que o projeto compila**

```powershell
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --no-restore -v quiet
```

Esperado: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```
git add src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj
git commit -m "chore: InternalsVisibleTo VisuFiscalHub.Tests para acesso a tipos internos"
```

---

## Task 2: SefazRetornoParserTests

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/Sefaz/SefazRetornoParserTests.cs`

Os métodos de classificação (`IsDuplicidade`, `IsDenegado`, `IsNaoEncontrado`, `IsRecuperavel`) são `public static` e podem ser chamados diretamente. O método `Parse` é `public static` mas precisa do namespace `internal` — resolvido pelo `InternalsVisibleTo` da Task 1.

- [ ] **Step 1: Criar o arquivo de testes**

```csharp
// tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/Sefaz/SefazRetornoParserTests.cs
using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal.Sefaz;

public class SefazRetornoParserTests
{
    // ── IsDuplicidade ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public void IsDuplicidade_CodigoDuplicidade_RetornaTrue(string cStat)
        => SefazRetornoParser.IsDuplicidade(cStat).ShouldBeTrue();

    [Theory]
    [InlineData("100")]
    [InlineData("110")]
    [InlineData("999")]
    [InlineData("")]
    public void IsDuplicidade_CodigoNaoDuplicidade_RetornaFalse(string cStat)
        => SefazRetornoParser.IsDuplicidade(cStat).ShouldBeFalse();

    // ── IsDenegado ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("110")]
    [InlineData("301")]
    [InlineData("302")]
    public void IsDenegado_CodigoDenegado_RetornaTrue(string cStat)
        => SefazRetornoParser.IsDenegado(cStat).ShouldBeTrue();

    [Theory]
    [InlineData("100")]
    [InlineData("204")]
    [InlineData("572")]
    [InlineData("999")]
    [InlineData("")]
    public void IsDenegado_CodigoNaoDenegado_RetornaFalse(string cStat)
        => SefazRetornoParser.IsDenegado(cStat).ShouldBeFalse();

    // ── IsNaoEncontrado ─────────────────────────────────────────────────────────

    [Fact]
    public void IsNaoEncontrado_CStat217_RetornaTrue()
        => SefazRetornoParser.IsNaoEncontrado("217").ShouldBeTrue();

    [Theory]
    [InlineData("100")]
    [InlineData("204")]
    [InlineData("110")]
    [InlineData("999")]
    [InlineData("")]
    public void IsNaoEncontrado_OutrosCodigos_RetornaFalse(string cStat)
        => SefazRetornoParser.IsNaoEncontrado(cStat).ShouldBeFalse();

    // ── IsRecuperavel ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("108")]  // 1xx genérico recuperável
    [InlineData("150")]
    [InlineData("199")]
    public void IsRecuperavel_CodigoMenor200ExcetoDenego_RetornaTrue(string cStat)
        => SefazRetornoParser.IsRecuperavel(cStat).ShouldBeTrue();

    [Theory]
    [InlineData("110")]  // denegado — mesmo sendo 1xx, não é recuperável
    public void IsRecuperavel_CStat110_RetornaFalse(string cStat)
        => SefazRetornoParser.IsRecuperavel(cStat).ShouldBeFalse();

    [Theory]
    [InlineData("200")]
    [InlineData("204")]
    [InlineData("301")]
    [InlineData("302")]
    [InlineData("400")]
    [InlineData("500")]
    [InlineData("999")]
    public void IsRecuperavel_Codigo200OuMaior_RetornaFalse(string cStat)
        => SefazRetornoParser.IsRecuperavel(cStat).ShouldBeFalse();

    [Theory]
    [InlineData("abc")]   // não-numérico — trata como transiente
    [InlineData("")]
    [InlineData("   ")]
    public void IsRecuperavel_CStatMalFormado_RetornaTrue(string cStat)
        => SefazRetornoParser.IsRecuperavel(cStat).ShouldBeTrue();

    // ── Parse — retorno de autorização ─────────────────────────────────────────

    [Fact]
    public void Parse_CStat100_RetornaAutorizadoComProtocolo()
    {
        var soap = BuildSoapAutorizacao(cStat: "100", nProt: "315230012345678", xmlProtBody: "<protNFe><infProt><nProt>315230012345678</nProt></infProt></protNFe>");

        var result = SefazRetornoParser.Parse(soap);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeTrue();
        result.Value.CStat.ShouldBe("100");
        result.Value.NProt.ShouldBe("315230012345678");
        result.Value.XmlAutorizado.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("204")]
    [InlineData("572")]
    public void Parse_CStatDuplicidade_NaoAutorizado(string cStat)
    {
        var soap = BuildSoapAutorizacao(cStat: cStat, nProt: null, xmlProtBody: null);

        var result = SefazRetornoParser.Parse(soap);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeFalse();
        result.Value.CStat.ShouldBe(cStat);
        result.Value.XmlAutorizado.ShouldBeNull();
    }

    [Theory]
    [InlineData("110")]
    [InlineData("301")]
    [InlineData("302")]
    public void Parse_CStatDenegado_NaoAutorizado(string cStat)
    {
        var soap = BuildSoapAutorizacao(cStat: cStat, nProt: null, xmlProtBody: null);

        var result = SefazRetornoParser.Parse(soap);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Autorizado.ShouldBeFalse();
    }

    [Fact]
    public void Parse_XmlInvalido_RetornaFalha()
    {
        var result = SefazRetornoParser.Parse("não é xml");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sefaz.XmlInvalido");
    }

    [Fact]
    public void Parse_SemRetEnviNFe_RetornaFalha()
    {
        var soap = "<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"><soap:Body><outro/></soap:Body></soap:Envelope>";

        var result = SefazRetornoParser.Parse(soap);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Sefaz.RetornoInvalido");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static string BuildSoapAutorizacao(string cStat, string? nProt, string? xmlProtBody)
    {
        var nProtEl    = nProt is null ? "" : $"<nfe:nProt xmlns:nfe=\"http://www.portalfiscal.inf.br/nfe\">{nProt}</nfe:nProt>";
        var protNfeEl  = xmlProtBody is null ? "" : $"<nfe:protNFe xmlns:nfe=\"http://www.portalfiscal.inf.br/nfe\">{xmlProtBody}</nfe:protNFe>";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body>
                <nfe:retEnviNFe xmlns:nfe="http://www.portalfiscal.inf.br/nfe">
                  <nfe:cStat>{cStat}</nfe:cStat>
                  <nfe:xMotivo>Motivo teste</nfe:xMotivo>
                  {nProtEl}
                  {protNfeEl}
                </nfe:retEnviNFe>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }
}
```

- [ ] **Step 2: Executar os testes para verificar que passam**

```powershell
dotnet test tests/VisuFiscalHub.Tests --filter "FullyQualifiedName~SefazRetornoParserTests" --no-build -v normal
```

Esperado: todos os testes passam (verde).

- [ ] **Step 3: Commit**

```
git add tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/Sefaz/SefazRetornoParserTests.cs
git commit -m "test: SefazRetornoParser — IsDuplicidade, IsDenegado, IsNaoEncontrado, IsRecuperavel, Parse"
```

---

## Task 3: SoapEnvelopeBuilderTests

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilderTests.cs`

`SoapEnvelopeBuilder` é `internal static` — acessível via `InternalsVisibleTo`.

- [ ] **Step 1: Criar o arquivo de testes**

```csharp
// tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilderTests.cs
using System.Xml;
using Shouldly;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal.Sefaz;

public class SoapEnvelopeBuilderTests
{
    private const string NfeNs   = "http://www.portalfiscal.inf.br/nfe";
    private const string SoapNs  = "http://www.w3.org/2003/05/soap-envelope";
    private const string WsNs    = "http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4";

    private static readonly string SampleNfeXml =
        $"<nfeProc xmlns=\"{NfeNs}\"><NFe><infNFe/></NFe></nfeProc>";

    // ── idLote válido ────────────────────────────────────────────────────────────

    [Fact]
    public void BuildAutorizacao_IdLoteValido_RetornaXmlComEstruturaSoap()
    {
        var result = SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, tpAmb: 2, idLote: "202605171200000");

        var doc = new XmlDocument();
        doc.LoadXml(result); // não deve lançar

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("soap12", SoapNs);
        ns.AddNamespace("nfe", NfeNs);
        ns.AddNamespace("ws", WsNs);

        doc.SelectSingleNode("//soap12:Envelope", ns).ShouldNotBeNull();
        doc.SelectSingleNode("//soap12:Header", ns).ShouldNotBeNull();
        doc.SelectSingleNode("//soap12:Body", ns).ShouldNotBeNull();
        doc.SelectSingleNode("//nfe:cUF", ns)!.InnerText.ShouldBe("35");
        doc.SelectSingleNode("//nfe:versaoDados", ns)!.InnerText.ShouldBe("4.00");
        doc.SelectSingleNode("//ws:nfeDadosMsg", ns).ShouldNotBeNull();
    }

    [Fact]
    public void BuildAutorizacao_IdLoteValido_EnviNFeContemIdLoteEIndSinc()
    {
        var result = SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 43, tpAmb: 1, idLote: "123456789012345");

        var doc = new XmlDocument();
        doc.LoadXml(result);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        doc.SelectSingleNode("//nfe:idLote", ns)!.InnerText.ShouldBe("123456789012345");
        doc.SelectSingleNode("//nfe:indSinc", ns)!.InnerText.ShouldBe("1"); // síncrono
    }

    [Fact]
    public void BuildAutorizacao_IdLoteValido_VersoesCorretas()
    {
        var result = SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, tpAmb: 2, idLote: "202605171200000");

        var doc = new XmlDocument();
        doc.LoadXml(result);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("nfe", NfeNs);

        var enviNfe = doc.SelectSingleNode("//nfe:enviNFe", ns);
        enviNfe.ShouldNotBeNull();
        enviNfe!.Attributes?["versao"]?.Value.ShouldBe("4.00");
    }

    // ── idLote inválido — guard TDec_015 ────────────────────────────────────────

    [Theory]
    [InlineData("12345678901234")]   // 14 dígitos — curto
    [InlineData("1234567890123456")] // 16 dígitos — longo
    [InlineData("")]                 // vazio
    [InlineData("2026051712000AB")]  // contém letras
    [InlineData("2026051712 0000")]  // contém espaço
    public void BuildAutorizacao_IdLoteInvalido_LancaArgumentException(string idLote)
    {
        var ex = Should.Throw<ArgumentException>(
            () => SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, tpAmb: 2, idLote: idLote));

        ex.ParamName.ShouldBe("idLote");
    }

    [Fact]
    public void BuildAutorizacao_IdLoteExatos15Digitos_NaoLancaExcecao()
    {
        // boundary: exatamente 15 dígitos numéricos deve ser aceito
        Should.NotThrow(() =>
            SoapEnvelopeBuilder.BuildAutorizacao(SampleNfeXml, cUF: 35, tpAmb: 2, idLote: "000000000000000"));
    }
}
```

- [ ] **Step 2: Executar os testes**

```powershell
dotnet test tests/VisuFiscalHub.Tests --filter "FullyQualifiedName~SoapEnvelopeBuilderTests" --no-build -v normal
```

Esperado: todos passam.

- [ ] **Step 3: Commit**

```
git add tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/Sefaz/SoapEnvelopeBuilderTests.cs
git commit -m "test: SoapEnvelopeBuilder — estrutura SOAP, idLote válido/inválido, versões"
```

---

## Task 4: WebhookSsrfTests — IsInBlockedRange via comportamento observável

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Services/WebhookSsrfTests.cs`

`IsInBlockedRange` e `IsInSubnet` são `private static` em `WebhookDeliveryService`. A estratégia correta é acessá-los via reflexão — os testes verificam o invariante de segurança, não detalhes de implementação. Alternativa: expor como `internal` e usar `InternalsVisibleTo`. Aqui usamos reflexão para não alterar a visibilidade de métodos que não fazem parte da API pública.

- [ ] **Step 1: Criar o arquivo de testes**

```csharp
// tests/VisuFiscalHub.Tests/Infrastructure/Services/WebhookSsrfTests.cs
using System.Net;
using System.Reflection;
using Shouldly;
using VisuFiscalHub.Infrastructure.Services;

namespace VisuFiscalHub.Tests.Infrastructure.Services;

public class WebhookSsrfTests
{
    // Obtém IsInBlockedRange via reflexão — método private static.
    private static bool IsInBlockedRange(string ip)
    {
        var method = typeof(WebhookDeliveryService)
            .GetMethod("IsInBlockedRange", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, [IPAddress.Parse(ip)])!;
    }

    // ── IPv4 bloqueados ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("10.0.0.1")]        // RFC 1918
    [InlineData("10.255.255.255")]  // RFC 1918 — limite do bloco
    [InlineData("172.16.0.1")]      // RFC 1918
    [InlineData("172.31.255.255")]  // RFC 1918 — limite do bloco /12
    [InlineData("192.168.0.1")]     // RFC 1918
    [InlineData("192.168.255.255")] // RFC 1918 — limite do bloco
    [InlineData("127.0.0.1")]       // loopback
    [InlineData("127.255.255.255")] // loopback — limite
    [InlineData("169.254.0.1")]     // link-local IPv4
    [InlineData("169.254.255.255")] // link-local — limite
    public void IsInBlockedRange_IPv4Privado_Bloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeTrue();

    [Theory]
    [InlineData("8.8.8.8")]         // Google DNS — público
    [InlineData("1.1.1.1")]         // Cloudflare — público
    [InlineData("203.0.113.1")]     // TEST-NET-3 (documentação) — não bloqueado
    [InlineData("172.15.255.255")]  // fora do bloco 172.16/12
    [InlineData("172.32.0.0")]      // fora do bloco 172.16/12
    public void IsInBlockedRange_IPv4Publico_NaoBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeFalse();

    // ── IPv6 bloqueados ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("::1")]             // loopback IPv6
    [InlineData("fc00::1")]         // ULA
    [InlineData("fd00::1")]         // ULA (fd00::/8 está dentro de fc00::/7)
    [InlineData("fdff:ffff::1")]    // ULA — limite
    [InlineData("fe80::1")]         // link-local IPv6
    [InlineData("fe80::ffff")]      // link-local IPv6
    [InlineData("febf::1")]         // link-local — limite do bloco /10
    [InlineData("ff00::1")]         // multicast
    [InlineData("ffff::1")]         // multicast — limite
    [InlineData("2002:0a00::1")]    // 6to4 encapsulando 10.0.0.0 (privado)
    [InlineData("2002:c0a8::1")]    // 6to4 encapsulando 192.168.0.0 (privado)
    public void IsInBlockedRange_IPv6Privado_Bloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeTrue();

    [Theory]
    [InlineData("2001:db8::1")]     // documentação — público
    [InlineData("2606:4700::1")]    // Cloudflare IPv6 — público
    public void IsInBlockedRange_IPv6Publico_NaoBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeFalse();

    // ── IPv4-mapped-to-IPv6 (normalização) ──────────────────────────────────────

    [Theory]
    [InlineData("::ffff:10.0.0.1")]       // mapeado para 10.0.0.1 — privado
    [InlineData("::ffff:192.168.1.1")]    // mapeado para 192.168.1.1 — privado
    [InlineData("::ffff:127.0.0.1")]      // mapeado para 127.0.0.1 — loopback
    [InlineData("::ffff:169.254.0.1")]    // mapeado para link-local
    public void IsInBlockedRange_IPv4MappedIPv6Privado_Bloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeTrue();

    [Theory]
    [InlineData("::ffff:8.8.8.8")]        // mapeado para 8.8.8.8 — público
    [InlineData("::ffff:1.1.1.1")]        // mapeado para 1.1.1.1 — público
    public void IsInBlockedRange_IPv4MappedIPv6Publico_NaoBloqueado(string ip)
        => IsInBlockedRange(ip).ShouldBeFalse();
}
```

- [ ] **Step 2: Executar os testes**

```powershell
dotnet test tests/VisuFiscalHub.Tests --filter "FullyQualifiedName~WebhookSsrfTests" --no-build -v normal
```

Esperado: todos passam.

- [ ] **Step 3: Commit**

```
git add tests/VisuFiscalHub.Tests/Infrastructure/Services/WebhookSsrfTests.cs
git commit -m "test: WebhookDeliveryService.IsInBlockedRange — IPv4/IPv6/6to4/mapped, todos os blocos SSRF"
```

---

## Task 5: WebhookRetryTests — EnqueueRetryIfApplicable

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Services/WebhookRetryTests.cs`

`EnqueueRetryIfApplicable` é `private`. Testamos o comportamento observável: quando `DeliverAsync` é chamado e o HTTP retorna falha, o `IBackgroundJobClient.Schedule` deve ser chamado com o delay correto. Usamos NSubstitute para substituir todas as dependências.

A `WebhookDeliveryService` tem dependência de `ApplicationDbContext` (concreto, não interface). Para evitar banco real, testamos apenas `EnqueueRetryIfApplicable` via reflexão — é mais simples e mais focado.

- [ ] **Step 1: Criar o arquivo de testes**

```csharp
// tests/VisuFiscalHub.Tests/Infrastructure/Services/WebhookRetryTests.cs
using System.Linq.Expressions;
using System.Reflection;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Infrastructure.Services;

namespace VisuFiscalHub.Tests.Infrastructure.Services;

public class WebhookRetryTests
{
    private readonly IBackgroundJobClient _jobClient = Substitute.For<IBackgroundJobClient>();

    // Cria instância de WebhookDeliveryService com todas as dependências substituídas.
    private WebhookDeliveryService CreateService()
        => new(
            Substitute.For<IClienteAppRepository>(),
            Substitute.For<IDocumentoFiscalRepository>(),
            Substitute.For<ICertificateEncryptionService>(),
            Substitute.For<IHttpClientFactory>(),
            _jobClient,
            null!,   // ApplicationDbContext — não usado em EnqueueRetryIfApplicable
            TimeProvider.System,
            NullLogger<WebhookDeliveryService>.Instance);

    // Invoca EnqueueRetryIfApplicable via reflexão.
    private void InvokeEnqueueRetry(WebhookDeliveryService svc, DocumentoFiscalId documentoId, ClienteAppId clienteAppId, int attemptNumber)
    {
        var method = typeof(WebhookDeliveryService)
            .GetMethod("EnqueueRetryIfApplicable", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(svc, [documentoId, clienteAppId, attemptNumber]);
    }

    // ── tentativa 1 → agenda tentativa 2 com delay 30s ──────────────────────────

    [Fact]
    public void EnqueueRetry_Tentativa1_AgendaTentativa2Com30s()
    {
        var svc        = CreateService();
        var docId      = DocumentoFiscalId.New();
        var clienteId  = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 1);

        _jobClient.Received(1).Schedule<WebhookDeliveryService>(
            Arg.Any<Expression<Action<WebhookDeliveryService>>>(),
            Arg.Is<TimeSpan>(d => d == TimeSpan.FromSeconds(30)));
    }

    // ── tentativa 2 → agenda tentativa 3 com delay 5min ─────────────────────────

    [Fact]
    public void EnqueueRetry_Tentativa2_AgendaTentativa3Com5min()
    {
        var svc        = CreateService();
        var docId      = DocumentoFiscalId.New();
        var clienteId  = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 2);

        _jobClient.Received(1).Schedule<WebhookDeliveryService>(
            Arg.Any<Expression<Action<WebhookDeliveryService>>>(),
            Arg.Is<TimeSpan>(d => d == TimeSpan.FromMinutes(5)));
    }

    // ── tentativa 3 (última) → NÃO agenda retry ──────────────────────────────────

    [Fact]
    public void EnqueueRetry_Tentativa3_NaoAgendaNovoRetry()
    {
        var svc        = CreateService();
        var docId      = DocumentoFiscalId.New();
        var clienteId  = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 3);

        _jobClient.DidNotReceiveWithAnyArgs()
            .Schedule<WebhookDeliveryService>(default!, default(TimeSpan));
    }

    // ── boundary: tentativa acima de MaxAttempts → não agenda ───────────────────

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void EnqueueRetry_TentativaAcimaDoMaximo_NaoAgendaRetry(int attemptNumber)
    {
        var svc        = CreateService();
        var docId      = DocumentoFiscalId.New();
        var clienteId  = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber);

        _jobClient.DidNotReceiveWithAnyArgs()
            .Schedule<WebhookDeliveryService>(default!, default(TimeSpan));
    }
}
```

- [ ] **Step 2: Executar os testes**

```powershell
dotnet test tests/VisuFiscalHub.Tests --filter "FullyQualifiedName~WebhookRetryTests" --no-build -v normal
```

Esperado: todos passam.

- [ ] **Step 3: Commit**

```
git add tests/VisuFiscalHub.Tests/Infrastructure/Services/WebhookRetryTests.cs
git commit -m "test: WebhookDeliveryService.EnqueueRetryIfApplicable — delays corretos, boundary MaxAttempts"
```

---

## Task 6: OutboxRelayJobResolveTypeTests

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Jobs/OutboxRelayJobResolveTypeTests.cs`

`ResolveEventType` é `private static` em `OutboxRelayJob`. Testado via reflexão. Precisamos de um tipo concreto que implemente `INotification` para o teste positivo — usamos `IDomainEvent` que já implementa `INotification` via Mediator.

- [ ] **Step 1: Criar o arquivo de testes**

```csharp
// tests/VisuFiscalHub.Tests/Infrastructure/Jobs/OutboxRelayJobResolveTypeTests.cs
using System.Reflection;
using Mediator;
using Shouldly;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Infrastructure.Jobs;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

public class OutboxRelayJobResolveTypeTests
{
    // Tipo concreto de domínio que implementa IDomainEvent (que implementa INotification).
    // Usado como tipo real para o teste positivo — garante que o assembly já está carregado.
    private static readonly string ValidEventTypeName =
        typeof(VisuFiscalHub.Domain.Events.DocumentoFiscalAutorizadoEvent).AssemblyQualifiedName!;

    private static Type? InvokeResolveEventType(string typeName)
    {
        var method = typeof(OutboxRelayJob)
            .GetMethod("ResolveEventType", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Type?)method.Invoke(null, [typeName]);
    }

    // ── tipo válido que implementa INotification ─────────────────────────────────

    [Fact]
    public void ResolveEventType_TipoValidoINotification_RetornaTipo()
    {
        var result = InvokeResolveEventType(ValidEventTypeName);

        result.ShouldNotBeNull();
        typeof(INotification).IsAssignableFrom(result).ShouldBeTrue();
    }

    // ── tipo existente que NÃO implementa INotification → retorna null ───────────

    [Fact]
    public void ResolveEventType_TipoExistenteSemINotification_RetornaNull()
    {
        // String é um tipo real, existente, mas não implementa INotification.
        var typeName = typeof(string).AssemblyQualifiedName!;

        var result = InvokeResolveEventType(typeName);

        result.ShouldBeNull();
    }

    // ── tipo desconhecido → retorna null ─────────────────────────────────────────

    [Fact]
    public void ResolveEventType_TipoDesconhecido_RetornaNull()
    {
        var result = InvokeResolveEventType("Namespace.Inexistente.TipoFantasma, AssemblyFantasma");

        result.ShouldBeNull();
    }

    // ── string vazia → retorna null (não lança) ──────────────────────────────────

    [Fact]
    public void ResolveEventType_StringVazia_RetornaNull()
    {
        var result = InvokeResolveEventType(string.Empty);

        result.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Verificar que o evento de domínio existe**

O teste usa `VisuFiscalHub.Domain.Events.DocumentoFiscalAutorizadoEvent`. Confirme que esse tipo existe:

```powershell
grep -r "DocumentoFiscalAutorizadoEvent" src/VisuFiscalHub.Domain/
```

Se o tipo tiver nome diferente, use o nome correto no campo `ValidEventTypeName`.

- [ ] **Step 3: Executar os testes**

```powershell
dotnet test tests/VisuFiscalHub.Tests --filter "FullyQualifiedName~OutboxRelayJobResolveTypeTests" --no-build -v normal
```

Esperado: todos passam.

- [ ] **Step 4: Commit**

```
git add tests/VisuFiscalHub.Tests/Infrastructure/Jobs/OutboxRelayJobResolveTypeTests.cs
git commit -m "test: OutboxRelayJob.ResolveEventType — INotification válido, sem INotification, tipo desconhecido"
```

---

## Task 7: Executar suite completa + remover placeholder

**Files:**
- Delete: `tests/VisuFiscalHub.Tests/UnitTest1.cs`

- [ ] **Step 1: Executar todos os testes**

```powershell
dotnet test tests/VisuFiscalHub.Tests --no-build -v normal
```

Esperado: todos os testes passam, 0 failures.

- [ ] **Step 2: Remover o arquivo placeholder**

```powershell
Remove-Item tests/VisuFiscalHub.Tests/UnitTest1.cs
```

- [ ] **Step 3: Executar novamente para confirmar**

```powershell
dotnet test tests/VisuFiscalHub.Tests -v normal
```

Esperado: build + todos os testes passam.

- [ ] **Step 4: Commit final**

```
git add -A tests/VisuFiscalHub.Tests/
git commit -m "test: remover placeholder UnitTest1; suite completa de testes passa"
```

---

## Self-Review

**Spec coverage:**
- ✅ SefazRetornoParser — IsDuplicidade, IsDenegado, IsNaoEncontrado, IsRecuperavel, Parse (Task 2)
- ✅ SoapEnvelopeBuilder — BuildAutorizacao idLote válido/inválido, estrutura SOAP (Task 3)
- ✅ WebhookDeliveryService — IsInBlockedRange todos os blocos RFC1918/IPv6 (Task 4)
- ✅ WebhookDeliveryService — EnqueueRetryIfApplicable delays e boundary MaxAttempts (Task 5)
- ✅ OutboxRelayJob — ResolveEventType INotification, sem INotification, desconhecido (Task 6)
- ✅ InternalsVisibleTo configurado antes dos testes (Task 1)

**Placeholder scan:** Nenhum TBD, TODO ou "similar a Task N" encontrado.

**Type consistency:**
- `DocumentoFiscalId.New()` — ✅ definido em `DocumentoFiscalId.cs:8`
- `ClienteAppId.New()` — ✅ definido em `ClienteAppId.cs:8`
- `SefazRetornoParser.IsDuplicidade/IsDenegado/IsNaoEncontrado/IsRecuperavel` — ✅ todos `public static`
- `SoapEnvelopeBuilder.BuildAutorizacao(string, int, int, string)` — ✅ assinatura atual com `idLote`
- `WebhookDeliveryService` construtor — ✅ 8 parâmetros na ordem correta
- `OutboxRelayJob.ResolveEventType` — ✅ `private static`, acessado via reflexão
