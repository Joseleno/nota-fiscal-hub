using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class JwtSecurityTests : IntegrationTestBase
{
    [Fact]
    public async Task PostNfce_TokenExpirado_Retorna401()
    {
        // ValidateLifetime = true no TokenValidationParameters — token com exp no passado deve
        // ser rejeitado pelo JwtBearerMiddleware antes de atingir qualquer handler.
        var expiredToken = GerarTokenExpirado();
        using var http = CriarClienteAutenticado(expiredToken, Guid.NewGuid());

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostNfce_TokenAssinadoComChaveErrada_Retorna401()
    {
        // ValidateIssuerSigningKey = true — token assinado com RSA diferente da chave
        // registrada (JWT__PUBLICKEYPEMS__0) deve ser rejeitado com 401.
        // Cobre o cenário de forjamento: atacante emite token próprio para se passar por
        // um ClienteApp legítimo.
        var forgedToken = GerarTokenComChaveErrada();
        using var http = CriarClienteAutenticado(forgedToken, Guid.NewGuid());

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetXml_TenantDeOutroClienteApp_Retorna403()
    {
        // Espelha GetStatus_TenantDeOutroClienteApp_Retorna403 para o endpoint /xml.
        // GetDocumentXmlQueryHandler tem o mesmo check de ownership que GetDocumentStatusQueryHandler.
        var (clienteAppId1, clientId1, clientSecret1) = await CriarClienteAppAsync();
        var token1 = await ObterTokenAsync(clientId1, clientSecret1);
        var tenantId1 = await CriarTenantAsync(clienteAppId1);
        using var http1 = CriarClienteAutenticado(token1, tenantId1.Value);

        // Emite documento com ClienteApp-1
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var emitirResponse = await http1.SendAsync(request);
        emitirResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
        var docId = emitirBody!.DocumentoId.Value;

        // ClienteApp-2 com Tenant-2 próprio tenta acessar o XML do documento do ClienteApp-1
        var (clienteAppId2, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);
        var tenantId2 = await CriarTenantAsync(clienteAppId2, cnpj: "12345678000195");
        using var http2 = CriarClienteAutenticado(token2, tenantId2.Value);

        using var response = await http2.GetAsync($"/api/v1/documentos/{docId}/xml");

        // GetDocumentXmlQueryHandler: documento.ClienteAppId != query.ClienteAppId → 403
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetXml_TokenDeClienteApp2_ComTenantIdDeClienteApp1_Retorna403()
    {
        // Espelha GetStatus_TokenDeClienteApp2_ComTenantIdDeClienteApp1_Retorna403 para /xml.
        // TenantValidationMiddleware detecta mismatch token↔tenant antes de o handler ser chamado.
        var (clienteAppId1, clientId1, clientSecret1) = await CriarClienteAppAsync();
        var token1 = await ObterTokenAsync(clientId1, clientSecret1);
        var tenantId1 = await CriarTenantAsync(clienteAppId1);

        var (_, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);

        // Mismatch deliberado: token do ClienteApp-2 + X-Tenant-Id do Tenant-1 (ClienteApp-1)
        using var httpAtaque = CriarClienteAutenticado(token2, tenantId1.Value);

        using var response = await httpAtaque.GetAsync($"/api/v1/documentos/{Guid.NewGuid()}/xml");

        // TenantValidationMiddleware: tenant.ClienteAppId (1) != clienteAppId do JWT (2) → 403
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostNfce_SemXTenantId_ComTokenValido_Retorna500()
    {
        // Documenta o comportamento atual: sem X-Tenant-Id, TenantValidationMiddleware
        // não popula TenantContext → HttpContextCurrentUserContext.TenantId lança
        // InvalidOperationException → GlobalExceptionHandler retorna 500.
        // Gap conhecido: deveria retornar 422 (header obrigatório ausente).
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        using var http = CriarClienteAutenticado(token, tenantId: null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var response = await http.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }
}
