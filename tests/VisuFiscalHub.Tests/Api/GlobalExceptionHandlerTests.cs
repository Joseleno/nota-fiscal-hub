using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Api.Middleware;

namespace VisuFiscalHub.Tests.Api;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_ComCorrelationIdEmItems_IncluiNoExtensions()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Items["CorrelationId"] = "abc123";

        ProblemDetailsContext? capturedContext = null;
        problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(c => capturedContext = c))
            .Returns(ValueTask.FromResult(true));

        var result = await handler.TryHandleAsync(context, new Exception("test"), CancellationToken.None);

        capturedContext.ShouldNotBeNull();
        capturedContext!.ProblemDetails.Extensions["correlationId"].ShouldBe("abc123");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task TryHandleAsync_SemCorrelationId_ExtensionEhNull()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = new GlobalExceptionHandler(
            problemDetailsService,
            NullLogger<GlobalExceptionHandler>.Instance);

        var context = new DefaultHttpContext();

        ProblemDetailsContext? capturedContext = null;
        problemDetailsService
            .TryWriteAsync(Arg.Do<ProblemDetailsContext>(c => capturedContext = c))
            .Returns(ValueTask.FromResult(true));

        var result = await handler.TryHandleAsync(context, new Exception("test"), CancellationToken.None);

        capturedContext.ShouldNotBeNull();
        capturedContext!.ProblemDetails.Extensions.ContainsKey("correlationId").ShouldBeTrue();
        capturedContext!.ProblemDetails.Extensions["correlationId"].ShouldBeNull();
        result.ShouldBeTrue();
    }
}
