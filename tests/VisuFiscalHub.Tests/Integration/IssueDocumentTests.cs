using System.Net;
using System.Net.Http.Json;
using Shouldly;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

public sealed class IssueDocumentTests : IntegrationTestBase
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

    [Fact]
    public async Task PostNfce_RequestValido_Retorna202ComDocumentoId()
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
        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();
        body!.DocumentoId.ShouldNotBeNull();
        body.DocumentoId.Value.ShouldNotBe(Guid.Empty);
        body.PollUrl.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostNfce_RequestValido_RetornaStatusEnfileirado()
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
        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();
        // StatusDocumento.Enfileirado = 2 (serializado como int pois não há JsonStringEnumConverter)
        body!.Status.ShouldBe(2);
    }

    [Fact]
    public async Task PostNfce_SemIdempotencyKey_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        var response = await http.PostAsJsonAsync("/api/v1/documentos/nfce", BodyValido());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("IdempotencyKey");
    }

    [Fact]
    public async Task PostNfce_SemToken_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostNfce_MesmaIdempotencyKey_RetornaMesmoDocumentoId()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);
        var idempotencyKey = Guid.NewGuid().ToString();

        HttpRequestMessage MakeReq() => new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", idempotencyKey } }
        };

        var r1 = await http.SendAsync(MakeReq());
        var r2 = await http.SendAsync(MakeReq());

        r1.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        r2.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var b1 = await r1.Content.ReadFromJsonAsync<IssueResponse>();
        var b2 = await r2.Content.ReadFromJsonAsync<IssueResponse>();
        b1.ShouldNotBeNull();
        b2.ShouldNotBeNull();
        b1!.DocumentoId.Value.ShouldBe(b2!.DocumentoId.Value);
    }

    [Fact]
    public async Task PostNfce_NcmInvalido_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        var bodyComNcmInvalido = new
        {
            itens = new[]
            {
                new
                {
                    codigoProduto = "PROD001",
                    descricao = "Produto",
                    ncm = "1234567",  // 7 dígitos — inválido (requer exatamente 8)
                    cest = (string?)null,
                    cfopSaida = "5102",
                    unidadeComercial = "UN",
                    quantidade = 1.0,
                    valorUnitario = 10.0,
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 10.0 } },
            consumidor = (object?)null,
            indPresenca = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(bodyComNcmInvalido),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfce_IndPresencaInvalido_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        // indPresenca = 2 é explicitamente rejeitado pelo validator
        var bodyComIndPresencaInvalido = new
        {
            itens = new[]
            {
                new
                {
                    codigoProduto = "PROD001",
                    descricao = "Produto",
                    ncm = "12345678",
                    cest = (string?)null,
                    cfopSaida = "5102",
                    unidadeComercial = "UN",
                    quantidade = 1.0,
                    valorUnitario = 10.0,
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 10.0 } },
            consumidor = (object?)null,
            indPresenca = 2
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(bodyComIndPresencaInvalido),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfce_TotaisNaoFecham_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        // Item vale 10.00, pagamento é 5.00 — não fecha
        var bodyComTotalErrado = new
        {
            itens = new[]
            {
                new
                {
                    codigoProduto = "PROD001",
                    descricao = "Produto",
                    ncm = "12345678",
                    cest = (string?)null,
                    cfopSaida = "5102",
                    unidadeComercial = "UN",
                    quantidade = 1.0,
                    valorUnitario = 10.0,
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 5.0 } },  // pagamento != item
            consumidor = (object?)null,
            indPresenca = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(bodyComTotalErrado),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
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
