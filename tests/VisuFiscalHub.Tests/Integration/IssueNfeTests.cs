using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class IssueNfeTests : IntegrationTestBase
{
    [Fact]
    public async Task PostNfe_SemXIdempotencyKey_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        using var response = await http.PostAsJsonAsync("/api/v1/documentos/nfe", BuildValidNfeRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("IdempotencyKey");
    }

    [Fact]
    public async Task PostNfe_SemAutorizacao_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfe");
        request.Headers.Add("X-Idempotency-Key", "TEST-KEY-NF-E");
        request.Content = JsonContent.Create(BuildValidNfeRequest());

        // Client é o cliente não-autenticado herdado de IntegrationTestBase
        using var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static object BuildValidNfeRequest() => new
    {
        itens = new[]
        {
            new
            {
                codigoProduto = "001",
                descricao = "Produto Teste",
                ncm = "12345678",
                cest = (string?)null,
                cfopSaida = "5102",
                unidadeComercial = "UN",
                quantidade = 1.0,
                valorUnitario = 10.0,
                valorDesconto = 0.0,
                origemMercadoria = 0,
                tributo = new
                {
                    tipoIcms = 0,        // TipoIcms.CST
                    csosnOuCst = 40,
                    aliquotaIcms = 0.0,
                    baseCalculoIcms = 0.0,
                    valorIcms = 0.0,
                    cstPis = 7,
                    baseCalculoPis = 0.0,
                    aliquotaPis = 0.0,
                    valorPis = 0.0,
                    cstCofins = 7,
                    baseCalculoCofins = 0.0,
                    aliquotaCofins = 0.0,
                    valorCofins = 0.0
                }
            }
        },
        pagamentos = new[] { new { tipoPagamento = 1, valor = 10.0 } },
        nfeDestinatario = new
        {
            cnpjOuCpf = "11222333000181",
            razaoSocial = "Empresa Teste Ltda",
            indIeDest = 9,
            ie = (string?)null,
            logradouro = "Rua Teste",
            numero = "100",
            complemento = (string?)null,
            bairro = "Centro",
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
}
