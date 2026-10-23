using TenderSync.Domain.Tenders;

namespace TenderSync.Domain.Tests.Tenders;

public class ContentHasherTests
{
    [Fact]
    public void Volatile_release_fields_do_not_change_the_hash()
    {
        var a = TestSnapshots.ComfortPacks(releaseId: "release-1");
        var b = TestSnapshots.ComfortPacks(releaseId: "release-2") with { RawJson = "{}", ReleaseDate = TestSnapshots.Now.AddDays(3) };

        ContentHasher.Compute(a).Should().Be(ContentHasher.Compute(b));
    }

    [Fact]
    public void Document_and_closing_changes_change_the_hash()
    {
        var baseline = ContentHasher.Compute(TestSnapshots.ComfortPacks());

        ContentHasher.Compute(TestSnapshots.ComfortPacks(closingAt: TestSnapshots.Now.AddDays(60))).Should().NotBe(baseline);
        ContentHasher.Compute(TestSnapshots.ComfortPacks(documents: [TestSnapshots.Document(title: "Renamed.pdf")])).Should().NotBe(baseline);
    }

    [Fact]
    public void Document_order_does_not_matter()
    {
        var one = TestSnapshots.Document(id: "1", title: "One.pdf");
        var two = TestSnapshots.Document(id: "2", title: "Two.pdf");

        ContentHasher.Compute(TestSnapshots.ComfortPacks(documents: [one, two]))
            .Should().Be(ContentHasher.Compute(TestSnapshots.ComfortPacks(documents: [two, one])));
    }
}
