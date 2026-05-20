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
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly IBackgroundJobClient _jobClient = Substitute.For<IBackgroundJobClient>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private WebhookDeliveryService CreateService()
        => new(
            Substitute.For<IClienteAppRepository>(),
            Substitute.For<IDocumentoFiscalRepository>(),
            Substitute.For<ICertificateEncryptionService>(),
            Substitute.For<IHttpClientFactory>(),
            _jobClient,
            null!,
            Substitute.For<IUnitOfWork>(),
            _timeProvider,
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

    // ScheduledState.EnqueueAt = DateTime.UtcNow + delay, set internally by Hangfire's Schedule<T>
    // extension method — the injected TimeProvider does not control this clock.
    // Strategy: bracket the call with before/after timestamps and assert EnqueueAt ∈ [before+delay, after+delay].
    // This is deterministic regardless of CI jitter and does not rely on a fixed tolerance constant.
    private static bool HasDelay(IState state, TimeSpan expectedDelay, DateTime before, DateTime after)
    {
        if (state is not ScheduledState scheduled)
            return false;

        var lo = before.Add(expectedDelay);
        var hi = after.Add(expectedDelay);
        return scheduled.EnqueueAt >= lo && scheduled.EnqueueAt <= hi;
    }

    [Fact]
    public void EnqueueRetry_Tentativa1_AgendaTentativa2Com30s()
    {
        var svc       = CreateService();
        var docId     = DocumentoFiscalId.New();
        var clienteId = ClienteAppId.New();

        var before = DateTime.UtcNow;
        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 1);
        var after = DateTime.UtcNow;

        _jobClient.Received(1).Create(
            Arg.Any<Hangfire.Common.Job>(),
            Arg.Is<IState>(s => HasDelay(s, TimeSpan.FromSeconds(30), before, after)));
    }

    [Fact]
    public void EnqueueRetry_Tentativa2_AgendaTentativa3Com5min()
    {
        var svc       = CreateService();
        var docId     = DocumentoFiscalId.New();
        var clienteId = ClienteAppId.New();

        var before = DateTime.UtcNow;
        InvokeEnqueueRetry(svc, docId, clienteId, attemptNumber: 2);
        var after = DateTime.UtcNow;

        _jobClient.Received(1).Create(
            Arg.Any<Hangfire.Common.Job>(),
            Arg.Is<IState>(s => HasDelay(s, TimeSpan.FromMinutes(5), before, after)));
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

    private sealed class FixedTimeProvider(DateTimeOffset fixedNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedNow;
    }
}
