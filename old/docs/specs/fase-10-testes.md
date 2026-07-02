# Spec Fase 10 — Testes

**Versão:** 1.0
**Data:** 2026-05-12
**Dependências:** Fases 1 a 8 concluídas
**Critério de conclusão:** `dotnet test` passa 100% nos dois projetos; metas de cobertura enforçadas via `.runsettings`; UnitTest1.cs removido

---

## 1. Visão Geral

### Objetivo

Garantir cobertura suficiente para dar confiança no pipeline fiscal com foco em: value objects críticos (ChaveAcesso, CNPJ), state machine do DocumentoFiscal, parsers SOAP de cStat, boundary tests de cancelamento, validação XSD do XML NFC-e, e testes de integração end-to-end com banco PostgreSQL real via Testcontainers.

### Dependências diretas

| Dependência | Fase |
|---|---|
| Domain Layer completo | Fase 1 |
| Application Layer (Commands, Queries, Validators) | Fases 3, 5 |
| Infrastructure fiscal (XmlBuilder, XmlSigner, TributacaoCalculator) | Fase 6a |
| NfceAutorizacaoService com parsers | Fase 7 |
| Program.cs com `public partial class Program { }` | Fase 8 |
| Docker rodando (para Testcontainers) | Fase 9 |

### Critério de conclusão

- [ ] `dotnet test --project tests/VisuFiscalHub.Tests` passa 100%
- [ ] `dotnet test --project tests/VisuFiscalHub.IntegrationTests --filter "Category!=Smoke"` passa 100%
- [ ] Metas de cobertura enforçadas via `.runsettings` — build falha se não atingidas
- [ ] `UnitTest1.cs` removido
- [ ] `tests/Schemas/nfe_v4.00.xsd` presente e referenciado nos testes
- [ ] EF Core InMemory NUNCA usado para testes de repositório
- [ ] `FakeSefazClient` implementa `ISefazClient` completamente

---

## 2. Árvore de Arquivos

```
tests/
├── VisuFiscalHub.Tests/                       ← UNIT TESTS (renomear se necessário)
│   ├── VisuFiscalHub.Tests.csproj
│   ├── Domain/
│   │   ├── ChaveAcessoTests.cs
│   │   ├── CnpjTests.cs
│   │   ├── CpfTests.cs
│   │   ├── DocumentoFiscalTests.cs
│   │   └── QrCodeTests.cs
│   ├── Application/
│   │   ├── IssueDocumentCommandHandlerTests.cs
│   │   ├── IssueDocumentCommandValidatorTests.cs
│   │   ├── CreateTenantCommandHandlerTests.cs
│   │   └── ValidationBehaviorTests.cs
│   └── Infrastructure/
│       ├── XmlSignerTests.cs
│       ├── NfceXmlBuilderTests.cs
│       ├── CertificateEncryptionServiceTests.cs
│       ├── TributacaoCalculatorTests.cs
│       ├── ReconciliacaoJobProcessorTests.cs
│       ├── NfceAutorizacaoServiceParserTests.cs
│       ├── IBPTServiceTests.cs
│       ├── OutboxRelayJobTests.cs
│       └── WebhookDeliveryServiceTests.cs
│
├── VisuFiscalHub.IntegrationTests/            ← INTEGRATION TESTS
│   ├── VisuFiscalHub.IntegrationTests.csproj
│   ├── Infrastructure/
│   │   ├── CustomWebApplicationFactory.cs
│   │   └── FakeSefazClient.cs
│   ├── AuthFlowTests.cs
│   ├── EmissaoNfceTests.cs
│   ├── IdempotenciaTests.cs
│   ├── TenantIsolamentoTests.cs
│   ├── HealthCheckTests.cs
│   └── Smoke/
│       └── SandboxAmSmokeTests.cs
│
└── Schemas/
    ├── nfe_v4.00.xsd                          ← Schema XSD oficial NF-e 4.00
    ├── xmldsig-core-schema.xsd                ← Dependência do nfe_v4.00.xsd
    └── leiauteNFe_v4.00.xsd                   ← Schema auxiliar (se separado)
```

---

## 3. Configuração dos Projetos

### 3.1 `VisuFiscalHub.Tests.csproj`

**Packages necessários:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Shouldly" Version="4.*" />
    <PackageReference Include="NSubstitute" Version="5.*" />
    <PackageReference Include="coverlet.msbuild" Version="6.*" />
    <PackageReference Include="xunit.skippable.fact" Version="1.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\VisuFiscalHub.Domain\VisuFiscalHub.Domain.csproj" />
    <ProjectReference Include="..\..\src\VisuFiscalHub.Application\VisuFiscalHub.Application.csproj" />
    <ProjectReference Include="..\..\src\VisuFiscalHub.Infrastructure\VisuFiscalHub.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

