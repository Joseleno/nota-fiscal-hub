using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VisuFiscalHub.Application.Common.Security;
using VisuFiscalHub.Domain.Entities;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.ValueObjects;
using VisuFiscalHub.Infrastructure.Persistence;

namespace VisuFiscalHub.Tests.Integration.Infrastructure;

public abstract class IntegrationTestBase : IDisposable
{
    protected readonly VisuFiscalHubFactory Factory;
    protected readonly HttpClient Client;
    protected readonly FakeSefazClient SefazFake;
    protected readonly FakeDocumentJobQueue JobQueue;
    protected readonly FakeSequenceManager SequenceManager;

    protected IntegrationTestBase()
    {
        Factory = new VisuFiscalHubFactory();
        Client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        SefazFake       = Factory.SefazClient;
        JobQueue        = Factory.JobQueue;
        SequenceManager = Factory.SequenceManager;
        JobQueue.Reset();
    }

    protected async Task<(ClienteAppId Id, string ClientId, string ClientSecret)> CriarClienteAppAsync()
    {
        var clientId = $"cli_{Guid.NewGuid():N}";
        var clientSecret = Guid.NewGuid().ToString("N");
        var clientSecretHash = ClientSecretHasher.DerivarHash(clientSecret);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var app = ClienteApp.Criar(
            name: "Test ClienteApp",
            clientId: clientId,
            clientSecretHash: clientSecretHash,
            timeProvider: timeProvider).Value;

        db.ClienteApps.Add(app);
        await db.SaveChangesAsync();

        return (app.Id, clientId, clientSecret);
    }

    protected async Task<string> ObterTokenAsync(string clientId, string clientSecret)
    {
        var response = await Client.PostAsJsonAsync("/auth/token", new { clientId, clientSecret });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenBody>();
        return body!.AccessToken;
    }

    protected HttpClient CriarClienteAutenticado(string token, Guid? tenantId = null)
    {
        var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (tenantId.HasValue)
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.Value.ToString());
        return client;
    }

    protected async Task<TenantId> CriarTenantAsync(ClienteAppId clienteAppId, string cnpj = "11222333000181")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        var configuracao = ConfiguracaoFiscal.Criar(
            crt: RegimeTributario.SimplesNacional,
            serie: "001",
            ambiente: AmbienteSefaz.Homologacao,
            ufCodigo: 35).Value;

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
            razaoSocial: "Empresa Teste LTDA",
            nomeFantasia: null,
            configuracaoFiscal: configuracao,
            endereco: endereco,
            timeProvider: timeProvider).Value;

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private sealed record TokenBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn);

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
    }
}
