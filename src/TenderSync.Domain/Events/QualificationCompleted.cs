using TenderSync.Domain.Common;
using TenderSync.Domain.Enums;

namespace TenderSync.Domain.Events;

public sealed record QualificationCompleted(Guid TenderId, Guid CompanyProfileId, Guid ReportId, QualificationVerdict Verdict, int Score, DateTimeOffset OccurredAt) : IDomainEvent;