### 3.2 `VisuFiscalHub.IntegrationTests.csproj`

**Packages necessários:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Shouldly" Version="4.*" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.*" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="3.*" />
    <PackageReference Include="coverlet.msbuild" Version="6.*" />
    <PackageReference Include="xunit.skippable.fact" Version="1.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\VisuFiscalHub.Api\VisuFiscalHub.Api.csproj" />
  </ItemGroup>
</Project>
```

### 3.3 `.runsettings` — Enforcement de Cobertura

**Localização:** `tests/.runsettings`

```xml
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="XPlat Code Coverage">
        <Configuration>
          <Format>cobertura</Format>
          <Include>
            [VisuFiscalHub.Domain]*
            [VisuFiscalHub.Application]*
            [VisuFiscalHub.Infrastructure]*
            [VisuFiscalHub.Api]*
          </Include>
          <Exclude>
            [VisuFiscalHub.Tests]*
            [VisuFiscalHub.IntegrationTests]*
          </Exclude>
          <ExcludeByAttribute>
            GeneratedCodeAttribute,
            CompilerGeneratedAttribute,
            ObsoleteAttribute
          </ExcludeByAttribute>
          <Threshold>
            <Line Assembly="VisuFiscalHub.Domain">95</Line>
            <Line Assembly="VisuFiscalHub.Application">90</Line>
            <Line Assembly="VisuFiscalHub.Infrastructure">80</Line>
            <Line Assembly="VisuFiscalHub.Api">80</Line>
          </Threshold>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
```

**Metas de cobertura:**

| Camada | Meta | Justificativa |
|---|---|---|
| Domain | 95% | Value objects e state machine são críticos |
| Application | 90% | Handlers e validators com lógica de negócio |
| Infrastructure/Fiscal (XmlBuilder, XmlSigner, TributacaoCalculator) | 85% | Subconjunto crítico dentro da meta de 80% |
| Infrastructure/Sefaz (parsers de cStat) | 80% | Parsers testáveis unitariamente com strings hardcoded |
| Api | 80% | Coberto pelos testes de integração |

---

## 4. `FakeSefazClient.cs`

**Namespace:** `VisuFiscalHub.IntegrationTests.Infrastructure`

**Localização:** `tests/VisuFiscalHub.IntegrationTests/Infrastructure/FakeSefazClient.cs`

```csharp
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Infrastructure.Fiscal.Sefaz;

namespace VisuFiscalHub.IntegrationTests.Infrastructure;

/// <summary>
/// Implementação fake de ISefazClient para testes de integração.
/// Substitui NfceAutorizacaoService nos testes — sem conectividade real com SEFAZ.
/// Configurável para simular falhas em testes específicos.
/// </summary>
public sealed class FakeSefazClient : ISefazClient
{
    private Func<SefazRetorno>? _submeterOverride;
    private Func<SefazConsultaRetorno>? _consultarOverride;

    /// <summary>
    /// Configura retorno customizado para SubmeterAutorizacaoAsync.
    /// Usado para simular cStat=110, cStat=204, timeout, etc.
    /// </summary>
    public void ConfigurarSubmeter(Func<SefazRetorno> factory) =>
        _submeterOverride = factory;

    /// <summary>
    /// Configura retorno customizado para ConsultarNfeAsync.
    /// </summary>
    public void ConfigurarConsultar(Func<SefazConsultaRetorno> factory) =>
        _consultarOverride = factory;

    /// <summary>
    /// Reseta para comportamento padrão (cStat=100, autorizado).
    /// </summary>
    public void ResetarParaPadrao()
    {
        _submeterOverride = null;
        _consultarOverride = null;
    }

    public Task<SefazRetorno> SubmeterAutorizacaoAsync(
        DocumentoFiscal documento,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        if (_submeterOverride is not null)
            return Task.FromResult(_submeterOverride());

        // Comportamento padrão: autorizado com protocolo simulado
        var nProt = $"135{DateTime.UtcNow:yyMMddHHmmss}000000001";
        var xmlAutorizado = $"<nfeProc><NFe/><protNFe><infProt><nProt>{nProt}</nProt></infProt></protNFe></nfeProc>";

        return Task.FromResult(new SefazRetorno(
            Autorizado: true,
            CStat: "100",
            XMotivo: "Autorizado o uso da NF-e",
            NProt: nProt,
            XmlAutorizado: xmlAutorizado));
    }

