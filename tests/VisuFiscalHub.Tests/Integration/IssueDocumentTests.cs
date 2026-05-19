using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class IssueDocumentTests : IntegrationTestBase
{
    [Fact]
    public async Task PostNfce_RequestValido_Retorna202ComDocumentoId()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();
        body!.DocumentoId.ShouldNotBeNull();
        body.DocumentoId.Value.ShouldNotBe(Guid.Empty);
        body.PollUrl.ShouldNotBeNullOrWhiteSpace();
        body.ChaveAcesso.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostNfce_RequestValido_RetornaStatusEnfileirado()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();
        // StatusDocumento.Enfileirado serializado como int pois não há JsonStringEnumConverter
        body!.Status.ShouldBe((int)StatusDocumento.Enfileirado);
    }

    [Fact]
    public async Task PostNfce_SemIdempotencyKey_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        using var response = await http.PostAsJsonAsync("/api/v1/documentos/nfce", BodyValido());

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

        using var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostNfce_MesmaIdempotencyKey_RetornaMesmoDocumentoId()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);
        var idempotencyKey = Guid.NewGuid().ToString();

        HttpRequestMessage MakeReq() => new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", idempotencyKey } }
        };

        using var r1 = await http.SendAsync(MakeReq());
        using var r2 = await http.SendAsync(MakeReq());

        r1.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        r2.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var b1 = await r1.Content.ReadFromJsonAsync<IssueResponse>();
        var b2 = await r2.Content.ReadFromJsonAsync<IssueResponse>();
        b1.ShouldNotBeNull();
        b2.ShouldNotBeNull();
        b1!.DocumentoId.Value.ShouldBe(b2!.DocumentoId.Value);
        // O handler de idempotência retorna o documento existente sem chamar EnqueueProcessingAsync —
        // um único job enfileirado prova que não houve re-processamento duplicado.
        JobQueue.EnqueuedIds.Count.ShouldBe(1);
    }

    [Fact]
    public async Task PostNfce_NcmInvalido_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

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

        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("ncm");
    }

    [Fact]
    public async Task PostNfce_IndPresencaInvalido_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

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

        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfce_TotaisNaoFecham_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

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
            pagamentos = new[] { new { tipoPagamento = 1, valor = 5.0 } },
            consumidor = (object?)null,
            indPresenca = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(bodyComTotalErrado),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostNfce_CpfObrigatorio_AcimaLimiteLegal_Retorna422()
    {
        // IssueDocumentCommandValidator: totalItens > 10_000m requer CPF do consumidor.
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var body = new
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
                    valorUnitario = 10_001.0,  // > 10_000m — acima do limite real do validator
                    valorDesconto = 0.0,
                    origemMercadoria = 0,
                    tributo = TributoSimples()
                }
            },
            pagamentos = new[] { new { tipoPagamento = 1, valor = 10_001.0 } },
            consumidor = (object?)null,
            indPresenca = 1
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(body),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };

        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var responseBody = await response.Content.ReadAsStringAsync();
        // A mensagem exata do validator contém "10.000" — substring que identifica
        // especificamente a regra de CPF obrigatório acima do limite legal.
        responseBody.ShouldContain("10.000");
    }

    [Fact]
    public async Task CancelarDocumento_Retorna501()
    {
        // Cancelamento não implementado — endpoint retorna 501 incondicionalmente.
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        using var response = await http.PostAsJsonAsync(
            $"/api/v1/documentos/{Guid.NewGuid()}/cancelar", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

}
