using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Events;
using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Tests.Tenders;

public class TenderMatchTests
{
    [Fact]
    public void Creating_a_match_raises_TenderMatched()
    {
        var tenderId = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();

        var match = TenderMatch.Create(tenderId, profileId, 85, TestSnapshots.Now);

        match.Status.Should().Be(MatchStatus.New);
        var raised = match.DomainEvents.OfType<TenderMatched>().Should().ContainSingle().Which;
        raised.TenderId.Should().Be(tenderId);
        raised.CompanyProfileId.Should().Be(profileId);
        raised.Relevance.Should().Be(85);
    }

    [Fact]
    public void Dismissing_records_reason_and_time()
    {
        var match = TenderMatch.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), 70, TestSnapshots.Now);

        match.Dismiss("Hardware only", TestSnapshots.Now.AddHours(1));

        match.Status.Should().Be(MatchStatus.Dismissed);
        match.DismissReason.Should().Be("Hardware only");
        match.DismissedAt.Should().Be(TestSnapshots.Now.AddHours(1));
    }

    [Fact]
    public void Dismissing_through_SetStatus_is_rejected()
    {
        var match = TenderMatch.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), 70, TestSnapshots.Now);

        var act = () => match.SetStatus(MatchStatus.Dismissed);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Relevance_outside_0_to_100_is_rejected(int relevance)
    {
        var act = () => TenderMatch.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), relevance, TestSnapshots.Now);

        act.Should().Throw<DomainException>();
    }
}
