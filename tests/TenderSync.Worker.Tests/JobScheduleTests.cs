using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TenderSync.Worker.Jobs;

namespace TenderSync.Worker.Tests;

public class JobScheduleTests
{
    [Fact]
    public void Trigger_on_start_runs_the_listed_recurring_jobs_once_and_skips_unknown_ids()
    {
        var manager = Substitute.For<IRecurringJobManager>();

        var triggered = JobSchedule.TriggerOnStart(manager, [IngestRecentReleasesJob.RecurringId, " ", "no-such-job", IngestRecentReleasesJob.RecurringId], NullLogger.Instance);

        triggered.Should().Equal(IngestRecentReleasesJob.RecurringId);
        manager.Received(1).Trigger(IngestRecentReleasesJob.RecurringId);
        manager.DidNotReceive().Trigger("no-such-job");
    }
}