    public Task<SefazConsultaRetorno> ConsultarNfeAsync(
        string chaveAcesso,
        Tenant tenant,
        CancellationToken cancellationToken)
    {
        if (_consultarOverride is not null)
            return Task.FromResult(_consultarOverride());

        // Comportamento padrão: nota encontrada e autorizada
        return Task.FromResult(new SefazConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            CStat: "100",
            NProt: $"135{DateTime.UtcNow:yyMMddHHmmss}000000002",
            XmlProtocolo: "<protNFe><infProt><nProt>...</nProt></infProt></protNFe>"));
    }
}
```

---

## 5. `CustomWebApplicationFactory.cs`

**Namespace:** `VisuFiscalHub.IntegrationTests.Infrastructure`

**Localização:** `tests/VisuFiscalHub.IntegrationTests/Infrastructure/CustomWebApplicationFactory.cs`

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.IntegrationTests.Infrastructure;

/// <summary>
/// Factory de integração com:
/// - PostgreSQL real via Testcontainers (NÃO InMemory — proibido)
/// - ISefazClient substituído por FakeSefazClient
/// - Migrations aplicadas em InitializeAsync
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("visufiscalhub_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    public FakeSefazClient FakeSefaz { get; } = new FakeSefazClient();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove DbContext original e substitui pela connection string do container
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(_dbContainer.GetConnectionString()));

            // Remove ISefazClient real e substitui pelo Fake
            services.RemoveAll<ISefazClient>();
            services.AddScoped<ISefazClient>(_ => FakeSefaz);
        });

        builder.UseEnvironment("Test");
    }

    public async Task InitializeAsync()
    {
        // 1. Iniciar container PostgreSQL
        await _dbContainer.StartAsync();

        // 2. Aplicar migrations (banco real, não InMemory)
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        // 3. Seed de dados base (se necessário — criado via DbContext direto)
        // await SeedTestDataAsync(dbContext);
    }

    public new async Task DisposeAsync()
    {
        await _dbContainer.StopAsync();
        await base.DisposeAsync();
    }
}
```

**Regras críticas:**
- `services.RemoveAll<DbContextOptions<ApplicationDbContext>>()` — remove configuração original
- `services.RemoveAll<ISefazClient>()` — remove implementação real
- `await dbContext.Database.MigrateAsync()` — aplica todas as migrations no container
- EF Core InMemory NUNCA usado — não valida constraints, índices únicos, tipos PostgreSQL
- Cada classe de teste cria e destrói seu `CustomWebApplicationFactory` isolado

---

## 6. Testes de Domínio

### 6.1 `ChaveAcessoTests.cs`

**Namespace:** `VisuFiscalHub.Tests.Domain`

**Testes obrigatórios:**

```csharp
[Fact]
public void Gerar_DeveProduirStringDe44Digitos()

[Theory]
[InlineData("3509061420016714006512500100000180010000009", 7)]  // MOC 7.0 Seção 4.1.6
[InlineData(/* 43 dígitos com resto 0 */, 0)]                  // resto 0 → cDV=0
[InlineData(/* 43 dígitos com resto 1 */, 0)]                  // resto 1 → cDV=0
[InlineData(/* 43 dígitos com resto 2 */, 9)]                  // resto 2 → cDV=9
[InlineData(/* 43 dígitos com resto 9 */, 2)]                  // resto 9 → cDV=2
public void Gerar_cDV_DeveCalcularModulo11Corretamente(string quarentaTresDig, int cDVEsperado)

[Fact]
public void Gerar_QuandoCnpjComMenosDe14Digitos_DeveRetornarErro()

[Fact]
public void Gerar_QuandoCamposValidos_DeveRetornarChaveComCDVNoFinal()
```

**Invariante do teste do MOC 7.0:**
- 43 dígitos: `3509061420016714006512500100000180010000009`
- cDV esperado: `7`
- Chave completa: `35090614200167140065125001000001800100000097`
- Este teste é a prova de conformidade com a especificação técnica do SEFAZ

**Algoritmo para construir InlineData de casos de borda:**
```
Para resto=0: precisa de 43 dígitos cuja soma ponderada (pesos 2..9 ciclicamente, da direita) resulte em soma%11=0
Para resto=1: soma%11=1 → cDV=0 (mesma regra do resto=0)
Para resto=2: soma%11=2 → cDV=9 (11-2=9)
Para resto=9: soma%11=9 → cDV=2 (11-9=2)

Calcular os valores corretos com script antes de escrever o código.
Hardcodar os valores nos [InlineData] — verificar contra implementação de referência.
```

---

### 6.2 `CnpjTests.cs`

```csharp
[Fact]
public void Criar_QuandoCnpjValido_DeveRetornarSucesso()

[Theory]
[InlineData("11.222.333/0001-81")]
[InlineData("11222333000181")]
public void Criar_QuandoCnpjComMascara_DeveNormalizarEProduirMesmoValor(string cnpjComOuSemMascara)
// Assert: ambos produzem Cnpj.Valor == "11222333000181"

[Fact]
public void Criar_QuandoDigitosVerificadoresErrados_DeveRetornarErro()

[Theory]
[InlineData("00000000000000")]
[InlineData("11111111111111")]
[InlineData("99999999999999")]
public void Criar_QuandoTodosDigitosIguais_DeveRetornarErro(string cnpjRepetido)
```

