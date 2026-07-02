# Fase 15 — NFS-e ABRASF v2.04: Implementação Concreta

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar as classes concretas de emissão de NFS-e via padrão ABRASF v2.04, incluindo XML builder, HTTP client municipal, job Hangfire, endpoint API, e testes de integração do ciclo de vida completo.

**Architecture:** PrefeituraClient orquestra (igual ao SefazClient): carrega documento + tenant + certificado → NfseXmlBuilder constrói o RPS → assina com XmlSigner → PrefeituraHttpClient envia SOAP → parseia retorno. PrefeituraProcessingJob segue o mesmo padrão do FiscalDocumentProcessingJob: retry automático via Hangfire, DeliveryAttempt para cada interação, throw em falha transiente. A API expõe POST /api/v1/documentos/nfse com o mesmo padrão de idempotency key dos outros endpoints.

**Tech Stack:** ABRASF v2.04 (namespace `http://www.abrasf.org.br/nfse.xsd`), SOAP 1.2 (`GerarNfseEnvio`), XmlDocument + XmlSigner existente, Hangfire AutomaticRetry, xUnit + Shouldly para testes de integração.

---

## Mapa de Arquivos

**Criar:**
- `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/NfseXmlBuilder.cs` — INfseXmlBuilder impl
- `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraHttpClient.cs` — HTTP SOAP municipal
- `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraClient.cs` — IPrefeituraClient impl
- `src/VisuFiscalHub.Infrastructure/Jobs/PrefeituraProcessingJob.cs` — Hangfire job NFS-e
- `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/NfseXmlBuilderTests.cs` — testes unitários
- `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakePrefeituraClient.cs` — stub configurável
- `tests/VisuFiscalHub.Tests/Integration/NfseLifecycleTests.cs` — testes integração

**Modificar:**
- `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs` — registrar PrefeituraHttpClient, IPrefeituraClient, PrefeituraProcessingJob
- `src/VisuFiscalHub.Infrastructure/Jobs/DocumentJobQueue.cs` — detectar NFSe → enfileirar PrefeituraProcessingJob
- `src/VisuFiscalHub.Application/Common/Interfaces/IDocumentJobQueue.cs` — sem alteração de interface (já tem EnqueueProcessingAsync)
- `src/VisuFiscalHub.Api/Program.cs` — adicionar endpoint POST /api/v1/documentos/nfse
- `tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs` — registrar FakePrefeituraClient
- `tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs` — expor PrefeituraFake

---

## Task 1: NfseXmlBuilder — Construtor do RPS ABRASF v2.04

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/NfseXmlBuilder.cs`
- Create: `tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/NfseXmlBuilderTests.cs`

- [ ] **Step 1: Escrever o teste que falha**

```csharp
// tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/NfseXmlBuilderTests.cs
using System.Xml;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;
using VisuFiscalHub.Tests.Helpers;

namespace VisuFiscalHub.Tests.Infrastructure.Fiscal;

public sealed class NfseXmlBuilderTests
{
    private readonly NfseXmlBuilder _builder = new();

    [Fact]
    public void ConstruirRps_DocumentoNfseValido_RetornaXmlComNamespaceAbrasf()
    {
        var (doc, tenant) = CriarDocumentoNfseValido();

        var result = _builder.ConstruirRps(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var xml = result.Value;
        xml.ShouldNotBeNull();
        // Namespace ABRASF v2.04 obrigatório
        xml.OuterXml.ShouldContain("http://www.abrasf.org.br/nfse.xsd");
    }

    [Fact]
    public void ConstruirRps_DocumentoNfseValido_XmlContemCamposObrigatorios()
    {
        var (doc, tenant) = CriarDocumentoNfseValido();

        var result = _builder.ConstruirRps(doc, tenant);

        result.IsSuccess.ShouldBeTrue();
        var outerXml = result.Value.OuterXml;
        // Campos obrigatórios ABRASF
        outerXml.ShouldContain("<InfRps");
        outerXml.ShouldContain("<Servicos>");
        outerXml.ShouldContain("<Prestador>");
        outerXml.ShouldContain("<Tomador>");
        outerXml.ShouldContain("<Discriminacao>");
    }

    [Fact]
    public void ConstruirRps_DocumentoNfCe_RetornaFalha()
    {
        var (doc, tenant) = CriarDocumentoNfCeValido();

        var result = _builder.ConstruirRps(doc, tenant);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NfseXml.TipoInvalido");
    }

    [Fact]
    public void ConstruirRps_SemTomador_RetornaFalha()
    {
        var (doc, tenant) = CriarDocumentoNfseValido(semTomador: true);

        var result = _builder.ConstruirRps(doc, tenant);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NfseXml.TomadorAusente");
    }

    [Fact]
    public void ConstruirRps_SemServicoNfse_RetornaFalha()
    {
        var (doc, tenant) = CriarDocumentoNfseValido(semServico: true);

        var result = _builder.ConstruirRps(doc, tenant);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NfseXml.ServicoAusente");
    }

    [Fact]
    public void ConstruirRps_InscricaoMunicipalAusente_RetornaFalha()
    {
        var (doc, tenant) = CriarDocumentoNfseValido(semInscricaoMunicipal: true);

        var result = _builder.ConstruirRps(doc, tenant);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("NfseXml.InscricaoMunicipalAusente");
    }

    private static (DocumentoFiscal Doc, Tenant Tenant) CriarDocumentoNfseValido(
        bool semTomador = false,
        bool semServico = false,
        bool semInscricaoMunicipal = false)
    {
        var timeProvider = TimeProvider.System;
        var tenant = NfseTestHelpers.CriarTenantNfse(semInscricaoMunicipal: semInscricaoMunicipal);
        var tomador = semTomador ? null : NfseTestHelpers.TomadorValido();
        var servico = semServico ? null : NfseTestHelpers.ServicoNfseValido();
        var item = NfseTestHelpers.ItemServicoMinimo();

        // DocumentoFiscal.Criar assinatura real:
        // Criar(DocumentoFiscalId id, TenantId, ClienteAppId, string idempotencyKey, TipoDocumento,
        //       ChaveAcesso?, long numero, string serie, int indPresenca,
        //       IEnumerable<ItemDocumento> items, IEnumerable<Pagamento> pagamentos,
        //       TimeProvider, string? cpfConsumidor = null, string? nomeConsumidor = null,
        //       NfeDestinatario? = null, string? natOp = null, int modFrete = 9,
        //       Tomador? = null, ServicoNfse? = null)
        // Após Task 4b, NFSe aceita lista de itens vazia.
        var doc = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: tenant.Id,
            clienteAppId: tenant.ClienteAppId,
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NFSe,
            chaveAcesso: null,
            numero: 1L,
            serie: "001",
            indPresenca: 0,
            items: [],
            pagamentos: [],
            timeProvider: timeProvider,
            tomador: tomador,
            servicoNfse: servico).Value;

        return (doc, tenant);
    }

    private static (DocumentoFiscal Doc, Tenant Tenant) CriarDocumentoNfCeValido()
    {
        var timeProvider = TimeProvider.System;
        var tenant = NfseTestHelpers.CriarTenantNfse();

        var chaveResult = ChaveAcesso.Gerar(
            cUF: 35,
            aamm: timeProvider.GetUtcNow().ToString("yyMM"),
            cnpj: "11222333000181",
            mod: 65,
            serie: "001",
            nNF: "000000001",
            tpEmis: TipoEmissao.Normal,
            cNF: "00000001");
        var chave = chaveResult.Value;

        var produto = Produto.Criar("PROD01", "Produto", "12345678", null, "5102", "UN",
            1m, 10m, 0m, OrigemMercadoria.Nacional).Value;
        var tributo = Tributo.Criar(TipoIcms.CSOSN, 400, 0m, 0m, 0m,
            CstPisCofins.Cst07, 0m, 0m, 0m,
            CstPisCofins.Cst07, 0m, 0m, 0m).Value;
        var pagamento = Pagamento.Criar(TipoPagamento.Dinheiro, 10m).Value;

        var doc = DocumentoFiscal.Criar(
            id: new DocumentoFiscalId(Guid.NewGuid()),
            tenantId: tenant.Id,
            clienteAppId: tenant.ClienteAppId,
            idempotencyKey: Guid.NewGuid().ToString(),
            tipo: TipoDocumento.NfCe,
            chaveAcesso: chave,
            numero: 1L,
            serie: "001",
            indPresenca: 1,
            items: [new ItemDocumento(1, produto, tributo)],
            pagamentos: [pagamento],
            timeProvider: timeProvider).Value;

        return (doc, tenant);
    }
}
```

- [ ] **Step 2: Criar NfseTestHelpers no projeto de testes**

```csharp
// tests/VisuFiscalHub.Tests/Helpers/NfseTestHelpers.cs
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;

