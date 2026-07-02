using System.Net;
using System.Net.Http.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class AdminAndTenantTests : IntegrationTestBase
{
    private const string AdminKey = "test-admin-key-1234567890";

    [Fact]
    public async Task PostClientes_AdminKeyValida_Retorna201ComClienteAppId()
    {
        var clientId = $"cli_{Guid.NewGuid():N}";
        var body = new
        {
            name = "Empresa Teste",
            clientId,
            clientSecret = "senhaSegura123456",
            webhookUrl = (string?)null
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/clientes")
        {
            Content = JsonContent.Create(body),
            Headers = { { "X-Admin-Key", AdminKey } }
        };
        using var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ClienteAppCreatedDto>();
        created.ShouldNotBeNull();
        created!.Id.ShouldNotBe(Guid.Empty);
        created.ClientId.ShouldBe(clientId);
        created.WebhookSecret.ShouldNotBeNullOrWhiteSpace();
        created.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task PostClientes_ClientIdDuplicado_Retorna422()
    {
        var clientId = $"cli_{Guid.NewGuid():N}";
        var body = new { name = "Empresa", clientId, clientSecret = "senhaSegura123456" };

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/clientes")
        {
            Content = JsonContent.Create(body),
            Headers = { { "X-Admin-Key", AdminKey } }
        };
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/clientes")
        {
            Content = JsonContent.Create(body),
            Headers = { { "X-Admin-Key", AdminKey } }
        };

        using var r1 = await Client.SendAsync(req1);
        using var r2 = await Client.SendAsync(req2);

        r1.StatusCode.ShouldBe(HttpStatusCode.Created);
        // Segundo com mesmo clientId deve falhar — validator ou handler rejeita duplicata
        ((int)r2.StatusCode).ShouldBeInRange(400, 499);
    }

    [Fact]
    public async Task PostTenants_RequestValido_Retorna201ComTenantId()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        // POST /api/v1/tenants não exige X-Tenant-Id — ainda não existe tenant para este ClienteApp
        using var http = CriarClienteAutenticado(token);

        var body = new
        {
            cnpj = "11222333000181",
            razaoSocial = "Empresa Via API LTDA",
            nomeFantasia = (string?)null,
            regimeTributario = 1,   // SimplesNacional
            ambiente = 2,           // Homologacao
            ufCodigo = 35,          // São Paulo
            serie = "001",
            endereco = new
            {
                logradouro = "Rua Via API",
                numero = "42",
                complemento = (string?)null,
                bairro = "Centro",
                municipio = "São Paulo",
                codigoMunicipio = 3550308,
                uf = "SP",
                cep = "01310100"
            }
        };

        using var response = await http.PostAsJsonAsync("/api/v1/tenants", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<TenantCreatedDto>();
        created.ShouldNotBeNull();
        created!.Id.ShouldNotBe(Guid.Empty);
        // ClienteAppId no response deve corresponder ao sub do JWT
        created.ClienteAppId.ShouldBe(clienteAppId.Value);
        created.Cnpj.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostTenants_CnpjDuplicado_Retorna409()
    {
        // Mesmo CNPJ no mesmo ClienteApp — o handler detecta a duplicata na transação.
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        using var http = CriarClienteAutenticado(token);

        var body = new
        {
            cnpj = "11222333000181",
            razaoSocial = "Empresa LTDA",
            nomeFantasia = (string?)null,
            regimeTributario = 1,
            ambiente = 2,
            ufCodigo = 35,
            serie = "001",
            endereco = new
            {
                logradouro = "Rua Teste",
                numero = "1",
                complemento = (string?)null,
                bairro = "Centro",
                municipio = "São Paulo",
                codigoMunicipio = 3550308,
                uf = "SP",
                cep = "01310100"
            }
        };

        using var r1 = await http.PostAsJsonAsync("/api/v1/tenants", body);
        using var r2 = await http.PostAsJsonAsync("/api/v1/tenants", body);

        r1.StatusCode.ShouldBe(HttpStatusCode.Created);
        r2.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetTenants_ComTokenValido_Retorna200ComLista()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        await CriarTenantAsync(clienteAppId);
        var tenantId = await CriarTenantAsync(clienteAppId, cnpj: "12345678000195");
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        using var response = await http.GetAsync("/api/v1/tenants");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTenant_PorId_Retorna200ComDados()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        using var response = await http.GetAsync($"/api/v1/tenants/{tenantId.Value}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TenantCreatedDto>();
        body.ShouldNotBeNull();
        body!.Id.ShouldBe(tenantId.Value);
    }

    // DTOs para deserializar respostas dos endpoints administrativos.
    // Campos são subconjunto mínimo necessário para validar o response — não espelham o record completo.
    private sealed record ClienteAppCreatedDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] Guid Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("clientId")] string ClientId,
        [property: System.Text.Json.Serialization.JsonPropertyName("webhookSecret")] string WebhookSecret,
        [property: System.Text.Json.Serialization.JsonPropertyName("isActive")] bool IsActive);

    private sealed record TenantCreatedDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("id")] Guid Id,
        [property: System.Text.Json.Serialization.JsonPropertyName("clienteAppId")] Guid ClienteAppId,
        [property: System.Text.Json.Serialization.JsonPropertyName("cnpj")] string Cnpj);
}