---

### 6.3 `DocumentoFiscalTests.cs`

**Testes de state machine — TODOS obrigatórios:**

```csharp
// Transições válidas
[Fact] public void Enfileirar_QuandoCriado_DeveTransicionarParaEnfileirado()
[Fact] public void IniciarProcessamento_QuandoEnfileirado_DeveTransicionarParaProcessando()
[Fact] public void Autorizar_QuandoProcessando_DeveTransicionarParaAutorizado()
[Fact] public void Rejeitar_QuandoProcessando_DeveTransicionarParaRejeitado()
[Fact] public void Falhar_QuandoProcessando_DeveTransicionarParaFalhou()
[Fact] public void Denegar_QuandoProcessando_DeveTransicionarParaDenegado()

// Transições inválidas (retornam Result.Failure — NUNCA lançam exceção)
[Fact] public void Autorizar_QuandoJaAutorizado_DeveRetornarErro_NaoLancarExcecao()
[Fact] public void Rejeitar_QuandoAutorizado_DeveRetornarErro()
[Fact] public void Cancelar_QuandoCriado_DeveRetornarErro()
[Fact] public void Enfileirar_QuandoJaEnfileirado_DeveRetornarErro()

// Domain events
[Fact] public void Autorizar_DevePublicarDocumentoFiscalAutorizadoEvent()
[Fact] public void Rejeitar_DeveGravarMotivoNaPropriedade()
[Fact] public void Denegar_DevePublicarDocumentoFiscalDenegadoEvent()
[Fact] public void Falhar_DevePublicarDocumentoFiscalFalhouEvent()

// Testes de cancelamento com TimeProvider — CRÍTICOS
[Fact]
public void Cancelar_QuandoDentro30Minutos_DeveTransicionarParaCancelado()
{
    // Arrange
    var authorizedAt = DateTimeOffset.UtcNow;
    var documento = CriarDocumentoAutorizado(authorizedAt);
    var timeProvider = TimeProvider.Fixed(authorizedAt.AddMinutes(29));

    // Act
    var result = documento.Cancelar(timeProvider);

    // Assert
    result.IsSuccess.ShouldBeTrue();
    documento.Status.ShouldBe(StatusDocumento.Cancelado);
}

[Fact]
public void Cancelar_QuandoFora30Minutos_DeveRetornarErro()
{
    // Arrange
    var authorizedAt = DateTimeOffset.UtcNow;
    var documento = CriarDocumentoAutorizado(authorizedAt);
    var timeProvider = TimeProvider.Fixed(authorizedAt.AddMinutes(31));

    // Act
    var result = documento.Cancelar(timeProvider);

    // Assert
    result.IsFailure.ShouldBeTrue();
    result.Error.Code.ShouldBe(DocumentoFiscalErrors.PrazoCancelamentoExpirado.Code);
}

/// <summary>
/// BOUNDARY TEST CRÍTICO — condição é estrita (<), não menor-ou-igual (<=).
/// Um documento autorizado EXATAMENTE 30 minutos atrás NÃO pode ser cancelado.
/// </summary>
[Fact]
public void Cancelar_QuandoExatamente30Minutos_DeveRetornarErro()
{
    // Arrange
    var authorizedAt = DateTimeOffset.UtcNow;
    var documento = CriarDocumentoAutorizado(authorizedAt);

    // O TimeProvider retorna EXATAMENTE authorizedAt + 30 minutos
    var timeProvider = TimeProvider.Fixed(authorizedAt.AddMinutes(30));

    // Act
    var result = documento.Cancelar(timeProvider);

    // Assert
    // A regra é: authorizedAt + 30min < utcNow (menor ESTRITO)
    // Se utcNow == authorizedAt + 30min: NÃO pode cancelar
    result.IsFailure.ShouldBeTrue("documento autorizado há exatamente 30min não pode ser cancelado");
}
```

---

### 6.4 `QrCodeTests.cs`

```csharp
[Fact]
public void Gerar_NaoDeveConterCscNaUrl()
{
    var csc = "0123456789";
    var url = QrCode.Gerar(...);
    url.ShouldNotContain(csc);
}

/// <summary>
/// Hash SHA-1 pré-computado externamente antes de implementar o código.
/// Calcular com: echo -n "35090614200167140065125001000001800100000097|2|2|0123456789" | sha1sum
/// Hardcodar o valor hex no [InlineData].
/// </summary>
[Theory]
[InlineData(
    "35090614200167140065125001000001800100000097",  // chaveAcesso (MOC 7.0)
    2,                                               // tpAmb = Homologacao
    "0123456789",                                    // CSC sandbox AM
    "HASH_SHA1_CALCULADO_EXTERNAMENTE")]             // ← substituir pelo valor real
public void Gerar_DeveProduirUrlComHashCorreto(
    string chaveAcesso, int tpAmb, string csc, string hashEsperado)
```

