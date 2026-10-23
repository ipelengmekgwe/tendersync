namespace TenderSync.Application.Abstractions;

/// <summary>Injectable clock. All domain timestamps are UTC; SAST is applied only at the edges.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
