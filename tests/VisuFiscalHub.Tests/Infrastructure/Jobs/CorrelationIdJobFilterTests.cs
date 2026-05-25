using Hangfire;
using Hangfire.Server;
using Hangfire.Storage;
using NSubstitute;
using Shouldly;
using VisuFiscalHub.Infrastructure.Jobs;

namespace VisuFiscalHub.Tests.Infrastructure.Jobs;

public class CorrelationIdJobFilterTests
{
    [Fact]
    public void OnPerforming_ArmazenaDisposableEmItems()
    {
        var filter = new CorrelationIdJobFilter();
        var (performingContext, _) = CreateContexts("job-123");

        filter.OnPerforming(performingContext);

        performingContext.Items.Values.OfType<IDisposable>().Count().ShouldBe(1);
    }

    [Fact]
    public void OnPerformed_DescartaDisposable()
    {
        var filter = new CorrelationIdJobFilter();
        var (performingContext, performedContext) = CreateContexts("job-456");
        filter.OnPerforming(performingContext);

        var disposable = Substitute.For<IDisposable>();
        var key = performingContext.Items.Keys.First();
        performingContext.Items[key] = disposable;

        filter.OnPerformed(performedContext);

        disposable.Received(1).Dispose();
    }

    /// <summary>
    /// Creates a <see cref="PerformingContext"/> and a <see cref="PerformedContext"/> that share
    /// the same underlying <see cref="PerformContext"/> (and therefore the same Items dictionary).
    /// </summary>
    private static (PerformingContext Performing, PerformedContext Performed) CreateContexts(string jobId)
    {
        var connection = Substitute.For<IStorageConnection>();
        var cancellationToken = Substitute.For<IJobCancellationToken>();
        var backgroundJob = new BackgroundJob(jobId, new Hangfire.Common.Job(
            typeof(object), typeof(object).GetMethod("ToString")!), DateTime.UtcNow);
        var performContext = new PerformContext(connection, backgroundJob, cancellationToken);
        var performing = new PerformingContext(performContext);
        var performed = new PerformedContext(performContext, null, false, null);
        return (performing, performed);
    }
}