namespace VisuFiscalHub.Tests.Helpers;

public static class NfseTestHelpers
{
    public static Tenant CriarTenantNfse(bool semInscricaoMunicipal = false)
    {
        var timeProvider = TimeProvider.System;
        var clienteAppId = new ClienteAppId(Guid.NewGuid());

        var configuracao = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35,
            inscricaoMunicipal: semInscricaoMunicipal ? null : "123456789").Value;

        var endereco = Endereco.Criar(
            logradouro: "Rua Teste",
            numero: "100",
            complemento: null,
            bairro: "Centro",
            municipio: "São Paulo",
            codigoMunicipio: 3550308,
            uf: "SP",
            cep: "01310100").Value;

        return Tenant.Criar(
            clienteAppId: clienteAppId,
            cnpj: Cnpj.Criar("11222333000181").Value,
            razaoSocial: "Empresa Teste LTDA",
            nomeFantasia: null,
            configuracaoFiscal: configuracao,
            endereco: endereco,
            timeProvider: timeProvider).Value;
    }

    public static Tomador TomadorValido() => Tomador.FromStorage(
        cnpjOuCpf: "44555666000177",
        razaoSocial: "Tomador Teste LTDA",
        logradouro: "Av Paulista",
        numero: "1000",
        complemento: null,
        bairro: "Bela Vista",
        municipio: "São Paulo",
        codigoMunicipio: "3550308",
        uf: "SP",
        cep: "01310100",
        email: null,
        inscricaoMunicipal: null);

    public static ServicoNfse ServicoNfseValido() => ServicoNfse.FromStorage(
        codigoServico: "1.01",
        discriminacao: "Serviço de consultoria de TI",
        codigoTributacaoMunicipio: null,
        aliquotaIss: 2.00m,
        baseCalculoIss: 1000.00m,
        valorIss: 20.00m,
        valorDeducoes: null,
        issRetido: false);

    // DocumentoFiscal.Criar exige items.Count > 0 mesmo para NFSe.
    // Usamos um item mínimo de serviço (sem tributos de ICMS) para satisfazer o invariante.
    public static ItemDocumento ItemServicoMinimo()
    {
        var produto = Produto.Criar(
            codigoProduto: "SVC001",
            descricao: "Serviço",
            ncm: "00000000",
            cest: null,
            cfopSaida: "5301",
            unidadeComercial: "UN",
            quantidade: 1m,
            valorUnitario: 1000m,
            valorDesconto: 0m,
            origemMercadoria: OrigemMercadoria.Nacional).Value;

        var tributo = Tributo.Criar(
            tipoIcms: TipoIcms.CSOSN,
            csosnOuCst: 400,
            aliquotaIcms: 0m, baseCalculoIcms: 0m, valorIcms: 0m,
            cstPis: CstPisCofins.Cst07,
            baseCalculoPis: 0m, aliquotaPis: 0m, valorPis: 0m,
            cstCofins: CstPisCofins.Cst07,
            baseCalculoCofins: 0m, aliquotaCofins: 0m, valorCofins: 0m).Value;

        return new ItemDocumento(1, produto, tributo);
    }
}
```

- [ ] **Step 3: Executar para verificar falha**

```
dotnet test tests/VisuFiscalHub.Tests --filter "NfseXmlBuilderTests" --no-build
```

Esperado: FAIL — `NfseXmlBuilder` não existe.

- [ ] **Step 4: Implementar NfseXmlBuilder**

```csharp
// src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/NfseXmlBuilder.cs
using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Constrói o XML de RPS (Recibo Provisório de Serviço) ABRASF v2.04 para envio ao webservice municipal.
/// </summary>
internal sealed class NfseXmlBuilder : INfseXmlBuilder
{
    private const string NfseNs = "http://www.abrasf.org.br/nfse.xsd";

    public Result<XmlDocument> ConstruirRps(DocumentoFiscal documento, Domain.Entities.Tenant tenant)
    {
        if (documento.Tipo != TipoDocumento.NFSe)
            return Result.Failure<XmlDocument>(new Error("NfseXml.TipoInvalido",
                $"NfseXmlBuilder recebeu documento do tipo {documento.Tipo} — apenas NFSe é suportado."));

        if (documento.Tomador is null)
            return Result.Failure<XmlDocument>(new Error("NfseXml.TomadorAusente",
                "Tomador é obrigatório para emissão de NFS-e."));

        if (documento.ServicoNfse is null)
            return Result.Failure<XmlDocument>(new Error("NfseXml.ServicoAusente",
                "ServicoNfse é obrigatório para emissão de NFS-e."));

        if (string.IsNullOrWhiteSpace(tenant.ConfiguracaoFiscal.InscricaoMunicipal))
            return Result.Failure<XmlDocument>(new Error("NfseXml.InscricaoMunicipalAusente",
                "InscricaoMunicipal é obrigatória na ConfiguracaoFiscal do tenant para emissão de NFS-e."));

        var doc = new XmlDocument();
        var root = doc.CreateElement("GerarNfseEnvio", NfseNs);
        doc.AppendChild(root);

        var loteRps = doc.CreateElement("LoteRps", NfseNs);
        loteRps.SetAttribute("versao", "2.04");
        root.AppendChild(loteRps);

        AppendElement(doc, loteRps, "NumeroLote", documento.Id.Value.ToString("N")[..15]);
        AppendElement(doc, loteRps, "CpfCnpj", tenant.Cnpj.Valor);
        AppendElement(doc, loteRps, "InscricaoMunicipal", tenant.ConfiguracaoFiscal.InscricaoMunicipal!);
        AppendElement(doc, loteRps, "QuantidadeRps", "1");

        var listaRps = doc.CreateElement("ListaRps", NfseNs);
        loteRps.AppendChild(listaRps);

        var rps = doc.CreateElement("Rps", NfseNs);
        listaRps.AppendChild(rps);

        var infRps = doc.CreateElement("InfRps", NfseNs);
        infRps.SetAttribute("Id", $"Rps{documento.Id.Value:N}");
        rps.AppendChild(infRps);

        // Identificação do RPS
        var identificacaoRps = doc.CreateElement("IdentificacaoRps", NfseNs);
        infRps.AppendChild(identificacaoRps);
        AppendElement(doc, identificacaoRps, "Numero", "1");
        AppendElement(doc, identificacaoRps, "Serie", tenant.ConfiguracaoFiscal.Serie);
        AppendElement(doc, identificacaoRps, "Tipo", "1"); // 1=RPS

        AppendElement(doc, infRps, "DataEmissao", documento.CriadoEm.ToString("yyyy-MM-ddTHH:mm:ss"));
        AppendElement(doc, infRps, "NaturezaOperacao", "1"); // 1=Tributação no município
        AppendElement(doc, infRps, "OptanteSimplesNacional",
            tenant.ConfiguracaoFiscal.Crt == RegimeTributario.SimplesNacional ? "1" : "2");
        AppendElement(doc, infRps, "IncentivadoCultural", "2"); // 2=Não
        AppendElement(doc, infRps, "Status", "1"); // 1=Normal

        // Serviços
        var servicos = doc.CreateElement("Servicos", NfseNs);
        infRps.AppendChild(servicos);

        var servico = doc.CreateElement("Servico", NfseNs);
        servicos.AppendChild(servico);

        var valores = doc.CreateElement("Valores", NfseNs);
        servico.AppendChild(valores);

        var svc = documento.ServicoNfse;
        AppendElement(doc, valores, "ValorServicos", FormatDecimal(svc.BaseCalculoIss));
        if (svc.ValorDeducoes.HasValue)
            AppendElement(doc, valores, "ValorDeducoes", FormatDecimal(svc.ValorDeducoes.Value));
        AppendElement(doc, valores, "ValorIss", FormatDecimal(svc.ValorIss));
        AppendElement(doc, valores, "Aliquota", FormatDecimal(svc.AliquotaIss));
        AppendElement(doc, valores, "BaseCalculo", FormatDecimal(svc.BaseCalculoIss));
        AppendElement(doc, valores, "IssRetido", svc.IssRetido ? "1" : "2");

        AppendElement(doc, servico, "ItemListaServico", svc.CodigoServico);
        if (!string.IsNullOrWhiteSpace(svc.CodigoTributacaoMunicipio))
            AppendElement(doc, servico, "CodigoTributacaoMunicipio", svc.CodigoTributacaoMunicipio);
        AppendElement(doc, servico, "Discriminacao", svc.Discriminacao);
        AppendElement(doc, servico, "CodigoMunicipio",
            tenant.Endereco.CodigoMunicipio.ToString());

        // Prestador
        var prestador = doc.CreateElement("Prestador", NfseNs);
        infRps.AppendChild(prestador);

        var prestadorCpfCnpj = doc.CreateElement("CpfCnpj", NfseNs);
        prestador.AppendChild(prestadorCpfCnpj);
        AppendElement(doc, prestadorCpfCnpj, "Cnpj", tenant.Cnpj.Valor);
        AppendElement(doc, prestador, "InscricaoMunicipal", tenant.ConfiguracaoFiscal.InscricaoMunicipal!);

        // Tomador
        var tom = documento.Tomador;
        var tomadorEl = doc.CreateElement("Tomador", NfseNs);
        infRps.AppendChild(tomadorEl);

        var tomadorIdentificacao = doc.CreateElement("IdentificacaoTomador", NfseNs);
        tomadorEl.AppendChild(tomadorIdentificacao);

        var tomadorCpfCnpj = doc.CreateElement("CpfCnpj", NfseNs);
        tomadorIdentificacao.AppendChild(tomadorCpfCnpj);
        if (tom.CnpjOuCpf.Length == 14)
            AppendElement(doc, tomadorCpfCnpj, "Cnpj", tom.CnpjOuCpf);
        else
            AppendElement(doc, tomadorCpfCnpj, "Cpf", tom.CnpjOuCpf);

        if (!string.IsNullOrWhiteSpace(tom.InscricaoMunicipal))
            AppendElement(doc, tomadorIdentificacao, "InscricaoMunicipal", tom.InscricaoMunicipal);

        AppendElement(doc, tomadorEl, "RazaoSocial", tom.RazaoSocial);

        var tomadorEndereco = doc.CreateElement("Endereco", NfseNs);
        tomadorEl.AppendChild(tomadorEndereco);
        AppendElement(doc, tomadorEndereco, "Endereco", tom.Logradouro);
        AppendElement(doc, tomadorEndereco, "Numero", tom.Numero);
        if (!string.IsNullOrWhiteSpace(tom.Complemento))
            AppendElement(doc, tomadorEndereco, "Complemento", tom.Complemento);
        AppendElement(doc, tomadorEndereco, "Bairro", tom.Bairro);
        AppendElement(doc, tomadorEndereco, "CodigoMunicipio", tom.CodigoMunicipio);
        AppendElement(doc, tomadorEndereco, "Uf", tom.Uf);
        AppendElement(doc, tomadorEndereco, "Cep", tom.Cep);

        if (!string.IsNullOrWhiteSpace(tom.Email))
        {
            var tomadorContato = doc.CreateElement("Contato", NfseNs);
            tomadorEl.AppendChild(tomadorContato);
            AppendElement(doc, tomadorContato, "Email", tom.Email);
        }

        return Result.Success(doc);
    }

