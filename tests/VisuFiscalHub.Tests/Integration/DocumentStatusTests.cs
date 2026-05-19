using System.Net;
using System.Net.Http.Json;
using Shouldly;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

public sealed class DocumentStatusTests : IntegrationTestBase
{
    // TributoDto minimal válido para Simples Nacional (CSOSN 400 — sem cálculo de ICMS).
    // CstPisCofins 7 = Cst07 (PISNT/COFINSNT — Simples Nacional).
    private static object TributoSimples() => new
    {
        tipoIcms = 1,          // TipoIcms.CSOSN
        csosnOuCst = 400,      // CSOSN 400
        aliquotaIcms = 0.0,
        baseCalculoIcms = 0.0,
        valorIcms = 0.0,
        cstPis = 7,            // CstPisCofins.Cst07 — PISNT
        baseCalculoPis = 0.0,
        aliquotaPis = 0.0,
        valorPis = 0.0,
        cstCofins = 7,         // CstPisCofins.Cst07 — COFINSNT
        baseCalculoCofins = 0.0,
        aliquotaCofins = 0.0,
        valorCofins = 0.0
    };

    // Body válido: itens + pagamentos com totais fechando, NCM 8 dígitos, indPresenca 1.
    private static object BodyValido(decimal valor = 10.00m) => new
    {
        itens = new[]
        {
            new
            {
                codigoProduto = "PROD001",
                descricao = "Produto Teste",
                ncm = "12345678",
                cest = (string?)null,
                cfopSaida = "5102",
                unidadeComercial = "UN",
                quantidade = (double)1m,
                valorUnitario = (double)valor,
                valorDesconto = 0.0,
                origemMercadoria = 0,  // OrigemMercadoria.Nacional
                tributo = TributoSimples()
            }
        },
        pagamentos = new[]
        {
            new { tipoPagamento = 1, valor = (double)valor }  // TipoPagamento.Dinheiro = 1
        },
        consumidor = (object?)null,
        indPresenca = 1
    };

    private async Task<(HttpClient Http, Guid DocumentoGuid)> EmitirAsync()
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
        var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // DocumentoFiscalId serializa como { "documentoId": { "value": "<guid>" } }
        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();
        var docIdGuid = body!.DocumentoId.Value;

        return (http, docIdGuid);
    }

    [Fact]
    public async Task GetStatus_DocumentoExistente_Retorna200()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetStatus_DocumentoInexistente_Retorna404()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        var response = await http.GetAsync($"/api/v1/documentos/{Guid.NewGuid()}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetXml_DocumentoEnfileirado_Retorna404()
    {
        var (http, docId) = await EmitirAsync();

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/xml");

        // Enfileirado → XmlAssinado é null → XmlIndisponivel → 404
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStatus_TenantErrado_Retorna404Ou403()
    {
        // Emite documento com o ClienteApp-1 / Tenant-1
        var (http1, docId) = await EmitirAsync();

        // Tenta acessar o mesmo documento usando o mesmo token mas com um tenantId
        // que não existe — simula isolamento sem precisar de um segundo /auth/token
        // (uma segunda chamada a /auth/token na mesma instância falha por bug conhecido
        // no cache do AsymmetricSignatureProvider do Microsoft.IdentityModel).
        // O cabeçalho X-Tenant-Id inexistente deve resultar em 404 ou 403.
        using var httpTenantFalso = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Reutiliza o Authorization do http1 mas com um TenantId aleatório inexistente
        var authHeader = http1.DefaultRequestHeaders.Authorization;
        httpTenantFalso.DefaultRequestHeaders.Authorization = authHeader;
        httpTenantFalso.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await httpTenantFalso.GetAsync($"/api/v1/documentos/{docId}/status");

        ((int)response.StatusCode).ShouldBeOneOf(404, 403);
    }

    // DTO local para deserializar a resposta 202.
    // DocumentoFiscalId serializa como {"value": "<guid>"} pois é um readonly record struct
    // sem JsonConverter personalizado registrado.
    // Status é int pois não há JsonStringEnumConverter configurado no projeto.
    private sealed record IssueResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("pollUrl")] string PollUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

    private sealed record DocumentoIdDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("value")] Guid Value);
}
