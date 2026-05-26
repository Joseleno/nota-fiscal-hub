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
public sealed class NfseLifecycleTests : IntegrationTestBase
{
    private async Task<(HttpClient Http, Guid DocumentoId)> EmitirNfseAsync()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantNfseAsync(clienteAppId);
        var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfse")
        {
            Content = JsonContent.Create(NfseBodyValido()),
            Headers = { { "X-Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        using var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<IssueResponse>();
        body.ShouldNotBeNull();

        return (http, body!.DocumentoId.Value);
    }

    private async Task ProcessarAsync(Guid documentoId)
    {
        using var scope = Factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<PrefeituraProcessingJob>();
        await job.ExecuteAsync(new DocumentoFiscalId(documentoId), CancellationToken.None);
    }

    [Fact]
    public async Task NfseLifecycle_Autorizado_StatusENumeroNfseDisponiveis()
    {
        PrefeituraFake.SimularAutorizado(numeroNfse: "42");
        var (http, docId) = await EmitirNfseAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Autorizado);
        status.Protocolo.ShouldBe("42");
        status.AuthorizedAt.ShouldNotBeNull();
        status.MotivoRejeicao.ShouldBeNull();
        // NFS-e não tem ChaveAcesso de 44 dígitos SEFAZ
        status.ChaveAcesso.ShouldBeNull();
        // NFS-e não tem QR Code
        status.QrCode.ShouldBeNull();
    }

    [Fact]
    public async Task NfseLifecycle_Rejeitado_StatusComMotivoPreenchido()
    {
        PrefeituraFake.SimularRejeitado(motivo: "CNPJ do tomador inválido");
        var (http, docId) = await EmitirNfseAsync();

        await ProcessarAsync(docId);

        using var statusResponse = await http.GetAsync($"/api/v1/documentos/{docId}/status");
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var status = await statusResponse.Content.ReadFromJsonAsync<StatusResponse>();
        status.ShouldNotBeNull();
        status!.Status.ShouldBe((int)StatusDocumento.Rejeitado);
        status.MotivoRejeicao.ShouldNotBeNullOrWhiteSpace();
        status.MotivoRejeicao!.ShouldContain("CNPJ do tomador inválido");
        status.Protocolo.ShouldBeNull();
        status.AuthorizedAt.ShouldBeNull();
    }

    [Fact]
    public async Task EmitirNfse_SemIdempotencyKey_Retorna422()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantNfseAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfse")
        {
            Content = JsonContent.Create(NfseBodyValido())
            // sem X-Idempotency-Key
        };
        using var response = await http.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task GetNfseStatus_AntesDoProcessamento_RetornaEnfileirado()
    {
        var (clienteAppId, clientId, clientSecret) = await CriarClienteAppAsync();
        var token = await ObterTokenAsync(clientId, clientSecret);
        var tenantId = await CriarTenantNfseAsync(clienteAppId);
        using var http = CriarClienteAutenticado(token, tenantId.Value);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/documentos/nfse")
        {
            Content = JsonContent.Create(NfseBodyValido()),
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
        status.ChaveAcesso.ShouldBeNull();
        status.QrCode.ShouldBeNull();
    }

    private static object NfseBodyValido() => new
    {
        tomador = new
        {
            cnpjOuCpf = "11222333000181",
            razaoSocial = "Tomador Teste LTDA",
            logradouro = "Av Paulista",
            numero = "1000",
            complemento = (string?)null,
            bairro = "Bela Vista",
            municipio = "São Paulo",
            codigoMunicipio = "3550308",
            uf = "SP",
            cep = "01310100",
            email = (string?)null,
            inscricaoMunicipal = (string?)null
        },
        servicoNfse = new
        {
            codigoServico = "1.01",
            discriminacao = "Consultoria de TI conforme contrato",
            codigoTributacaoMunicipio = (string?)null,
            aliquotaIss = 2.00,
            baseCalculoIss = 1000.00,
            valorIss = 20.00,
            valorDeducoes = (double?)null,
            issRetido = false
        },
        pagamentos = new[]
        {
            new { tipoPagamento = (int)TipoPagamento.Dinheiro, valor = 1000.00 }
        }
    };
}
