using System.Net;
using System.Net.Http.Json;
using Shouldly;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

public sealed class AuthFlowTests : IntegrationTestBase
{
    [Fact]
    public async Task PostToken_CredenciaisValidas_Retorna200ComAccessToken()
    {
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();

        var response = await Client.PostAsJsonAsync("/auth/token", new { clientId, clientSecret });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TokenBody>();
        body.ShouldNotBeNull();
        body!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        body.TokenType.ShouldBe("Bearer");
        body.ExpiresIn.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task PostToken_ClientSecretErrado_Retorna401()
    {
        var (_, clientId, _) = await CriarClienteAppAsync();

        var response = await Client.PostAsJsonAsync("/auth/token",
            new { clientId, clientSecret = "senha-errada" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("invalid_client");
    }

    [Fact]
    public async Task PostToken_ClientIdInexistente_Retorna401()
    {
        var response = await Client.PostAsJsonAsync("/auth/token",
            new { clientId = "nao-existe", clientSecret = "qualquer" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostToken_CredenciaisValidas_RetornaContentTypeJson()
    {
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();

        var response = await Client.PostAsJsonAsync("/auth/token", new { clientId, clientSecret });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
    }

    private sealed record TokenBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);
}
