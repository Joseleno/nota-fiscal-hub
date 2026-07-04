using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotaFiscalHub.Api.Authentication;
using NotaFiscalHub.Api.Testing;
using NotaFiscalHub.BuildingBlocks.Idempotency;

namespace NotaFiscalHub.IntegrationTests.Idempotency;

/// <summary>
/// Prova, contra o host real (<c>Api</c>) via <see cref="WebApplicationFactory{TEntryPoint}"/> mas com
/// <see cref="IIdempotencyStore"/> substituído por <see cref="FakeIdempotencyStore"/> (em memória — SEM
/// PostgreSQL), que <see cref="IdempotencyEndpointFilter"/> em si está corretamente encanado: replay
/// devolve corpo/status/headers idênticos SEM re-executar o handler, conflito de hash dá 409, e — o ponto
/// mais importante — o <see cref="Microsoft.AspNetCore.Http.IResult"/> do handler não é executado DUAS
/// VEZES pelo pipeline de filtros (bug real do minimal API: o framework só chama
/// <c>IResult.ExecuteAsync</c> uma vez sobre o valor devolvido pelo filtro mais externo; <c>next()</c> não
/// executa o resultado do handler, só o repassa — ver comentário em
/// <c>IdempotencyEndpointFilter.ExecutarHandlerECompletarAsync</c>). Isso RODA neste ambiente (não depende
/// de Docker) e cobre a parte do filter que não exige semântica real de banco — a arbitração da corrida em
/// si (PK única, UPDATE atômico) continua exclusiva de <see cref="ConcurrencyTests"/>/
/// <see cref="OrphanTakeoverTests"/> contra Postgres real.
/// </summary>
public class EndpointFilterPlumbingTests
{
    private static WebApplicationFactory<Program> CriarFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IIdempotencyStore>();
                services.AddSingleton<IIdempotencyStore>(new FakeIdempotencyStore());
            });
        });

    [Fact]
    public async Task DoisPostsSequenciais_MesmaKeyMesmoCorpo_HandlerExecutaUmaVez_SegundaEhReplay()
    {
        using var factory = CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, Guid.NewGuid().ToString());
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "plumbing-1");

        var resposta1 = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));
        var resposta2 = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));

        Assert.Equal(1, contador.Total);
        Assert.Equal(HttpStatusCode.Created, resposta1.StatusCode);
        Assert.Equal(resposta1.StatusCode, resposta2.StatusCode);
        Assert.Equal(await resposta1.Content.ReadAsStringAsync(), await resposta2.Content.ReadAsStringAsync());
        Assert.Equal("true", resposta2.Headers.GetValues("Idempotency-Replayed").Single());
    }

    [Fact]
    public async Task PrimeiraResposta_NaoTemHeaderDeReplay()
    {
        using var factory = CriarFactory();
        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, Guid.NewGuid().ToString());
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "plumbing-2");

        var resposta = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));

        Assert.False(resposta.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task RespostaCapturada_NaoEhEscritaDuasVezes_CorpoNaoDuplicado()
    {
        // Prova direta do bug corrigido: se o IResult do handler fosse executado duas vezes (uma por
        // ExecutarHandlerECompletarAsync, outra pelo framework sobre o valor de retorno do filtro), o
        // corpo da resposta viria concatenado/corrompido ou a chamada lançaria por resposta já iniciada.
        using var factory = CriarFactory();
        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, Guid.NewGuid().ToString());
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "plumbing-3");

        var resposta = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));
        var corpo = await resposta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        // O corpo é um único objeto JSON válido — se tivesse sido escrito duas vezes concatenado, isso
        // falharia a desserialização (dois objetos JSON colados não formam JSON válido).
        var documento = System.Text.Json.JsonDocument.Parse(corpo);
        Assert.True(documento.RootElement.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task MesmaKeyCorpoDiferente_Retorna409_HandlerNaoExecutaDeNovo()
    {
        using var factory = CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, Guid.NewGuid().ToString());
        cliente.DefaultRequestHeaders.Add("Idempotency-Key", "plumbing-4");

        await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = "A" }));
        var resposta = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = "B" }));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(1, contador.Total);

        var problem = await resposta.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("idempotency_key_conflito", problem!.Type);
    }

    [Fact]
    public async Task SemIdempotencyKey_Retorna400_HandlerNaoExecuta()
    {
        using var factory = CriarFactory();
        var contador = factory.Services.GetRequiredService<ContadorDoHandlerFake>();
        contador.Resetar();

        using var cliente = factory.CreateClient();
        cliente.DefaultRequestHeaders.Add(StubTenantResolver.HeaderContaId, Guid.NewGuid().ToString());

        var resposta = await cliente.PostAsync("/v1/nfce", JsonContent.Create(new { valor = 1 }));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(0, contador.Total);
    }
}
