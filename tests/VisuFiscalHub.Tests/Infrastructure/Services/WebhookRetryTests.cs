using System.Reflection;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Application.Common.Interfaces;
using VisuFiscalHub.Domain.Identifiers;
using VisuFiscalHub.Domain.Interfaces;
using VisuFiscalHub.Infrastructure.Services;

namespace VisuFiscalHub.Tests.Infrastructure.Services;

// Schedule<T>(Expression, TimeSpan) is an extension method on IBackgroundJobClient — it
// cannot be intercepted by NSubstitute.  Internally it calls the single interface method:
//   IBackgroundJobClient.Create(Job job, IState state)
// with state = new ScheduledState(delay).  We therefore verify Create() directly.

public class WebhookRetryTests
{
    private readonly IBackgroundJobClient _jobClient = Substitute.For<IBackgroundJobClient>();

    private WebhookDeliveryService CreateService()
        => new(
            Substitute.For<IClienteAppRepository>(),
            Substitute.For<IDocumentoFiscalRepository>(),
            Substitute.For<ICertificateEncryptionService>(),
            Substitute.For<IHttpClientFactory>(),
            _jobClient,
            null!,
            TimeProvider.System,
            NullLogger<WebhookDeliveryService>.Instance);

    private static void InvokeEnqueueRetry(
        WebhookDeliveryService svc,
        DocumentoFiscalId documentoId,
        ClienteAppId clienteAppId,
        int attemptNumber)
    {
        var method = typeof(WebhookDeliveryService)
            .GetMethod("EnqueueRetryIfApplicable", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(svc, [documentoId, clienteAppId, attemptNumber]);
    }

    // ScheduledState stores delay as EnqueueAt ≈ UtcNow + delay.
    // We accept ±5 s of clock skew between scheduling and verification.
    private static bool HasApproximateDelay(IState state, TimeSpan expectedDelay)
    {
        if (state is not ScheduledState scheduled)
            return false;

        var expectedAt = DateTime.UtcNow.Add(expectedDelay);
        return Math.Abs((scheduled.EnqueueAt - expectedAt).TotalSeconds) <= 5;
    }

    [Fact]
    public void EnqueueRetry_Tentativa1_AgendaTentativa2Com30s()
    {
        var svc       = CreateService();
        var docId     = DocumentoFiscalId.New();
        var clienteId = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 1);

        _jobClient.Received(1).Create(
            Arg.Any<Hangfire.Common.Job>(),
            Arg.Is<IState>(s => HasApproximateDelay(s, TimeSpan.FromSeconds(30))));
    }

    [Fact]
    public void EnqueueRetry_Tentativa2_AgendaTentativa3Com5min()
    {
        var svc       = CreateService();
        var docId     = DocumentoFiscalId.New();
        var clienteId = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 2);

        _jobClient.Received(1).Create(
            Arg.Any<Hangfire.Common.Job>(),
            Arg.Is<IState>(s => HasApproximateDelay(s, TimeSpan.FromMinutes(5))));
    }

    [Fact]
    public void EnqueueRetry_Tentativa3_NaoAgendaNovoRetry()
    {
        var svc       = CreateService();
        var docId     = DocumentoFiscalId.New();
        var clienteId = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 3);

        _jobClient.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(10)]
    public void EnqueueRetry_TentativaAcimaDoMaximo_NaoAgendaRetry(int attemptNumber)
    {
        var svc       = CreateService();
        var docId     = DocumentoFiscalId.New();
        var clienteId = ClienteAppId.New();

        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber);

        _jobClient.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }
}