**REGRA:** O hash SHA-1 no `[InlineData]` DEVE ser calculado externamente antes de escrever o código do `QrCode.Gerar`. Calculá-lo com o código que está sendo testado invalida o teste (o código poderia estar errado e o teste ainda passaria).

**Como calcular:**
```bash
# Linux/macOS
echo -n "35090614200167140065125001000001800100000097|2|2|0123456789" | sha1sum

# PowerShell
[System.BitConverter]::ToString(
  [System.Security.Cryptography.SHA1]::Create().ComputeHash(
    [System.Text.Encoding]::UTF8.GetBytes("35090614200167140065125001000001800100000097|2|2|0123456789")
  )
).Replace("-","").ToLower()
```

---

## 7. Testes de Application

### 7.1 `IssueDocumentCommandValidatorTests.cs`

```csharp
[Fact]
public void Validar_QuandoValorAcimaDe10000SemCpf_DeveRejeitar()
// Valor: 10000.01 — boundary estrito (> não >=)

[Fact]
public void Validar_QuandoValorExatamente10000SemCpf_DevePermitir()
// Valor: 10000.00 — exatamente R$10.000 SEM CPF é PERMITIDO (condição é >, não >=)

[Fact]
public void Validar_QuandoValorAcimaDe10000ComCpf_DevePermitir()
// Valor: 10000.01 com CPF — deve passar

[Fact]
public void Validar_QuandoPagamentosNaoFechamTotal_DeveRejeitar()
// Itens totalizam R$100, pagamento é R$90

[Fact]
public void Validar_QuandoNcmComMenosDe8Digitos_DeveRejeitar()
// NCM "1234567" (7 dígitos) → rejeitar
// NCM "12345678" (8 dígitos) → aceitar

[Theory]
[InlineData(2)]       // indPres=2 proibido para NFC-e (rejeição 717)
public void Validar_QuandoIndPresencaProibido_DeveRejeitar(int indPresenca)

[Theory]
[InlineData(1)]       // presencial — permitido
[InlineData(3)]       // via internet — permitido para NFC-e
[InlineData(4)]       // entrega em domicílio — permitido
[InlineData(9)]       // outros — permitido
public void Validar_QuandoIndPresencaPermitido_DeveAceitar(int indPresenca)
```

---

## 8. Testes de Infraestrutura

### 8.1 `NfceAutorizacaoServiceParserTests.cs`

**CRÍTICO:** Testes unitários puros — métodos `ParseRetornoAutorizacao` e `ParseRetornoConsulta` aceitam `string` raw. Sem mock, sem rede.

