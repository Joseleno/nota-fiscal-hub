using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class CancelamentoTests : IntegrationTestBase
{
    private async Task<(HttpClient Http, Guid DocumentoId)> EmitirEAutorizarAsync()
    {
        SefazFake.SimularAutorizado();
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token    = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http     = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var emitirResponse = await http.SendAsync(request);
        emitirResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
        var docId = emitirBody!.DocumentoId.Value;

        using var scope = Factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<FiscalDocumentProcessingJob>();
        await job.ExecuteAsync(new DocumentoFiscalId(docId), CancellationToken.None);

        return (http, docId);
    }

    private async Task<(HttpClient Http, Guid DocumentoId)> EmitirSemProcessarAsync()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token    = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        var http     = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var emitirResponse = await http.SendAsync(request);
        emitirResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
        return (http, emitirBody!.DocumentoId.Value);
    }

    [Fact]
    public async Task PostCancelar_DocumentoAutorizado_Retorna202ComStatusCancelando()
    {
        var (http, id) = await EmitirEAutorizarAsync();
        var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var status = await http.GetFromJsonAsync<StatusResponse>($"/api/v1/documentos/{id}/status");
        status!.Status.ShouldBe((int)StatusDocumento.Cancelando);
    }

    [Fact]
    public async Task PostCancelar_DocumentoEnfileirado_Retorna422()
    {
        var (http, id) = await EmitirSemProcessarAsync();
        var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_DocumentoJaCancelando_Retorna422()
    {
        var (http, id) = await EmitirEAutorizarAsync();
        var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };
        await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body); // primeira
        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body); // segunda

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_SemJustificativa_Retorna422()
    {
        var (http, id) = await EmitirEAutorizarAsync();
        var body = new { justificativa = "" };

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_JustificativaMuitoCurta_Retorna422()
    {
        var (http, id) = await EmitirEAutorizarAsync();
        var body = new { justificativa = "abc de fghij n" }; // 14 chars (mínimo = 15)

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task PostCancelar_DocumentoDeOutroClienteApp_Retorna403()
    {
        var (_, id) = await EmitirEAutorizarAsync(); // ClienteApp-1
        var (_, clientId2, clientSecret2) = await CriarClienteAppAsync();
        var token2 = await ObterTokenAsync(clientId2, clientSecret2);
        var http2  = CriarClienteAutenticado(token2); // sem tenantId
        var body = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

        var response = await http2.PostAsJsonAsync($"/api/v1/documentos/{id}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostCancelar_DocumentoNaoEncontrado_Retorna404()
    {
        var (_, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var http  = CriarClienteAutenticado(token);
        var body  = new { justificativa = "Justificativa de cancelamento suficientemente longa" };

        var response = await http.PostAsJsonAsync($"/api/v1/documentos/{Guid.NewGuid()}/cancelar", body);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
