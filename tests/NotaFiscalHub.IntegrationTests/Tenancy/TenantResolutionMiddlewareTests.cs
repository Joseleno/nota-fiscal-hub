using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NotaFiscalHub.Api.Authentication;
using Xunit;

namespace NotaFiscalHub.IntegrationTests.Tenancy;

/// <summary>
/// Prova, ponta a ponta (host real via <see cref="WebApplicationFactory{TEntryPoint}"/>), que o
/// <c>TenantResolutionMiddleware</c> abre um escopo por requisição e que esse escopo NÃO vaza entre
/// requisições concorrentes — duas requisições em paralelo, cada uma com uma conta diferente no header
/// <c>X-Nfh-Conta-Id</c>, devem enxergar cada uma o seu próprio tenant, nunca o da outra.
/// </summary>
public class TenantResolutionMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public TenantResolutionMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task DoisRequestsConcorrentes_NaoVazamEscopoDeTenant()
    {
        var contaA = Guid.NewGuid();
        var contaB = Guid.NewGuid();

        var tarefaA = ClienteComHeader(contaA).GetAsync("/teste/tenant-atual");
        var tarefaB = ClienteComHeader(contaB).GetAsync("/teste/tenant-atual");
        await Task.WhenAll(tarefaA, tarefaB);

        var respostaA = await tarefaA;
        var respostaB = await tarefaB;

        Assert.Equal(contaA.ToString(), await respostaA.Content.ReadAsStringAsync());
        Assert.Equal(contaB.ToString(), await respostaB.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SemHeaderDeConta_Retorna401()
    {
        using var cliente = _factory.CreateClient();
        var resposta = await cliente.GetAsync("/teste/tenant-atual");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task EndpointAlive_NaoExigeHeaderDeConta()
    {
        using var cliente = _factory.CreateClient();
        var resposta = await cliente.GetAsync("/alive");
        Assert.Equal(System.Net.HttpStatusCode.OK, resposta.StatusCode);
    }

    private HttpClient ClienteComHeader(Guid contaId)
    {
        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, contaId.ToString());
        return cliente;
    }
}
