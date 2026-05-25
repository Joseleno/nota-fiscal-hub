using Microsoft.AspNetCore.Http;
using Shouldly;
using VisuFiscalHub.Api.Middleware;

namespace VisuFiscalHub.Tests.Api;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task Invoke_SemHeaderDeEntrada_GeraCorrelationIdNaResposta()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Correlation-Id"].ToString().ShouldNotBeEmpty();
        context.Items["CorrelationId"].ShouldNotBeNull();
    }

    [Fact]
    public async Task Invoke_ComHeaderDeEntrada_ReutilizaCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "meu-id-12345";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Correlation-Id"].ToString().ShouldBe("meu-id-12345");
        context.Items["CorrelationId"].ShouldBe("meu-id-12345");
    }

    [Fact]
    public async Task Invoke_HeaderMaiorQue128Chars_Trunca()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = new string('x', 200);
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var id = context.Response.Headers["X-Correlation-Id"].ToString();
        id.Length.ShouldBe(128);
        id.ShouldBe(new string('x', 128));  // verify content preserved for safe chars
    }

    [Fact]
    public async Task Invoke_HeaderComCaracteresInvalidos_Sanitiza()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "id-123 malicious";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var id = context.Response.Headers["X-Correlation-Id"].ToString();
        id.ShouldBe("id-123malicious");
    }
}