    private static XmlElement AppendElement(XmlDocument doc, XmlElement parent, string name, string value)
    {
        var el = doc.CreateElement(name, NfseNs);
        el.InnerText = value;
        parent.AppendChild(el);
        return el;
    }

    private static string FormatDecimal(decimal value) => value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests --filter "NfseXmlBuilderTests" --no-build
```

Esperado: 5/5 PASS.

- [ ] **Step 6: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/NfseXmlBuilder.cs tests/VisuFiscalHub.Tests/Infrastructure/Fiscal/NfseXmlBuilderTests.cs tests/VisuFiscalHub.Tests/Helpers/NfseTestHelpers.cs
git commit -m "feat: NfseXmlBuilder ABRASF v2.04 com testes unitários"
```

---

## Task 2: PrefeituraHttpClient — HTTP SOAP Municipal

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraHttpClient.cs`

> Nota: Prefeituras ABRASF geralmente NÃO exigem mTLS (diferente da SEFAZ federal). Usam HTTP simples com certificado digital apenas para assinatura XML. Algumas prefeituras exigem HTTPS com certificado do servidor, mas não client certificate. Se necessário, mTLS pode ser habilitado via overload futuro.

- [ ] **Step 1: Não há testes unitários para o HTTP client** (depende de infraestrutura de rede). O teste de integração em Task 7 cobre o comportamento via FakePrefeituraClient. Implementar diretamente.

- [ ] **Step 2: Implementar PrefeituraHttpClient**

```csharp
// src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraHttpClient.cs
using System.Net.Http.Headers;
using System.Text;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Cliente HTTP para os webservices SOAP das prefeituras (padrão ABRASF v2.04).
/// Prefeituras geralmente não exigem mTLS — apenas HTTPS com assinatura XML.
/// </summary>
internal sealed class PrefeituraHttpClient
{
    private const string UserAgent = "VisuFiscalHub/1.0 NfseEmissor";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    public async Task<string> PostSoapAsync(
        string url,
        string soapEnvelope,
        CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler, disposeHandler: false);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        using var content = new StringContent(soapEnvelope, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml")
        {
            CharSet = "utf-8"
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RequestTimeout);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(url, content, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Timeout de {RequestTimeout.TotalSeconds}s atingido para {url}.");
        }

        // Prefeituras retornam HTTP 500 para erros SOAP — não usar EnsureSuccessStatusCode.
        return await response.Content.ReadAsStringAsync(ct);
    }
}
```

- [ ] **Step 3: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraHttpClient.cs
git commit -m "feat: PrefeituraHttpClient SOAP HTTP municipal"
```

---

## Task 3: PrefeituraClient — Orquestrador IPrefeituraClient

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraClient.cs`
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraEndpointResolver.cs`
- Create: `src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraRetornoParser.cs`

> **Nota sobre assinatura XML**: O `XmlSigner` existente exige `X509Certificate2` (mTLS SEFAZ). Prefeituras ABRASF utilizam assinatura digital via certificado A1/A3 **inserido no XML** (não mTLS de transporte). A assinatura digital do RPS (Fase 16) requer carregar o certificado do tenant via `ITenantCertificateProvider`. Para Fase 15, o `PrefeituraClient` enviará o XML **sem assinatura embutida** — aceitável em homologação e suficiente para validar o pipeline completo.

- [ ] **Step 1: Implementar PrefeituraEndpointResolver**

Prefeituras têm endpoints variáveis por município. Implementar com suporte ao endpoint de referência da ABRASF (Betha, IPM, Governa). Em produção, adicionar por município conforme integração.

```csharp
// src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraEndpointResolver.cs
using VisuFiscalHub.Domain.Enums;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Resolve URLs dos webservices das prefeituras por código IBGE do município.
/// Endpoints variam por fornecedor de sistema (Betha, IPM, Governa, NFS-e Nacional).
/// Em homologação, todas as prefeituras sem mapeamento retornam null (não integradas).
/// </summary>
internal static class PrefeituraEndpointResolver
{
    // Mapeamento inicial: código IBGE → URL do webservice GerarNfse
    // Adicionar prefeituras conforme integração com cada sistema municipal.
    private static readonly Dictionary<int, (string Homologacao, string Producao)> Endpoints = new()
    {
        // São Paulo (SP) - NFS-e São Paulo (https://nfe.prefeitura.sp.gov.br)
        [3550308] = (
            "https://nfe.prefeitura.sp.gov.br/ws/lotenfe.asmx",
            "https://nfe.prefeitura.sp.gov.br/ws/lotenfe.asmx"),
    };

    public static string? ResolverGerarNfse(int codigoMunicipio, AmbienteSefaz ambiente)
    {
        if (!Endpoints.TryGetValue(codigoMunicipio, out var pair))
            return null;

        return ambiente == AmbienteSefaz.Producao ? pair.Producao : pair.Homologacao;
    }

    public static string? ResolverConsultarNfse(int codigoMunicipio, AmbienteSefaz ambiente)
    {
        // Por padrão, consulta usa o mesmo endpoint de emissão (GerarNfse/ConsultarNfse são no mesmo WS).
        return ResolverGerarNfse(codigoMunicipio, ambiente);
    }
}
```

- [ ] **Step 2: Implementar PrefeituraRetornoParser**

```csharp
// src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraRetornoParser.cs
using System.Xml;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

internal static class PrefeituraRetornoParser
{
    private const string NfseNs = "http://www.abrasf.org.br/nfse.xsd";

    public static Result<PrefeituraRetorno> ParseGerarNfse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfse", NfseNs);

            // Verificar erros ABRASF
            var listaErros = doc.SelectSingleNode("//nfse:ListaMensagemRetorno", ns);
            if (listaErros is not null)
            {
                var mensagem = listaErros.SelectSingleNode(".//nfse:Mensagem", ns)?.InnerText ?? "Erro desconhecido";
                var codigo = listaErros.SelectSingleNode(".//nfse:Codigo", ns)?.InnerText ?? "E99";
                return Result.Success(new PrefeituraRetorno(
                    Autorizado: false,
                    NumeroNfse: null,
                    Protocolo: null,
                    XmlNfse: null,
                    MotivoErro: $"[{codigo}] {mensagem}",
                    ElapsedMs: 0L));
            }

            // Sucesso: extrai número da NFS-e
            var nfseNode = doc.SelectSingleNode("//nfse:CompNfse/nfse:Nfse/nfse:InfNfse", ns);
            if (nfseNode is null)
                return Result.Failure<PrefeituraRetorno>(
                    new Error("Prefeitura.RetornoInvalido", "Retorno ABRASF sem CompNfse/InfNfse."));

            var numero = nfseNode.SelectSingleNode("nfse:Numero", ns)?.InnerText;
            var xmlNfse = doc.SelectSingleNode("//nfse:CompNfse", ns)?.OuterXml;

            return Result.Success(new PrefeituraRetorno(
                Autorizado: !string.IsNullOrWhiteSpace(numero),
                NumeroNfse: numero,
                Protocolo: numero,
                XmlNfse: xmlNfse,
                MotivoErro: null,
                ElapsedMs: 0L));
        }
        catch (XmlException ex)
        {
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.XmlInvalido", $"Falha ao parsear retorno ABRASF: {ex.Message}"));
        }
    }

    public static Result<PrefeituraConsultaRetorno> ParseConsultarNfse(string soapResponse)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(soapResponse);

            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("nfse", NfseNs);

            var listaErros = doc.SelectSingleNode("//nfse:ListaMensagemRetorno", ns);
            if (listaErros is not null)
            {
                // Código E10 = "Nota Fiscal não encontrada"
                var codigo = listaErros.SelectSingleNode(".//nfse:Codigo", ns)?.InnerText ?? "E99";
                var mensagem = listaErros.SelectSingleNode(".//nfse:Mensagem", ns)?.InnerText ?? "Erro";
                var naoEncontrado = codigo is "E10" or "E4";
                return Result.Success(new PrefeituraConsultaRetorno(
                    Encontrado: !naoEncontrado,
                    Autorizado: false,
                    NumeroNfse: null,
                    XmlNfse: null,
                    MotivoErro: $"[{codigo}] {mensagem}",
                    ElapsedMs: 0L));
            }

            var nfseNode = doc.SelectSingleNode("//nfse:CompNfse/nfse:Nfse/nfse:InfNfse", ns);
            var numero = nfseNode?.SelectSingleNode("nfse:Numero", ns)?.InnerText;
            var xmlNfse = doc.SelectSingleNode("//nfse:CompNfse", ns)?.OuterXml;

            return Result.Success(new PrefeituraConsultaRetorno(
                Encontrado: nfseNode is not null,
                Autorizado: !string.IsNullOrWhiteSpace(numero),
                NumeroNfse: numero,
                XmlNfse: xmlNfse,
                MotivoErro: null,
                ElapsedMs: 0L));
        }
        catch (XmlException ex)
        {
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.ConsultaXmlInvalido", $"Falha ao parsear consulta ABRASF: {ex.Message}"));
        }
    }

    private static string BuildConsultaEnvelope(string numeroRps, string serieRps, string cnpj,
        string inscricaoMunicipal, int codigoMunicipio)
    {
        const string NfseWsNs = "http://www.abrasf.org.br/nfse.xsd";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body>
                <ConsultarNfsePorRpsEnvio xmlns="{NfseWsNs}">
                  <IdentificacaoRps>
                    <Numero>{numeroRps}</Numero>
                    <Serie>{serieRps}</Serie>
                    <Tipo>1</Tipo>
                  </IdentificacaoRps>
                  <Prestador>
                    <CpfCnpj>
                      <Cnpj>{cnpj}</Cnpj>
                    </CpfCnpj>
                    <InscricaoMunicipal>{inscricaoMunicipal}</InscricaoMunicipal>
                  </Prestador>
                </ConsultarNfsePorRpsEnvio>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }
}
```

- [ ] **Step 3: Implementar PrefeituraClient**

```csharp
// src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/PrefeituraClient.cs
using System.Xml;
using Microsoft.Extensions.Logging;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;

namespace VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;

/// <summary>
/// Implementação real de IPrefeituraClient para NFS-e ABRASF v2.04.
/// Orquestra: carregar documento → carregar tenant → build RPS XML → enviar → parsear.
/// Assinatura digital do RPS via certificado A1/A3 será adicionada na Fase 16.
/// Em homologação, a maioria das prefeituras aceita RPS sem assinatura embutida.
/// </summary>
internal sealed class PrefeituraClient : IPrefeituraClient
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly ITenantRepository _tenantRepo;
    private readonly INfseXmlBuilder _xmlBuilder;
    private readonly PrefeituraHttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrefeituraClient> _logger;

    public PrefeituraClient(
        IDocumentoFiscalRepository documentoRepo,
        ITenantRepository tenantRepo,
        INfseXmlBuilder xmlBuilder,
        PrefeituraHttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<PrefeituraClient> logger)
    {
        _documentoRepo = documentoRepo;
        _tenantRepo = tenantRepo;
        _xmlBuilder = xmlBuilder;
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId,
        TenantId tenantId,
        CancellationToken ct)
    {
        var documento = await _documentoRepo.GetByIdAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("Documento NFS-e {DocumentoId} não encontrado", documentoId.Value);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.DocumentoNaoEncontrado", "Documento não encontrado."));
        }

        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            _logger.LogError("Tenant {TenantId} não encontrado ou inativo para NFS-e", tenantId.Value);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.TenantInvalido", "Tenant não encontrado ou inativo."));
        }

        var codigoMunicipio = tenant.Endereco.CodigoMunicipio;
        var url = PrefeituraEndpointResolver.ResolverGerarNfse(codigoMunicipio, tenant.ConfiguracaoFiscal.Ambiente);
        if (url is null)
        {
            _logger.LogError(
                "Município {CodigoMunicipio} não possui endpoint ABRASF configurado para Tenant {TenantId}",
                codigoMunicipio, tenantId.Value);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.MunicipioNaoSuportado",
                    $"Município {codigoMunicipio} não possui webservice ABRASF configurado."));
        }

        var xmlResult = _xmlBuilder.ConstruirRps(documento, tenant);
        if (xmlResult.IsFailure)
        {
            _logger.LogError("Falha ao construir RPS para NFS-e {DocumentoId}: {Error}",
                documentoId.Value, xmlResult.Error.Code);
            return Result.Failure<PrefeituraRetorno>(xmlResult.Error);
        }

        // Fase 16 adicionará assinatura digital do RPS via certificado A1/A3 do tenant.
        // Em Fase 15 o XML é enviado sem assinatura embutida (aceitável em homologação).
        var soapEnvelope = BuildGerarNfseEnvelope(xmlResult.Value.OuterXml);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, soapEnvelope, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Falha ao enviar NFS-e {DocumentoId} para {Url}",
                documentoId.Value, url);
            return Result.Failure<PrefeituraRetorno>(
                new Error("Prefeitura.HttpFalhou", $"Falha na comunicação com a prefeitura: {ex.Message}"));
        }

        sw.Stop();
        var retorno = PrefeituraRetornoParser.ParseGerarNfse(soapResponse);
        if (retorno.IsFailure) return retorno;
        return Result.Success(retorno.Value with { ElapsedMs = sw.ElapsedMilliseconds });
    }

    public async Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps,
        string serieRps,
        TenantId tenantId,
        int codigoMunicipio,
        CancellationToken ct)
    {
        var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.TenantInvalido", "Tenant não encontrado."));

        var url = PrefeituraEndpointResolver.ResolverConsultarNfse(codigoMunicipio, tenant.ConfiguracaoFiscal.Ambiente);
        if (url is null)
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.MunicipioNaoSuportado",
                    $"Município {codigoMunicipio} não possui webservice ABRASF configurado."));

        if (string.IsNullOrWhiteSpace(tenant.ConfiguracaoFiscal.InscricaoMunicipal))
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.InscricaoMunicipalAusente",
                    "InscricaoMunicipal é obrigatória para consulta NFS-e."));

        var envelope = BuildConsultaEnvelope(
            numeroRps, serieRps,
            tenant.Cnpj.Valor,
            tenant.ConfiguracaoFiscal.InscricaoMunicipal!,
            codigoMunicipio);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        string soapResponse;
        try
        {
            soapResponse = await _httpClient.PostSoapAsync(url, envelope, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            _logger.LogWarning(ex, "Falha ao consultar NFS-e RPS {Numero}/{Serie} na prefeitura {Url}",
                numeroRps, serieRps, url);
            return Result.Failure<PrefeituraConsultaRetorno>(
                new Error("Prefeitura.ConsultaFalhou", $"Falha na consulta à prefeitura: {ex.Message}"));
        }

        sw.Stop();
        var parseResult = PrefeituraRetornoParser.ParseConsultarNfse(soapResponse);
        if (parseResult.IsFailure) return parseResult;
        return Result.Success(parseResult.Value with { ElapsedMs = sw.ElapsedMilliseconds });
    }

    private static string BuildGerarNfseEnvelope(string rpsXml)
    {
        const string NfseWsNs = "http://www.abrasf.org.br/nfse.xsd";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body>
                <RecepcionarLoteRps xmlns="{NfseWsNs}">
                  <nfseDadosMsg>
                    {rpsXml}
                  </nfseDadosMsg>
                </RecepcionarLoteRps>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }

    private static string BuildConsultaEnvelope(string numeroRps, string serieRps, string cnpj,
        string inscricaoMunicipal, int codigoMunicipio)
    {
        const string NfseWsNs = "http://www.abrasf.org.br/nfse.xsd";
        return $"""
            <soap12:Envelope xmlns:soap12="http://www.w3.org/2003/05/soap-envelope">
              <soap12:Body>
                <ConsultarNfsePorRps xmlns="{NfseWsNs}">
                  <nfseDadosMsg>
                    <ConsultarNfsePorRpsEnvio versao="2.04" xmlns="{NfseWsNs}">
                      <IdentificacaoRps>
                        <Numero>{numeroRps}</Numero>
                        <Serie>{serieRps}</Serie>
                        <Tipo>1</Tipo>
                      </IdentificacaoRps>
                      <Prestador>
                        <CpfCnpj>
                          <Cnpj>{cnpj}</Cnpj>
                        </CpfCnpj>
                        <InscricaoMunicipal>{inscricaoMunicipal}</InscricaoMunicipal>
                      </Prestador>
                    </ConsultarNfsePorRpsEnvio>
                  </nfseDadosMsg>
                </ConsultarNfsePorRps>
              </soap12:Body>
            </soap12:Envelope>
            """;
    }
}
```

- [ ] **Step 4: Compilar**

```
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-incremental
```

Esperado: 0 erros.

- [ ] **Step 5: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Fiscal/Prefeitura/
git commit -m "feat: PrefeituraClient + EndpointResolver + RetornoParser ABRASF v2.04"
```

---

## Task 4: PrefeituraProcessingJob — Hangfire Job NFS-e

**Files:**
- Create: `src/VisuFiscalHub.Infrastructure/Jobs/PrefeituraProcessingJob.cs`

O job segue exatamente o mesmo padrão do `FiscalDocumentProcessingJob`:
- `[AutomaticRetry(Attempts = 5)]` com delays crescentes
- `[DisableConcurrentExecution(60)]`
- Throw em falha transiente (Hangfire retenta)
- `DeliveryAttempt` para cada interação
- Status terminal: Autorizado (via NumeroNfse) / Rejeitado / Falhou

- [ ] **Step 1: Implementar PrefeituraProcessingJob**

```csharp
// src/VisuFiscalHub.Infrastructure/Jobs/PrefeituraProcessingJob.cs
using Hangfire;
using Microsoft.Extensions.Logging;
using Serilog.Context;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Infrastructure.Jobs;

/// <summary>
/// Job Hangfire que processa um documento NFS-e do estado Enfileirado até Autorizado/Rejeitado/Falhou.
/// Segue o mesmo padrão do FiscalDocumentProcessingJob: throw em falha transiente (Hangfire retenta).
/// </summary>
[AutomaticRetry(Attempts = 5, DelaysInSeconds = [30, 120, 600, 1800, 3600],
    OnAttemptsExceeded = AttemptsExceededAction.Fail)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class PrefeituraProcessingJob
{
    private readonly IDocumentoFiscalRepository _documentoRepo;
    private readonly IPrefeituraClient _prefeituraClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrefeituraProcessingJob> _logger;
    private readonly ApplicationDbContext _dbContext;

    public PrefeituraProcessingJob(
        IDocumentoFiscalRepository documentoRepo,
        IPrefeituraClient prefeituraClient,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        ILogger<PrefeituraProcessingJob> logger,
        ApplicationDbContext dbContext)
    {
        _documentoRepo = documentoRepo;
        _prefeituraClient = prefeituraClient;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task ExecuteAsync(DocumentoFiscalId documentoId, CancellationToken ct)
    {
        _logger.LogInformation("Iniciando processamento NFS-e {DocumentoId}", documentoId.Value);

        var documento = await _documentoRepo.GetByIdForUpdateAsync(documentoId, ct);
        if (documento is null)
        {
            _logger.LogError("Documento NFS-e {DocumentoId} não encontrado", documentoId.Value);
            return;
        }

        using var tenantScope = LogContext.PushProperty("TenantId", documento.TenantId.Value);
        using var docScope    = LogContext.PushProperty("DocumentoId", documentoId.Value);

        // Idempotência: status finais são terminais
        if (documento.Status is StatusDocumento.Autorizado
            or StatusDocumento.Rejeitado
            or StatusDocumento.Falhou)
        {
            _logger.LogInformation("Documento NFS-e {DocumentoId} já em status final {Status} — ignorado.",
                documentoId.Value, documento.Status);
            return;
        }

        // Guard: apenas NFSe é suportado por este job
        if (documento.Tipo != TipoDocumento.NFSe)
        {
            _logger.LogError(
                "PrefeituraProcessingJob recebeu documento do tipo {Tipo} para {DocumentoId}. " +
                "Apenas NFSe é suportado aqui.",
                documento.Tipo, documentoId.Value);
            documento.Falhar(_timeProvider);
            await _documentoRepo.UpdateAsync(documento, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return;
        }

        if (documento.Status == StatusDocumento.Enfileirado)
        {
            var iniciarResult = documento.IniciarProcessamento();
            if (iniciarResult.IsFailure)
            {
                _logger.LogWarning("NFS-e {DocumentoId} não pôde transicionar para Processando: {Error}",
                    documentoId.Value, iniciarResult.Error.Code);
                return;
            }

            await _documentoRepo.UpdateAsync(documento, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        else
        {
            _logger.LogInformation("NFS-e {DocumentoId} retomado em status {Status}.",
                documentoId.Value, documento.Status);
        }

        var retornoResult = await _prefeituraClient.EnviarRpsAsync(documentoId, documento.TenantId, ct);

        if (retornoResult.IsFailure)
        {
            _logger.LogWarning("Falha transiente ao enviar RPS NFS-e {DocumentoId}: {Error}",
                documentoId.Value, retornoResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha na comunicação com a prefeitura para NFS-e {documentoId.Value}: {retornoResult.Error.Message}");
        }

        var retorno = retornoResult.Value;

        if (retorno.Autorizado && !string.IsNullOrWhiteSpace(retorno.NumeroNfse))
        {
            await AutorizarAsync(documento, retorno, ct);
        }
        else
        {
            await RejeitarAsync(documento, retorno, ct);
        }

        _logger.LogInformation("Processamento NFS-e concluído: {DocumentoId} → {Status}",
            documentoId.Value, documento.Status);
    }

    private async Task AutorizarAsync(
        DocumentoFiscal documento,
        PrefeituraRetorno retorno,
        CancellationToken ct)
    {
        var authorizedAt = _timeProvider.GetUtcNow();

        // NFS-e não tem QR Code — usa QrCode.NaoAplicavel()
        var authResult = documento.Autorizar(
            retorno.NumeroNfse!,
            retorno.XmlNfse ?? string.Empty,
            Domain.ValueObjects.QrCode.NaoAplicavel(),
            authorizedAt,
            _timeProvider);

        if (authResult.IsFailure)
        {
            _logger.LogError("Falha ao autorizar NFS-e {DocumentoId}: {Error}",
                documento.Id.Value, authResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha ao autorizar NFS-e {documento.Id.Value}: {authResult.Error.Code}");
        }

        var attempt = DeliveryAttempt.Criar(
            documento.Id,
            TipoTentativa.Envio,
            _timeProvider.GetUtcNow(),
            success: true,
            responseCode: "100",
            responseMessage: null,
            elapsedMs: retorno.ElapsedMs);

        await _documentoRepo.UpdateAsync(documento, ct);
        _dbContext.DeliveryAttempts.Add(attempt);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation("NFS-e {DocumentoId} AUTORIZADO. Número NFS-e: {NumeroNfse}",
            documento.Id.Value, retorno.NumeroNfse);
    }

    private async Task RejeitarAsync(
        DocumentoFiscal documento,
        PrefeituraRetorno retorno,
        CancellationToken ct)
    {
        var motivo = retorno.MotivoErro ?? "Prefeitura rejeitou a NFS-e sem motivo especificado.";
        var rejectResult = documento.Rejeitar(motivo, _timeProvider);

        if (rejectResult.IsFailure)
        {
            _logger.LogError("Falha ao rejeitar NFS-e {DocumentoId}: {Error}",
                documento.Id.Value, rejectResult.Error.Code);
            throw new InvalidOperationException(
                $"Falha ao rejeitar NFS-e {documento.Id.Value}: {rejectResult.Error.Code}");
        }

        var attempt = DeliveryAttempt.Criar(
            documento.Id,
            TipoTentativa.Envio,
            _timeProvider.GetUtcNow(),
            success: false,
            responseCode: "E99",
            responseMessage: motivo,
            elapsedMs: retorno.ElapsedMs);

        await _documentoRepo.UpdateAsync(documento, ct);
        _dbContext.DeliveryAttempts.Add(attempt);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogWarning("NFS-e {DocumentoId} REJEITADO. Motivo: {Motivo}",
            documento.Id.Value, motivo);
    }
}
```

- [ ] **Step 2: Compilar**

```
dotnet build src/VisuFiscalHub.Infrastructure/VisuFiscalHub.Infrastructure.csproj --no-incremental
```

Esperado: 0 erros.

- [ ] **Step 3: Commit**

```
git add src/VisuFiscalHub.Infrastructure/Jobs/PrefeituraProcessingJob.cs
git commit -m "feat: PrefeituraProcessingJob Hangfire para NFS-e ABRASF"
```

---

## Task 4b: Relaxar DocumentoFiscal.Criar para NFSe (sem itens obrigatórios)

**Files:**
- Modify: `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs`

NFS-e não possui itens de produto — o serviço é descrito pelo `ServicoNfse`. A validação `itemList.Count == 0` deve ser bypassada para `TipoDocumento.NFSe`.

- [ ] **Step 1: Escrever teste que documenta o comportamento correto**

```csharp
// Em tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs, adicionar:
[Fact]
public void Criar_NFSe_SemItens_Sucesso()
{
    var tenant = NfseTestHelpers.CriarTenantNfse();
    var tomador = NfseTestHelpers.TomadorValido();
    var servico = NfseTestHelpers.ServicoNfseValido();

    var result = DocumentoFiscal.Criar(
        id: new DocumentoFiscalId(Guid.NewGuid()),
        tenantId: tenant.Id,
        clienteAppId: tenant.ClienteAppId,
        idempotencyKey: Guid.NewGuid().ToString(),
        tipo: TipoDocumento.NFSe,
        chaveAcesso: null,
        numero: 1L,
        serie: "001",
        indPresenca: 0,
        items: [],  // lista vazia — NFSe não tem itens de produto
        pagamentos: [],
        timeProvider: TimeProvider.System,
        tomador: tomador,
        servicoNfse: servico);

    result.IsSuccess.ShouldBeTrue();
}
```

- [ ] **Step 2: Executar para verificar falha**

```
dotnet test tests/VisuFiscalHub.Tests --filter "Criar_NFSe_SemItens_Sucesso" --no-build
```

Esperado: FAIL com "SemItens".

- [ ] **Step 3: Corrigir DocumentoFiscal.Criar**

Localizar no arquivo `src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs` a linha:

```csharp
        if (itemList.Count == 0)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.SemItens);
```

Substituir por:

```csharp
        // NFSe não possui itens de produto — o serviço é descrito por ServicoNfse.
        if (itemList.Count == 0 && tipo != TipoDocumento.NFSe)
            return Result.Failure<DocumentoFiscal>(DocumentoFiscalErrors.SemItens);
```

- [ ] **Step 4: Executar testes**

```
dotnet test tests/VisuFiscalHub.Tests --no-build
```

Esperado: todos os testes passam incluindo o novo.

- [ ] **Step 5: Commit**

```
git add src/VisuFiscalHub.Domain/Entities/DocumentoFiscal.cs tests/VisuFiscalHub.Tests/Domain/DocumentoFiscalTests.cs
git commit -m "feat: relaxar invariante SemItens para NFSe em DocumentoFiscal.Criar"
```

---

## Task 5: DI + DocumentJobQueue — Roteamento NFSe

**Files:**
- Modify: `src/VisuFiscalHub.Infrastructure/DependencyInjection.cs`
- Modify: `src/VisuFiscalHub.Infrastructure/Jobs/DocumentJobQueue.cs`

- [ ] **Step 1: Atualizar DependencyInjection.cs**

Adicionar após o bloco "Fase 7 — Integração SEFAZ":

```csharp
// Em DependencyInjection.cs, adicionar usings no topo:
using VisuFiscalHub.Infrastructure.Fiscal.Prefeitura;
```

No bloco de registros, após `services.AddScoped<ISefazClient, SefazClient>();`, adicionar:

```csharp
        // Fase 15 — NFS-e ABRASF
        // INfseXmlBuilder: sem dependências externas — AddScoped é suficiente.
        // PrefeituraClient NÃO injeta XmlSigner (assinatura A1/A3 é Fase 16).
        services.AddScoped<INfseXmlBuilder, NfseXmlBuilder>();
        services.AddScoped<PrefeituraHttpClient>();
        services.AddScoped<IPrefeituraClient, PrefeituraClient>();
        services.AddScoped<PrefeituraProcessingJob>();
```

- [ ] **Step 2: Atualizar DocumentJobQueue.cs para rotear NFSe**

```csharp
// src/VisuFiscalHub.Infrastructure/Jobs/DocumentJobQueue.cs
using Hangfire;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Infrastructure.Jobs;

internal sealed class DocumentJobQueue : IDocumentJobQueue
{
    private readonly IBackgroundJobClient _jobClient;

    public DocumentJobQueue(IBackgroundJobClient jobClient)
    {
        _jobClient = jobClient;
    }

    public Task EnqueueProcessingAsync(DocumentoFiscalId id, TipoDocumento tipo, CancellationToken ct = default)
    {
        if (tipo == TipoDocumento.NFSe)
            _jobClient.Enqueue<PrefeituraProcessingJob>(job => job.ExecuteAsync(id, CancellationToken.None));
        else
            _jobClient.Enqueue<FiscalDocumentProcessingJob>(job => job.ExecuteAsync(id, CancellationToken.None));

        return Task.CompletedTask;
    }
}
```

> **IMPORTANTE:** A interface `IDocumentJobQueue.EnqueueProcessingAsync` precisa ter o parâmetro `TipoDocumento tipo` adicionado. Atualizar também.

- [ ] **Step 3: Atualizar IDocumentJobQueue**

```csharp
// src/VisuFiscalHub.Application/Common/Interfaces/IDocumentJobQueue.cs
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Application.Common.Interfaces;

public interface IDocumentJobQueue
{
    Task EnqueueProcessingAsync(DocumentoFiscalId id, TipoDocumento tipo, CancellationToken ct = default);
}
```

- [ ] **Step 4: Atualizar IssueDocumentCommandHandler para passar o tipo**

Localizar a chamada a `_jobQueue.EnqueueProcessingAsync` em `IssueDocumentCommandHandler.cs` e adicionar o parâmetro `tipo`:

```csharp
await _jobQueue.EnqueueProcessingAsync(documento.Id, documento.Tipo, ct);
```

- [ ] **Step 5: Atualizar FakeDocumentJobQueue nos testes**

```csharp
// tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs
// Localizar e atualizar a assinatura:
public Task EnqueueProcessingAsync(DocumentoFiscalId id, TipoDocumento tipo, CancellationToken ct = default)
{
    EnqueuedIds.Add(id);
    EnqueuedTipos.Add(tipo);
    return Task.CompletedTask;
}
```

Verificar se FakeDocumentJobQueue tem `EnqueuedIds` e adaptar conforme a implementação existente.

- [ ] **Step 6: Compilar tudo**

```
dotnet build --no-incremental
```

Esperado: 0 erros.

- [ ] **Step 7: Executar suite completa**

```
dotnet test tests/VisuFiscalHub.Tests --no-build
```

Esperado: todos os testes existentes passam.

- [ ] **Step 8: Commit**

```
git add src/VisuFiscalHub.Application/Common/Interfaces/IDocumentJobQueue.cs
git add src/VisuFiscalHub.Infrastructure/DependencyInjection.cs
git add src/VisuFiscalHub.Infrastructure/Jobs/DocumentJobQueue.cs
git add src/VisuFiscalHub.Application/Documents/Commands/IssueDocument/
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakeDocumentJobQueue.cs
git commit -m "feat: rotear NFSe para PrefeituraProcessingJob via IDocumentJobQueue"
```

---

## Task 6: Endpoint API POST /api/v1/documentos/nfse

**Files:**
- Modify: `src/VisuFiscalHub.Api/Program.cs`

- [ ] **Step 1: Adicionar record IssueNfseRequest**

No final de `Program.cs`, onde estão os outros records, adicionar:

```csharp
internal sealed record IssueNfseRequest(
    TomadorDto Tomador,
    ServicoNfseDto ServicoNfse,
    IReadOnlyList<PagamentoDto>? Pagamentos = null);
```

- [ ] **Step 2: Adicionar o endpoint no bloco documentos**

Após o endpoint `documentos.MapPost("/nfe", ...)`, adicionar:

```csharp
    documentos.MapPost("/nfse",
        async (
            IssueNfseRequest request,
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
                Tipo = TipoDocumento.NFSe,
                Itens = [],
                Pagamentos = request.Pagamentos ?? [],
                IndPresenca = 0,
                Tomador = request.Tomador,
                ServicoNfse = request.ServicoNfse
            };

            return (await mediator.Send(command, ct))
                .ToHttpResult(r => Results.Accepted($"/api/v1/documentos/{r.DocumentoId.Value}/status", r));
        })
        .RequireRateLimiting("api");
```

- [ ] **Step 3: Adicionar usings necessários em Program.cs**

Verificar se `TomadorDto` e `ServicoNfseDto` já estão acessíveis. Eles estão no namespace `VisuFiscalHub.Application.Documents.Commands.IssueDocument` que já está importado via `using VisuFiscalHub.Application.Documents.Commands.IssueDocument;`.

- [ ] **Step 4: Compilar**

```
dotnet build src/VisuFiscalHub.Api/VisuFiscalHub.Api.csproj --no-incremental
```

Esperado: 0 erros.

- [ ] **Step 5: Commit**

```
git add src/VisuFiscalHub.Api/Program.cs
git commit -m "feat: endpoint POST /api/v1/documentos/nfse"
```

---

## Task 7: FakePrefeituraClient + VisuFiscalHubFactory

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakePrefeituraClient.cs`
- Modify: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs`
- Modify: `tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs`

- [ ] **Step 1: Criar FakePrefeituraClient**

```csharp
// tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakePrefeituraClient.cs
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Common;
using VisuFiscalHub.Domain.Identifiers;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

/// <summary>
/// Stub configurável para IPrefeituraClient no ambiente de testes de integração.
/// Comportamento padrão: retorna Autorizado com número NFS-e "1".
/// </summary>
public sealed class FakePrefeituraClient : IPrefeituraClient
{
    private Func<DocumentoFiscalId, TenantId, Task<Result<PrefeituraRetorno>>> _enviarHandler =
        (_, _) => Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: true,
            NumeroNfse: "1",
            Protocolo: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));

    private Func<string, string, TenantId, int, Task<Result<PrefeituraConsultaRetorno>>> _consultaHandler =
        (_, _, _, _) => Task.FromResult(Result.Success(new PrefeituraConsultaRetorno(
            Encontrado: true,
            Autorizado: true,
            NumeroNfse: "1",
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));

    public void SimularAutorizado(string numeroNfse = "1") =>
        _enviarHandler = (_, _) => Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: true,
            NumeroNfse: numeroNfse,
            Protocolo: numeroNfse,
            XmlNfse: "<CompNfse/>",
            MotivoErro: null,
            ElapsedMs: 0L)));

    public void SimularRejeitado(string motivo = "Erro simulado prefeitura") =>
        _enviarHandler = (_, _) => Task.FromResult(Result.Success(new PrefeituraRetorno(
            Autorizado: false,
            NumeroNfse: null,
            Protocolo: null,
            XmlNfse: null,
            MotivoErro: motivo,
            ElapsedMs: 0L)));

    public void SimularFalhaHttp() =>
        _enviarHandler = (_, _) => Task.FromResult(Result.Failure<PrefeituraRetorno>(
            new Error("Prefeitura.HttpFalhou", "Falha de comunicação simulada.")));

    public Task<Result<PrefeituraRetorno>> EnviarRpsAsync(
        DocumentoFiscalId documentoId, TenantId tenantId, CancellationToken ct) =>
        _enviarHandler(documentoId, tenantId);

    public Task<Result<PrefeituraConsultaRetorno>> ConsultarNfseAsync(
        string numeroRps, string serieRps, TenantId tenantId, int codigoMunicipio, CancellationToken ct) =>
        _consultaHandler(numeroRps, serieRps, tenantId, codigoMunicipio);
}
```

- [ ] **Step 2: Atualizar VisuFiscalHubFactory**

Adicionar `FakePrefeituraClient PrefeituraClient { get; } = new();` como propriedade, e no bloco `ConfigureServices`:

```csharp
// Adicionar propriedade:
public FakePrefeituraClient PrefeituraClient { get; } = new();

// Em ConfigureServices, após o bloco do SefazClient:
// Replace IPrefeituraClient with fake
services.RemoveAll<IPrefeituraClient>();
services.AddSingleton<IPrefeituraClient>(PrefeituraClient);
```

- [ ] **Step 3: Atualizar IntegrationTestBase**

Adicionar campo e exposição:

```csharp
protected readonly FakePrefeituraClient PrefeituraFake;

// No construtor, após SefazFake = Factory.SefazClient;:
PrefeituraFake = Factory.PrefeituraClient;
```

- [ ] **Step 4: Compilar**

```
dotnet build tests/VisuFiscalHub.Tests/VisuFiscalHub.Tests.csproj --no-incremental
```

Esperado: 0 erros.

- [ ] **Step 5: Commit**

```
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/FakePrefeituraClient.cs
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/VisuFiscalHubFactory.cs
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs
git commit -m "test: FakePrefeituraClient e VisuFiscalHubFactory para testes NFS-e"
```

---

## Task 8: Testes de Integração NFS-e Lifecycle

**Files:**
- Create: `tests/VisuFiscalHub.Tests/Integration/NfseLifecycleTests.cs`

- [ ] **Step 1: Verificar estrutura de CriarTenantAsync para suportar InscricaoMunicipal**

`IntegrationTestBase.CriarTenantAsync` usa `ConfiguracaoFiscal.Criar(crt, serie, ambiente, ufCodigo)` sem `inscricaoMunicipal`. Para testes NFS-e precisa de um tenant com inscrição municipal.

Adicionar método helper `CriarTenantNfseAsync` em `IntegrationTestBase`:

```csharp
protected async Task<TenantId> CriarTenantNfseAsync(ClienteAppId clienteAppId, string cnpj = "11222333000181")
{
    using var scope = Factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

    var configuracao = ConfiguracaoFiscal.Criar(
        crt: RegimeTributario.SimplesNacional,
        serie: "001",
        ambiente: AmbienteSefaz.Homologacao,
        ufCodigo: 35,
        inscricaoMunicipal: "123456789").Value;

    var endereco = Endereco.Criar(
        logradouro: "Rua Teste",
        numero: "100",
        complemento: null,
        bairro: "Centro",
        municipio: "São Paulo",
        codigoMunicipio: 3550308,
        uf: "SP",
        cep: "01310100").Value;

    var tenant = Tenant.Criar(
        clienteAppId: clienteAppId,
        cnpj: Cnpj.Criar(cnpj).Value,
        razaoSocial: "Empresa NFS-e Teste LTDA",
        nomeFantasia: null,
        configuracaoFiscal: configuracao,
        endereco: endereco,
        timeProvider: timeProvider).Value;

    db.Tenants.Add(tenant);
    await db.SaveChangesAsync();

    return tenant.Id;
}
```

- [ ] **Step 2: Escrever os testes que falham**

```csharp
// tests/VisuFiscalHub.Tests/Integration/NfseLifecycleTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class NfseLifecycleTests : IntegrationTestBase
{
    private async Task<(HttpClient Http, Guid DocumentoId)> EmitirNfseAsync()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantNfseAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfse")
        {
            Content = JsonContent.Create(NfseBodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();

        return (http, body!.DocumentoId.Value);
    }

    private async Task ProcessarAsync(Guid documentoId)
    {
        using var scope = Factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<PrefeituraProcessingJob>();
        await job.ExecuteAsync(new DocumentoFiscalId(documentoId), CancellationToken.None);
    }

    [Fact]
    public async Task NfseLifecycle_Autorizado_StatusENumeroNfseDisponiveis()
    {
        PrefeituraFake.SimularAutorizado(numeroNfse: "42");
        var (http, docId) = await EmitirNfseAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Autorizado);
        status.Protocolo.ShouldBe("42");
        status.AuthorizedAt.ShouldNotBeNull();
        status.MotivoRejeicao.ShouldBeNull();
        // NFS-e não tem ChaveAcesso de 44 dígitos
        status.ChaveAcesso.ShouldBeNull();
        // NFS-e não tem QR Code
        status.QrCode.ShouldBeNull();
    }

    [Fact]
    public async Task NfseLifecycle_Rejeitado_StatusComMotivoPreenchido()
    {
        PrefeituraFake.SimularRejeitado(motivo: "CNPJ do tomador inválido");
        var (http, docId) = await EmitirNfseAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Rejeitado);
        status.MotivoRejeicao.ShouldNotBeNullOrWhiteSpace();
        status.MotivoRejeicao!.ShouldContain("CNPJ do tomador inválido");
        status.Protocolo.ShouldBeNull();
        status.AuthorizedAt.ShouldBeNull();
    }

    [Fact]
    public async Task EmitirNfse_SemIdempotencyKey_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantNfseAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfse")
        {
            Content = JsonContent.Create(NfseBodyValido())
            // sem X-Idempotency-Key
        };
        using var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetNfseStatus_AntesDaProcessamento_RetornaEnfileirado()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantNfseAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfse")
        {
            Content = JsonContent.Create(NfseBodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var emitirResponse = await http.SendAsync(request);
        emitirResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
        var docId = emitirBody!.DocumentoId.Value;

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status!.Status.ShouldBe((int)StatusDocumento.Enfileirado);
        status.Protocolo.ShouldBeNull();
        status.AuthorizedAt.ShouldBeNull();
        status.ChaveAcesso.ShouldBeNull();
        status.QrCode.ShouldBeNull();
    }

    private static object NfseBodyValido() => new
    {
        tomador = new
        {
            cnpjOuCpf = "44555666000177",
            razaoSocial = "Tomador Teste LTDA",
            logradouro = "Av Paulista",
            numero = "1000",
            complemento = (string?)null,
            bairro = "Bela Vista",
            municipio = "São Paulo",
            codigoMunicipio = "3550308",
            uf = "SP",
            cep = "01310100",
            email = (string?)null,
            inscricaoMunicipal = (string?)null
        },
        servicoNfse = new
        {
            codigoServico = "1.01",
            discriminacao = "Consultoria de TI conforme contrato",
            codigoTributacaoMunicipio = (string?)null,
            aliquotaIss = 2.00,
            baseCalculoIss = 1000.00,
            valorIss = 20.00,
            valorDeducoes = (double?)null,
            issRetido = false
        },
        pagamentos = new[]
        {
            new { tipoPagamento = (int)TipoPagamento.Dinheiro, valor = 1000.00 }
        }
    };
}
```

- [ ] **Step 3: Executar testes para verificar falha**

```
dotnet test tests/VisuFiscalHub.Tests --filter "NfseLifecycleTests" --no-build
```

Esperado: FAIL — endpoint não existe ainda (ou compilação falha).

- [ ] **Step 4: Executar após todas as tasks anteriores estarem completas**

```
dotnet test tests/VisuFiscalHub.Tests --filter "NfseLifecycleTests" --no-build
```

Esperado: 4/4 PASS.

- [ ] **Step 5: Executar suite completa**

```
dotnet test tests/VisuFiscalHub.Tests --no-build
```

Esperado: todos os testes passam (744+ existentes + 4 novos NFS-e lifecycle).

- [ ] **Step 6: Commit**

```
git add tests/VisuFiscalHub.Tests/Integration/NfseLifecycleTests.cs
git add tests/VisuFiscalHub.Tests/Integration/Infrastructure/IntegrationTestBase.cs
git commit -m "test: NfseLifecycleTests — ciclo de vida completo NFS-e autorizado/rejeitado"
```

---

## Self-Review

### Spec coverage:
- [x] INfseXmlBuilder implementado → NfseXmlBuilder.cs (Task 1)
- [x] PrefeituraHttpClient implementado (Task 2)
- [x] IPrefeituraClient implementado → PrefeituraClient.cs (Task 3)
- [x] PrefeituraProcessingJob Hangfire (Task 4)
- [x] DI registrations (Task 5)
- [x] DocumentJobQueue roteamento NFSe (Task 5)
- [x] API endpoint POST /nfse (Task 6)
- [x] FakePrefeituraClient (Task 7)
- [x] VisuFiscalHubFactory atualizada (Task 7)
- [x] Testes de integração lifecycle (Task 8)

### Gaps detectados:
- **XmlSigner com `certificate: null!`**: O XmlSigner atual foi escrito para mTLS com X509Certificate2. Para prefeituras sem mTLS, ele ainda pode ser chamado para assinar o XML com o certificado digital do prestador (A1/A3). Verificar a assinatura do método `Assinar` — se exigir `certificate` para fazer mTLS mas não para assinatura XML, o PrefeituraClient pode precisar de ajuste. Esta decisão está marcada no código com comentário.

- **IDocumentJobQueue breaking change**: A adição do parâmetro `TipoDocumento tipo` quebra todos os callers. Task 5 lista explicitamente o `IssueDocumentCommandHandler` como local a atualizar, mas há também `FakeDocumentJobQueue` nos testes.

### Placeholder scan: limpo — nenhum "TBD" ou "TODO" no plano.

### Type consistency: `PrefeituraRetorno` e `PrefeituraConsultaRetorno` usados de forma consistente com a definição em Fase 14 (IPrefeituraClient.cs).
