using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class DocumentStatusTests : IntegrationTestBase
{
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
    public async Task GetStatus_DocumentoExistente_Retorna200ComBodyValido()
    {
        var (httpRaw, docId) = await EmitirAsync();
        using var http = httpRaw;

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatusResponse>();
        body.ShouldNotBeNull();
        body!.DocumentoId.Value.ShouldBe(docId);
        // StatusDocumento serializa como int — Enfileirado é o estado imediatamente após emissão
        body.Status.ShouldBe((int)StatusDocumento.Enfileirado);
    }

    [Fact]
    public async Task GetStatus_DocumentoInexistente_Retorna404()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var response = await http.GetAsync($"/api/v1/documentos/{Guid.NewGuid()}/status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetXml_DocumentoEnfileirado_Retorna404ComProblemDetails()
    {
        var (httpRaw, docId) = await EmitirAsync();
        using var http = httpRaw;

        var response = await http.GetAsync($"/api/v1/documentos/{docId}/xml");

        // Enfileirado → XmlAssinado é null → DocumentoFiscalErrors.XmlIndisponivel → 404
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("detail", out var detail).ShouldBeTrue();
        detail.GetString().ShouldBe("DocumentoFiscal.XmlIndisponivel");
    }

    [Fact]
    public async Task GetStatus_TenantInexistente_Retorna404()
    {
        // Emite documento com o ClienteApp-1 / Tenant-1
        var (http1Raw, docId) = await EmitirAsync();
        using var http1 = http1Raw;

        // Reutiliza o token do http1 com um TenantId inexistente para verificar isolamento.
        using var httpTenantFalso = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Reutiliza o Authorization do http1 mas com um TenantId aleatório inexistente
        var authHeader = http1.DefaultRequestHeaders.Authorization;
        httpTenantFalso.DefaultRequestHeaders.Authorization = authHeader;
        httpTenantFalso.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await httpTenantFalso.GetAsync($"/api/v1/documentos/{docId}/status");

        // Tenant inexistente → TenantValidationMiddleware retorna 404 antes do handler.
        // 403 não é possível neste cenário (handler nunca é alcançado).
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStatus_TenantDeOutroClienteApp_Retorna403()
    {
        // Emite documento com ClienteApp-1 / Tenant-1
        var (http1Raw, docId) = await EmitirAsync();
        using var http1 = http1Raw;

        // Cria ClienteApp-2 com Tenant-2. Token e tenant pertencem ao mesmo ClienteApp-2 —
        // TenantValidationMiddleware passa; a guarda de ownership no handler retorna 403.
        var (clienteAppId2, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);
        var tenantId2 = await CriarTenantAsync(clienteAppId2, cnpj: "12345678000195");
        using var http2 = CriarClienteAutenticado(token2, tenantId2.Value);

        var response = await http2.GetAsync($"/api/v1/documentos/{docId}/status");

        // GetDocumentStatusQueryHandler: documento.ClienteAppId != query.ClienteAppId → 403
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetStatus_TokenDeClienteApp2_ComTenantIdDeClienteApp1_Retorna403()
    {
        // Emite documento com ClienteApp-1 / Tenant-1; obtém o TenantId-1
        var (clienteAppId1, clientId1, clientSecret1) = await CriarClienteAppAsync();
        var token1 = await ObterTokenAsync(clientId1, clientSecret1);
        var tenantId1 = await CriarTenantAsync(clienteAppId1);

        // Cria ClienteApp-2 com token próprio
        var (_, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);

        // Mismatch deliberado: token do ClienteApp-2 + X-Tenant-Id do Tenant-1 (ClienteApp-1)
        using var httpAtaque = CriarClienteAutenticado(token2, tenantId1.Value);

        var response = await httpAtaque.GetAsync($"/api/v1/documentos/{Guid.NewGuid()}/status");

        // TenantValidationMiddleware: tenant.ClienteAppId (1) != clienteAppId do JWT (2) → 403
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // Espelha DocumentoStatusResponse (Application/Common/Models/DocumentoStatusResponse.cs).
    // StatusDocumento serializa como int — sem JsonStringEnumConverter no projeto.
    private sealed record StatusResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("documentoId")] DocumentoIdDto DocumentoId,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int Status,
        [property: System.Text.Json.Serialization.JsonPropertyName("chaveAcesso")] string? ChaveAcesso,
        [property: System.Text.Json.Serialization.JsonPropertyName("qrCode")] string? QrCode,
        [property: System.Text.Json.Serialization.JsonPropertyName("protocolo")] string? Protocolo,
        [property: System.Text.Json.Serialization.JsonPropertyName("motivoRejeicao")] string? MotivoRejeicao,
        [property: System.Text.Json.Serialization.JsonPropertyName("authorizedAt")] DateTimeOffset? AuthorizedAt);

}