```csharp
// Strings de resposta SOAP hardcoded abaixo — baseadas no schema real do SEFAZ
private const string SoapCstat100 = @"
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"">
  <soap:Body>
    <nfeAutorizacaoLoteResult>
      <retEnviNFe versao=""4.00"" xmlns=""http://www.portalfiscal.inf.br/nfe"">
        <tpAmb>2</tpAmb>
        <cStat>103</cStat>
        <xMotivo>Lote recebido com sucesso</xMotivo>
        <protNFe><infProt>
          <cStat>100</cStat>
          <xMotivo>Autorizado o uso da NF-e</xMotivo>
          <nProt>135260000000001</nProt>
        </infProt></protNFe>
      </retEnviNFe>
    </nfeAutorizacaoLoteResult>
  </soap:Body>
</soap:Envelope>";

private const string SoapCstat110 = @"
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"">
  <soap:Body>
    <nfeAutorizacaoLoteResult>
      <retEnviNFe versao=""4.00"" xmlns=""http://www.portalfiscal.inf.br/nfe"">
        <protNFe><infProt>
          <cStat>110</cStat>
          <xMotivo>Uso Denegado. CNPJ do emitente com irregularidade na SEFAZ</xMotivo>
        </infProt></protNFe>
      </retEnviNFe>
    </nfeAutorizacaoLoteResult>
  </soap:Body>
</soap:Envelope>";

private const string SoapCstat204 = @"
<!-- Resposta de duplicidade — nota já autorizada anteriormente -->
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"">
  <soap:Body>
    <nfeAutorizacaoLoteResult>
      <retEnviNFe versao=""4.00"" xmlns=""http://www.portalfiscal.inf.br/nfe"">
        <protNFe><infProt>
          <cStat>204</cStat>
          <xMotivo>Duplicidade de NF-e</xMotivo>
        </infProt></protNFe>
      </retEnviNFe>
    </nfeAutorizacaoLoteResult>
  </soap:Body>
</soap:Envelope>";

[Fact]
public void ParsearRetorno_cStat100_DeveRetornarAutorizado()
{
    // Arrange
    var service = CriarService();

    // Act
    var retorno = service.ParseRetornoAutorizacao(SoapCstat100);

    // Assert
    retorno.Autorizado.ShouldBeTrue();
    retorno.CStat.ShouldBe("100");
    retorno.NProt.ShouldNotBeNullOrEmpty();
}

[Fact]
public void ParsearRetorno_cStat110_DeveRetornarDenegado()
{
    var service = CriarService();
    var retorno = service.ParseRetornoAutorizacao(SoapCstat110);

    retorno.Autorizado.ShouldBeFalse();
    retorno.CStat.ShouldBe("110");
    retorno.EhDenegado.ShouldBeTrue();
    // NUNCA deve ser tratado como rejeição comum
    retorno.XMotivo.ShouldContain("Denegad");
}

[Fact]
public void ParsearRetorno_cStat204_DeveRetornarDefinitivoNaoAutorizado()
{
    var service = CriarService();
    var retorno = service.ParseRetornoAutorizacao(SoapCstat204);

    retorno.Autorizado.ShouldBeFalse();
    retorno.CStat.ShouldBe("204");
    retorno.EhDuplicidade.ShouldBeTrue();
    retorno.EhDefinitivo.ShouldBeTrue(); // não retentar
}

[Theory]
[InlineData("539", "Rejeição: Duplicidade de NF-e")] // 4xx → definitivo
[InlineData("508", "Rejeição: Data de Emissão muito atrasada")]
public void ParsearRetorno_cStat4xx_DeveRetornarRejeicaoDefinitiva(string cStat, string xMotivo)

[Theory]
[InlineData("301")]  // 3xx → temporário
[InlineData("502")]  // 5xx → temporário
public void ParsearRetorno_cStat3xxOu5xx_DeveRetornarErroTemporario(string cStat)

[Fact]
public void ParsearRetorno_XmlMalformado_DeveRetornarErroSemLancarExcecao()
{
    var service = CriarService();

    // Não deve lançar exceção — retorna erro genérico
    var retorno = service.ParseRetornoAutorizacao("<xml_invalido_completamente");

    retorno.Autorizado.ShouldBeFalse();
    retorno.CStat.ShouldBe("999");
}
```

---

### 8.2 `NfceXmlBuilderTests.cs`

```csharp
[Fact]
public void Construir_XmlGerado_DevePassarValidacaoXSD_NfCe40()
{
    // Arrange
    var builder = CriarBuilder();
    var documento = CriarDocumentoFiscalValido();
    var tenant = CriarTenantValido();

    // Act
    var xmlDoc = builder.Construir(documento, tenant);

    // Assert — carrega schema XSD real
    var schemaPath = Path.Combine("Schemas", "nfe_v4.00.xsd");
    schemaPath.ShouldSatisfyAllConditions(
        () => File.Exists(schemaPath).ShouldBeTrue("nfe_v4.00.xsd deve estar em tests/Schemas/"));

    var errors = new List<string>();
    var settings = new XmlReaderSettings();
    settings.Schemas.Add("http://www.portalfiscal.inf.br/nfe",
        XmlReader.Create(schemaPath));
    settings.ValidationType = ValidationType.Schema;
    settings.ValidationEventHandler += (sender, e) =>
    {
        if (e.Severity == XmlSeverityType.Error)
            errors.Add(e.Message);
    };

    using var reader = XmlReader.Create(
        new StringReader(xmlDoc.OuterXml), settings);
    while (reader.Read()) { }

    errors.ShouldBeEmpty($"XML gerado falhou na validação XSD: {string.Join("; ", errors)}");
}

[Fact] public void Construir_ParaCrt1_DeveUsarCsosn400()
[Fact] public void Construir_ParaCrt2_DeveUsarCsosn900ComBaseCalculo()
// Assert: XmlDoc contém <ICMSSN900> com <vBC> e <pICMS> preenchidos

[Fact] public void Construir_ParaCrt3_DeveUsarCstIcmsComBaseCalculo()
[Fact] public void Construir_TotalVNF_DeveCorresponderSomatorioItens()
[Fact] public void Construir_QrCodeNaoDeveConterCsc()
[Fact] public void Construir_DeveConterProcEmiIgual3()
[Fact] public void Construir_DeveConterVerProcVisuFiscalHub1()
[Fact] public void Construir_DeveConterCIdTokenComSeisDígitos()
[Fact] public void Construir_QuandoConsumidorNulo_NaoDeveConterTagDest()
```

