using TenderSync.Domain.Notifications;

namespace TenderSync.Domain.Tests.Notifications;

public class QuietHoursTests
{
    private static DateTimeOffset Sast(int hour, int minute = 0, int day = 23) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(23, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(12, false)]
    [InlineData(21, false)]
    [InlineData(22, true)]
    public void A_window_crossing_midnight_contains_the_night_hours(int hour, bool expected) =>
        new QuietHours("22:00", "06:00", null).Contains(Sast(hour)).Should().Be(expected);

    [Fact]
    public void A_daytime_window_and_an_empty_window_behave()
    {
        var lunch = new QuietHours("12:00", "14:00", null);
        lunch.Contains(Sast(13)).Should().BeTrue();
        lunch.Contains(Sast(14)).Should().BeFalse();
        new QuietHours("08:00", "08:00", null).Contains(Sast(8)).Should().BeFalse("a zero-length window never holds anything");
    }

    [Fact]
    public void The_hold_ends_at_the_next_window_end_in_the_zone()
    {
        var night = new QuietHours("22:00", "06:00", null);

        night.EndsAt(Sast(23)).Should().Be(Sast(6, day: 24).ToUniversalTime(), "late evening waits for the next morning");
        night.EndsAt(Sast(3, day: 24)).Should().Be(Sast(6, day: 24).ToUniversalTime());
        night.EndsAt(Sast(12)).Should().BeNull("outside the window there is no hold");
        night.EndsAt(Sast(23).ToUniversalTime()).Should().Be(Sast(6, day: 24).ToUniversalTime(), "the input offset does not matter");
    }

    [Fact]
    public void Json_round_trips_and_an_unknown_zone_falls_back_to_sast()
    {
        var parsed = QuietHours.Parse("{\"start\":\"22:00\",\"end\":\"06:00\",\"timeZone\":\"Mars/Olympus\"}");

        parsed.Should().NotBeNull();
        parsed!.Contains(Sast(23)).Should().BeTrue();
        parsed.ToJson().Should().Contain("\"timeZone\":\"Mars/Olympus\"");
        QuietHours.Parse(null).Should().BeNull();
        QuietHours.Parse("   ").Should().BeNull();
    }
}
