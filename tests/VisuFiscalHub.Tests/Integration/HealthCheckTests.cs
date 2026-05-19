using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class HealthCheckTests : IntegrationTestBase
{
    [Fact]
    public async Task GetHealthLive_SempreRetorna200()
    {
        using var response = await Client.GetAsync("/health/live");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ComInMemoryDb_Retorna200OuServiceUnavailable()
    {
        using var response = await Client.GetAsync("/health");
        ((int)response.StatusCode).ShouldBeOneOf(200, 503);
    }

    [Fact]
    public async Task GetTenants_SemToken_Retorna401()
    {
        using var response = await Client.GetAsync("/api/v1/tenants");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostClientes_SemAdminKey_Retorna401()
    {
        var body = new { name = "Test", clientId = "test", clientSecret = "test12345" };
        using var response = await Client.PostAsJsonAsync("/api/v1/clientes", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostClientes_AdminKeyErrada_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/clientes")
        {
            Content = JsonContent.Create(new { name = "Test", clientId = "test", clientSecret = "test12345" }),
            Headers = { { "X-Admin-Key", "chave-errada" } }
        };
        using var response = await Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
