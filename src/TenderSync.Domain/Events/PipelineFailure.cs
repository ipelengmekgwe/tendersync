using TenderSync.Domain.Common;

namespace TenderSync.Domain.Events;

/// <summary>Something in the pipeline needs a human: a source is unreachable, its schema drifted, or a job keeps failing.</summary>
public sealed record PipelineFailure(string Source, string Message, string? DetailsJson, DateTimeOffset OccurredAt) : IDomainEvent;
