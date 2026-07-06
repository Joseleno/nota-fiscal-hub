using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotaFiscalHub.BuildingBlocks.Observability;

namespace NotaFiscalHub.BuildingBlocks.Observability.UnitTests;

/// <summary>
/// T2 (spec B7) — <see cref="CorrelationIdMiddleware"/> em um host mínimo (<see cref="TestServer"/>, sem
/// dependência de Postgres/Docker): cobre o contrato do middleware isoladamente, antes do wire-up completo
/// nos hosts reais (Api/Worker) via <see cref="ObservabilityExtensions"/>.
/// </summary>
public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Middleware_HeaderValido_PreservadoNaRespostaENoContexto()
    {
        using var cliente = CriarCliente();

        using var mensagem = new HttpRequestMessage(HttpMethod.Get, "/teste");
        mensagem.Headers.Add("X-Correlation-Id", "abc-123-def");

        var resposta = await cliente.SendAsync(mensagem);

        Assert.Equal("abc-123-def", resposta.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("abc-123-def", await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Middleware_SemHeader_GeraGuid()
    {
        using var cliente = CriarCliente();

        var resposta = await cliente.GetAsync("/teste");

        var valor = resposta.Headers.GetValues("X-Correlation-Id").Single();
        Assert.True(Guid.TryParse(valor, out _));
    }

    [Theory]
    [InlineData("curto")] // < 8 chars
    [InlineData("tem espaço e caracteres inválidos!!")]
    public async Task Middleware_HeaderInvalido_GeraGuidEmVezDeConfiarNoCliente(string headerInvalido)
    {
        using var cliente = CriarCliente();

        using var mensagem = new HttpRequestMessage(HttpMethod.Get, "/teste");
        mensagem.Headers.TryAddWithoutValidation("X-Correlation-Id", headerInvalido);

        var resposta = await cliente.SendAsync(mensagem);

        var valor = resposta.Headers.GetValues("X-Correlation-Id").Single();
        Assert.True(Guid.TryParse(valor, out _));
        Assert.NotEqual(headerInvalido, valor);
    }

    [Fact]
    public async Task Middleware_HeaderNoLimiteValido_EPreservado()
    {
        using var cliente = CriarCliente();

        var header64 = new string('a', 64);
        using var mensagem = new HttpRequestMessage(HttpMethod.Get, "/teste");
        mensagem.Headers.Add("X-Correlation-Id", header64);

        var resposta = await cliente.SendAsync(mensagem);

        Assert.Equal(header64, resposta.Headers.GetValues("X-Correlation-Id").Single());
    }

    private static HttpClient CriarCliente()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<CorrelationContext>();
                    services.AddSingleton<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.Run(async context =>
                    {
                        var correlationContext = context.RequestServices.GetRequiredService<ICorrelationContext>();
                        await context.Response.WriteAsync(correlationContext.CorrelationId);
                    });
                });
            });

        var host = builder.Start();
        return host.GetTestClient();
    }
}
