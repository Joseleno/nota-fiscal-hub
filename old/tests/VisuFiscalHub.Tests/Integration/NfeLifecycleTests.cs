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
public sealed class NfeLifecycleTests : IntegrationTestBase
{
    private async Task<(HttpClient Http, Guid DocumentoId)> EmitirNfeAsync()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfe")
        {
            Content = JsonContent.Create(NfeBodyValido()),
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
        var job = scope.ServiceProvider.GetRequiredService<FiscalDocumentProcessingJob>();
        await job.ExecuteAsync(new DocumentoFiscalId(documentoId), CancellationToken.None);
    }

    [Fact]
    public async Task NfeLifecycle_Autorizado_StatusEXmlDisponiveis()
    {
        SefazFake.SimularAutorizado();
        var (http, docId) = await EmitirNfeAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Autorizado);
        status.Protocolo.ShouldBe("135260000000001");
        status.ChaveAcesso.ShouldNotBeNullOrWhiteSpace();
        status.AuthorizedAt.ShouldNotBeNull();
        status.MotivoRejeicao.ShouldBeNull();
        // NF-e não tem QR Code
        status.QrCode.ShouldBeNull();

        using var xmlResponse = await http.GetAsync($"/api/v1/documentos/{docId}/xml");
        xmlResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var xmlBody = await xmlResponse.Content.ReadFromJsonAsync<XmlResponse>();
        xmlBody.ShouldNotBeNull();
        xmlBody!.XmlAssinado.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NfeLifecycle_Rejeitado_StatusComMotivoEXmlIndisponivel()
    {
        SefazFake.SimularRejeitado(cStat: "999", motivo: "Rejeição fiscal definitiva simulada");
        var (http, docId) = await EmitirNfeAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Rejeitado);
        status.MotivoRejeicao.ShouldNotBeNullOrWhiteSpace();
        status.MotivoRejeicao!.ShouldContain("999");
        status.Protocolo.ShouldBeNull();
        status.AuthorizedAt.ShouldBeNull();

        using var xmlResponse = await http.GetAsync($"/api/v1/documentos/{docId}/xml");
        xmlResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NfeLifecycle_Denegado_StatusDenegadoEMotivoPreenchido()
    {
        SefazFake.SimularDenegado(cStat: "110", motivo: "Uso Denegado por irregularidade fiscal simulada");
        var (http, docId) = await EmitirNfeAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Denegado);
        status.MotivoRejeicao.ShouldNotBeNullOrWhiteSpace();
        status.MotivoRejeicao!.ShouldContain("110");
        status.Protocolo.ShouldBeNull();
        status.AuthorizedAt.ShouldBeNull();
    }

    [Fact]
    public async Task NfeLifecycle_Duplicidade_StatusAutorizadoViaConsulta()
    {
        SefazFake.SimularDuplicidade(nProtConsulta: "135260000000099");
        var (http, docId) = await EmitirNfeAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Autorizado);
        status.Protocolo.ShouldBe("135260000000099");
        status.AuthorizedAt.ShouldNotBeNull();
        status.MotivoRejeicao.ShouldBeNull();
    }

    [Fact]
    public async Task GetNfeStatus_AntesDaProcessamento_RetornaEnfileirado()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfe")
        {
            Content = JsonContent.Create(NfeBodyValido()),
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
        status.QrCode.ShouldBeNull();
    }

    private static object NfeBodyValido(decimal valor = 10.00m) => new
    {
        itens = new[]
        {
            new
            {
                codigoProduto = "PROD001",
                descricao = "Produto Teste NF-e",
                ncm = "12345678",
                cest = (string?)null,
                cfopSaida = "5102",
                unidadeComercial = "UN",
                quantidade = (double)1m,
                valorUnitario = (double)valor,
                valorDesconto = 0.0,
                origemMercadoria = 0,
                tributo = new
                {
                    tipoIcms = (int)TipoIcms.CSOSN,
                    csosnOuCst = 400,
                    aliquotaIcms = 0.0,
                    baseCalculoIcms = 0.0,
                    valorIcms = 0.0,
                    cstPis = (int)CstPisCofins.Cst07,
                    baseCalculoPis = 0.0,
                    aliquotaPis = 0.0,
                    valorPis = 0.0,
                    cstCofins = (int)CstPisCofins.Cst07,
                    baseCalculoCofins = 0.0,
                    aliquotaCofins = 0.0,
                    valorCofins = 0.0
                }
            }
        },
        pagamentos = new[]
        {
            new { tipoPagamento = (int)TipoPagamento.Dinheiro, valor = (double)valor }
        },
        nfeDestinatario = new
        {
            cnpjOuCpf = "11222333000181",
            razaoSocial = "Empresa Destinataria Ltda",
            indIeDest = 9,
            ie = (string?)null,
            logradouro = "Av Paulista",
            numero = "1000",
            complemento = (string?)null,
            bairro = "Bela Vista",
            municipio = "São Paulo",
            codigoMunicipio = "3550308",
            uf = "SP",
            cep = "01310100",
            email = (string?)null
        },
        natOp = "Venda de mercadoria",
        indPresenca = 0,
        modFrete = 9
    };

    private sealed record XmlResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("xmlAssinado")] string XmlAssinado);
}