---

### 8.3 `TributacaoCalculatorTests.cs`

```csharp
[Fact]
public void CalcularParaCrt1_DeveProduzirCsosn400_ValorIcmsZero()
{
    // Para CRT 1 CSOSN 400: não há destaque de ICMS
    tributo.ValorIcms.ShouldBe(0m);
    tributo.BaseCalculoIcms.ShouldBe(0m);
    tributo.CsosnOuCst.ShouldBe("400");
}

[Fact]
public void CalcularParaCrt2_DeveProduirCsosn900ComAliquota12Porcento()
{
    // Produto R$ 100,00, alíquota ICMS 12%
    var produto = CriarProduto(valorUnitario: 100m, quantidade: 1m);

    var tributo = calculator.CalcularParaCrt2(produto, aliquotaIcms: 0.12m, ...);

    tributo.ValorIcms.ShouldBe(12m);
    tributo.BaseCalculoIcms.ShouldBe(100m);
    tributo.CsosnOuCst.ShouldBe("900");
}

[Fact]
public void CalcularParaCrt3_DeveProduzirCstIcmsComBaseCalculo()
{
    // Produto R$ 100,00, alíquota ICMS 12%
    var produto = CriarProduto(valorUnitario: 100m, quantidade: 1m);

    var tributo = calculator.CalcularParaCrt3(produto, aliquotaIcms: 0.12m, ...);

    tributo.ValorIcms.ShouldBe(12m);
    tributo.BaseCalculoIcms.ShouldBe(100m);
}
```

---

### 8.4 `WebhookDeliveryServiceTests.cs`

```csharp
[Fact]
public void Deliver_DeveAssinarPayloadComHmacSha256()

[Theory]
[InlineData("10.0.0.1")]          // RFC 1918 Classe A
[InlineData("192.168.1.1")]       // RFC 1918 Classe C
[InlineData("172.16.0.1")]        // RFC 1918 Classe B (172.16.0.0/12)
[InlineData("127.0.0.1")]         // loopback
[InlineData("169.254.0.1")]       // link-local (SSRF via metadata endpoint AWS/GCP)
[InlineData("::1")]               // IPv6 loopback
public void Deliver_DeveBloquearUrlRfc1918ELoopback(string ipBloqueado)
{
    // Arrange
    var webhookUrl = $"https://{ipBloqueado}/callback";
    var service = CriarService();

    // Act & Assert
    // Deve lançar SecurityException ou retornar Result.Failure
    // NUNCA deve efetuar a requisição HTTP para esses endereços
}

[Fact]
public void Deliver_NaoDeveBloquearUrlPublica()
{
    // "https://webhook.exemplo.com/callback" → deve processar
}

[Fact]
public void Deliver_DeveCalcularAssinaturaCorretamente()
{
    // Computar HMAC-SHA256 externamente e verificar que X-Hub-Signature-256 bate
    // Formato: "sha256=<hex>"
}
```

---

## 9. Testes de Integração

### 9.1 `EmissaoNfceTests.cs`

```csharp
public class EmissaoNfceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly FakeSefazClient _fakeSefaz;

    public EmissaoNfceTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _fakeSefaz = factory.FakeSefaz;
    }

    [Fact]
    public async Task PostNfce_FluxoCompleto_DeveRetornar202EEnfileirarJob()
    {
        // Arrange
        var jwt = await ObterJwtValido(_client);
        var tenantId = await ObterTenantIdSeeded(_client, jwt);
        var idempotencyKey = Guid.NewGuid().ToString();

        _fakeSefaz.ResetarParaPadrao(); // cStat=100

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/v1/documentos/nfce",
            CriarRequestNfceValido(),
            headers: new()
            {
                ["Authorization"] = $"Bearer {jwt}",
                ["X-Tenant-Id"] = tenantId,
                ["X-Idempotency-Key"] = idempotencyKey
            });

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<IssueDocumentResponse>();
        body!.Status.ShouldBe("Enfileirado");
        body.DocumentoId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task PostNfce_TenantDeOutroClienteApp_DeveRetornar403()
}
```

### 9.2 `AuthFlowTests.cs`

