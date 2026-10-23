using TenderSync.Application.Abstractions;

namespace TenderSync.Worker.Infrastructure;

/// <summary>The system identity under which background jobs run. Audit rows name it "worker".</summary>
internal sealed class WorkerCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => true;

    public Guid? UserId => null;

    public string? Email => null;

    public string DisplayName => "worker";

    public IReadOnlyCollection<string> Roles { get; } = [Application.Security.Roles.Admin];

    public string? IpAddress => null;
}
