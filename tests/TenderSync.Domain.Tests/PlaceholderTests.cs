namespace TenderSync.Domain.Tests;

/// <summary>Keeps the test project discoverable until WP01 adds real domain tests.</summary>
public class PlaceholderTests
{
    [Fact]
    public void Domain_assembly_is_loadable()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.Should().Be("TenderSync.Domain");
    }
}