```csharp
[Fact]
public async Task PostToken_ComCredenciaisValidas_DeveRetornarJwtRS256()
{
    var response = await _client.PostAsync("/auth/token",
        new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", "test-client"),
            new KeyValuePair<string, string>("client_secret", "test-secret"),
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        }));

    response.StatusCode.ShouldBe(HttpStatusCode.OK);
    var body = await response.Content.ReadFromJsonAsync<TokenResponse>();
    body!.TokenType.ShouldBe("Bearer");
    // Verificar que é JWT RS256 via JwtSecurityTokenHandler
    var handler = new JwtSecurityTokenHandler();
    handler.CanReadToken(body.AccessToken).ShouldBeTrue();
    var token = handler.ReadJwtToken(body.AccessToken);
    token.Header.Alg.ShouldBe("RS256");
}

[Fact]
public async Task PostToken_ComCredenciaisInvalidas_DeveRetornar401()

[Fact]
public async Task PostToken_AcimaDoRateLimit_DeveRetornar429ComRetryAfter()
{
    // Enviar 11 requisições (limite é 10/min por IP)
    // A 11ª deve retornar 429 com header Retry-After: 60
}
```

---

## 10. Smoke Tests

### 10.1 `SandboxAmSmokeTests.cs`

```csharp
namespace VisuFiscalHub.IntegrationTests.Smoke;

/// <summary>
/// Smoke tests com conectividade real ao sandbox AM.
/// Pulados automaticamente quando SEFAZ_SANDBOX_CERT não está configurado.
/// NÃO bloqueiam o CI padrão.
/// </summary>
public class SandboxAmSmokeTests : IClassFixture<CustomWebApplicationFactory>
{
    [SkippableFact]
    public async Task EmitirNfce_SandboxAmazonas_DeveRetornarCstat100()
    {
        Skip.If(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SEFAZ_SANDBOX_CERT")),
            "Smoke test pulado: SEFAZ_SANDBOX_CERT não configurado");

        // CSC sandbox AM: "0123456789", cIdToken: "000001"
        // Usar certificado autoassinado ou sem certificado (sandbox AM aceita qualquer)
        // Verificar que o retorno final do job seja status=Autorizado
    }
}
```

---

## 11. Schema XSD — `tests/Schemas/`

Os arquivos XSD são necessários para o teste `Construir_XmlGerado_DevePassarValidacaoXSD_NfCe40()`.

**Arquivos a obter:**
- `nfe_v4.00.xsd` — schema principal NF-e 4.00
- `xmldsig-core-schema.xsd` — schema W3C de assinatura digital (dependência do nfe_v4.00.xsd)
- `leiauteNFe_v4.00.xsd` — se o schema principal importar separadamente

**Fonte:** Portal do SEFAZ — `https://www.nfe.fazenda.gov.br/portal/listaConteudo.aspx?tipoConteudo=BMPFMBoln3w=`

**Configuração como EmbeddedResource:**
```xml
<!-- No VisuFiscalHub.Tests.csproj -->
<ItemGroup>
  <None Update="Schemas\*.xsd">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

---

## 12. Checklist de Conclusão

- [ ] `UnitTest1.cs` removido do projeto de testes
- [ ] `tests/Schemas/nfe_v4.00.xsd` presente e copiado para output directory
- [ ] `Cancelar_QuandoExatamente30Minutos_DeveRetornarErro()` implementado e passando
- [ ] `Gerar_cDV_DeveCalcularModulo11Corretamente()` com InlineData MOC 7.0 (`3509061420016714006512500100000180010000097`)
- [ ] `ParsearRetorno_cStat110_DeveRetornarDenegado()` com string SOAP hardcoded
- [ ] `Deliver_DeveBloquearUrlRfc1918()` com 10.0.0.1, 192.168.1.1, 172.16.0.1, 127.0.0.1
- [ ] `CustomWebApplicationFactory` remove `DbContextOptions<ApplicationDbContext>` e substitui por Testcontainers PostgreSQL
- [ ] `CustomWebApplicationFactory` remove `ISefazClient` e substitui por `FakeSefazClient`
- [ ] `InitializeAsync` chama `dbContext.Database.MigrateAsync()`
- [ ] EF Core InMemory NÃO usado em nenhum teste de repositório
- [ ] `FakeSefazClient` implementa ambos os métodos de `ISefazClient`
- [ ] `FakeSefazClient` configurável para simular falhas (`ConfigurarSubmeter`, `ConfigurarConsultar`)
- [ ] Smoke tests com `[SkippableFact]` e `Skip.If(SEFAZ_SANDBOX_CERT is null)`
- [ ] `.runsettings` configurado com thresholds de cobertura por assembly
- [ ] `dotnet test` com `--settings tests/.runsettings` falha se cobertura não atingida
- [ ] `Construir_XmlGerado_DevePassarValidacaoXSD_NfCe40()` passa com schema XSD real
- [ ] `Gerar_DeveProduirUrlComHashCorreto()` com hash SHA-1 calculado externamente
- [ ] `Validar_QuandoValorExatamente10000SemCpf_DevePermitir()` — boundary correto (> não >=)
- [ ] `Validar_QuandoIndPresencaProibido_DeveRejeitar()` cobre `indPres=2`
