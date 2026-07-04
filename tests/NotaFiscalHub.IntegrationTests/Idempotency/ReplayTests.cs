using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NotaFiscalHub.Api.Testing;

namespace NotaFiscalHub.IntegrationTests.Idempotency;

/// <summary>
/// Critérios de aceite 2, 5 e 9 (spec B4): reenvio da mesma key + mesmo corpo devolve a resposta original
/// sem re-executar o handler — inclusive quando a resposta original é <c>202</c> (contingência). GET
/// ignora o header mesmo se enviado.
///
/// NÃO EXECUTA neste ambiente: requer PostgreSQL real via Testcontainers/Docker, indisponível aqui
/// (ver task-4-report.md, "Gap de ambiente"). Escrito e revisado para rodar em CI/ambiente com Docker.
/// </summary>
public class ReplayTests : IClassFixture<IdempotencyTestFixture>
{
    private readonly IdempotencyTestFixture _fixture;

    public ReplayTests(IdempotencyTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task DoisPostsSequenciais_MesmaKeyMesmoCorpo_HandlerExecutaUmaVez_SegundaEhReplay()
    {
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        var contaId = Guid.NewGuid();
        using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, contaId);
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "abc");

        var corpo = JsonContent.Create(new { valor = 123 });
        var resposta1 = await cliente.PostAsync("/v1/nfce", corpo);
        var corpo2 = JsonContent.Create(new { valor = 123 });
        var resposta2 = await cliente.PostAsync("/v1/nfce", corpo2);

        Assert.Equal(1, contador.Total);
        Assert.Equal(await resposta1.Content.ReadAsStringAsync(), await resposta2.Content.ReadAsStringAsync());
        Assert.Equal("true", resposta2.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(resposta1.StatusCode, resposta2.StatusCode);
        Assert.Equal(resposta1.Content.Headers.ContentType, resposta2.Content.Headers.ContentType);
    }

    [Fact]
    public async Task Resposta202DeContingencia_EhArmazenadaEReplayadaIdentica_SemRedisparaEmissao()
    {
        using var factory = _fixture.CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, Guid.NewGuid());
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "cont-1");

        var corpo = JsonContent.Create(new { valor = "contingencia" });
        var resposta1 = await cliente.PostAsync("/v1/nfce/contingencia", corpo);
        Assert.Equal(HttpStatusCode.Accepted, resposta1.StatusCode);

        var corpo2 = JsonContent.Create(new { valor = "contingencia" });
        var resposta2 = await cliente.PostAsync("/v1/nfce/contingencia", corpo2);

        Assert.Equal(HttpStatusCode.Accepted, resposta2.StatusCode);
        Assert.Equal(await resposta1.Content.ReadAsStringAsync(), await resposta2.Content.ReadAsStringAsync());
        Assert.Equal("true", resposta2.Headers.GetValues("Idempotency-Replayed").Single());
        Assert.Equal(1, contador.Total);
    }

    [Fact]
    public async Task RotaGet_IgnoraHeaderIdempotencyKey_MesmoSeEnviado()
    {
        using var factory = _fixture.CriarFactory();
        using var cliente = IdempotencyTestFixture.ClienteComTenant(factory, Guid.NewGuid());
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "ignorada");

        var resposta1 = await cliente.GetAsync("/v1/nfce/123");
        var resposta2 = await cliente.GetAsync("/v1/nfce/123");

        Assert.False(resposta1.Headers.Contains("Idempotency-Replayed"));
        Assert.False(resposta2.Headers.Contains("Idempotency-Replayed"));
    }
}
