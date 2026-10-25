namespace TenderSync.Worker.Tests;

public class WorkerCliTests
{
    [Fact]
    public void Seed_demo_is_a_one_shot_mode()
    {
        var cli = WorkerCli.Parse(["--seed-demo"]);

        cli.SeedDemo.Should().BeTrue();
        cli.IsOneShot.Should().BeTrue();
    }

    [Fact]
    public void No_arguments_start_the_server()
    {
        var cli = WorkerCli.Parse([]);

        cli.SeedDemo.Should().BeFalse();
        cli.IsOneShot.Should().BeFalse();
    }
}
