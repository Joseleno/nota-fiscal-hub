using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using VisuFiscalHub.Domain.Enums;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Infrastructure.Jobs;
using VisuFiscalHub.Tests.Integration.Infrastructure;

namespace VisuFiscalHub.Tests.Integration;

[Collection("IntegrationTests")]
public sealed class DocumentLifecycleTests : IntegrationTestBase
{
    // Emite e retorna o documentoId para reutilização nos testes de lifecycle.
    private async Task<(HttpClient Http, Guid DocumentoId)> EmitirDocumentoAsync()
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
        using var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();

        return (http, body!.DocumentoId.Value);
    }

    // Invoca NfceProcessingJob diretamente via DI — dispensa Hangfire em execução.
    private async Task ProcessarAsync(Guid documentoId)
    {
        using var scope = Factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<NfceProcessingJob>();
        await job.ExecuteAsync(new DocumentoFiscalId(documentoId), CancellationToken.None);
    }

    [Fact]
    public async Task Lifecycle_Autorizado_StatusEXmlDisponiveis()
    {
        // Arrange
        SefazFake.SimularAutorizado();
        var (http, docId) = await EmitirDocumentoAsync();

        // Act — processa sincronamente no escopo de teste
        await ProcessarAsync(docId);

        // Assert: GET /status
        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Autorizado);
        status.Protocolo.ShouldBe("135260000000001");
        status.ChaveAcesso.ShouldNotBeNullOrWhiteSpace();
        status.AuthorizedAt.ShouldNotBeNull();
        status.MotivoRejeicao.ShouldBeNull();

        // Assert: GET /xml
        using var xmlResponse = await http.GetAsync($"/api/v1/documentos/{docId}/xml");
        xmlResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var xmlBody = await xmlResponse.Content.ReadFromJsonAsync<XmlResponse>();
        xmlBody.ShouldNotBeNull();
        xmlBody!.XmlAssinado.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Lifecycle_Rejeitado_StatusComMotivoEXmlIndisponivel()
    {
        // Arrange
        SefazFake.SimularRejeitado(cStat: "999", motivo: "Rejeição fiscal definitiva simulada");
        var (http, docId) = await EmitirDocumentoAsync();

        // Act
        await ProcessarAsync(docId);

        // Assert: GET /status
        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Rejeitado);
        status.MotivoRejeicao.ShouldNotBeNullOrWhiteSpace();
        status.MotivoRejeicao!.ShouldContain("999");
        status.Protocolo.ShouldBeNull();
        status.AuthorizedAt.ShouldBeNull();

        // Assert: GET /xml — documento rejeitado não possui XML; ResultExtensions mapeia
        // XmlIndisponivel → 404 (mesmo sufixo que NaoEncontrado no switch de erros).
        using var xmlResponse = await http.GetAsync($"/api/v1/documentos/{docId}/xml");
        xmlResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetStatus_AntesDaProcessamento_RetornaEnfileirado()
    {
        // Garante que o status inicial após emissão é Enfileirado (antes do job rodar).
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var emitirResponse = await http.SendAsync(request);
        emitirResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
        var docId = emitirBody!.DocumentoId.Value;

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status!.Status.ShouldBe((int)StatusDocumento.Enfileirado);
        status.Protocolo.ShouldBeNull();
        status.AuthorizedAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetXml_AntesDaProcessamento_Retorna404()
    {
        // XML não está disponível enquanto o documento está Enfileirado.
        // ResultExtensions mapeia XmlIndisponivel → 404 (mesmo código-sufixo que NaoEncontrado).
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfce")
        {
            Content = JsonContent.Create(BodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var emitirResponse = await http.SendAsync(request);
        var emitirBody = await emitirResponse.Content.ReadFromJsonAsync<IssueResponse>();
        var docId = emitirBody!.DocumentoId.Value;

        using var xmlResponse = await http.GetAsync($"/api/v1/documentos/{docId}/xml");

        xmlResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed record XmlResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("xmlAssinado")] string XmlAssinado);
}
