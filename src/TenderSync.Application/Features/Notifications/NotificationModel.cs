using System.Globalization;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Persistence;
using TenderSync.Application.Common;
using TenderSync.Domain.Companies;
using TenderSync.Domain.Enums;
using TenderSync.Domain.Qualification;
using TenderSync.Domain.Tenders;

namespace TenderSync.Application.Features.Notifications;

/// <summary>
/// Everything a template may print (PLAN.md §9): bid number, title, buyer, province, closing date in SAST, relevance,
/// verdict, the top missing items, the dashboard and source links, plus the fields specific to a type. Values are
/// pre-formatted strings so templates stay trivial; members are addressed by their C# names.
/// </summary>
public sealed class NotificationModel
{
    public string Type { get; set; } = string.Empty;

    public string BidNumber { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Buyer { get; set; } = string.Empty;

    public string Province { get; set; } = string.Empty;

    /// <summary>"Fri 20 Nov 2026 11:00 SAST" or "not stated".</summary>
    public string Closing { get; set; } = "not stated";

    /// <summary>"in 12 days", "tomorrow", "closed" or empty when unknown.</summary>
    public string ClosingIn { get; set; } = string.Empty;

    public int? Relevance { get; set; }

    public string Categories { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    /// <summary>"Qualify", "Partial: open questions", "Do not qualify" or "not assessed yet".</summary>
    public string Verdict { get; set; } = "not assessed yet";

    public int? Score { get; set; }

    public int? EstimatedFunctionality { get; set; }

    public IReadOnlyList<string> MissingItems { get; set; } = [];

    public int QuestionCount { get; set; }

    public string TenderUrl { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    /// <summary>TenderChanged: what changed, one sentence.</summary>
    public string Change { get; set; } = string.Empty;

    /// <summary>PipelineFailure: the failing source or job.</summary>
    public string Source { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>DocumentExpiring: document title and expiry.</summary>
    public string DocumentTitle { get; set; } = string.Empty;

    public string ExpiresOn { get; set; } = string.Empty;

    public int DaysLeft { get; set; }

    /// <summary>Briefing line when the tender has one.</summary>
    public string Briefing { get; set; } = string.Empty;

    /// <summary>DailyDigest sections.</summary>
    public IReadOnlyList<DigestItem> NewMatches { get; set; } = [];

    public IReadOnlyList<DigestItem> ClosingSoon { get; set; } = [];

    public IReadOnlyList<DigestItem> Qualified { get; set; } = [];

    public string Date { get; set; } = string.Empty;

    public string DashboardUrl { get; set; } = string.Empty;

    public static string Sast(DateTimeOffset value) => SouthAfricaTime.ToLocal(value).ToString("ddd d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " SAST";

    public static string Relative(DateTimeOffset? closing, DateTimeOffset now)
    {
        if (closing is not { } at)
        {
            return string.Empty;
        }

        var days = (int)Math.Floor((at - now).TotalDays);
        return days switch
        {
            < 0 => "closed",
            0 => "today",
            1 => "tomorrow",
            _ => $"in {days} days",
        };
    }
}

public sealed record DigestItem(string BidNumber, string Title, string Buyer, string Closing, string ClosingIn, int? Relevance, string Verdict, string TenderUrl);

/// <summary>Builds the model from the aggregate and whatever match and report exist for the profile.</summary>
public sealed class NotificationModelBuilder(IAppDbContext db, IOptions<NotificationOptions> options)
{
    public async Task<NotificationModel> ForTenderAsync(NotificationType type, Tender tender, CompanyProfile? profile, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tender);
        var settings = options.Value;
        var model = new NotificationModel
        {
            Type = type.ToString(),
            BidNumber = tender.BidNumber,
            Title = tender.Title,
            Buyer = tender.Buyer?.Name ?? string.Empty,
            Province = tender.Province == Domain.Enums.Province.Unknown ? "province not stated" : tender.Province.ToString(),
            Closing = tender.ClosingAt is { } closing ? NotificationModel.Sast(closing) : "not stated",
            ClosingIn = NotificationModel.Relative(tender.ClosingAt, now),
            Summary = tender.Summary ?? string.Empty,
            TenderUrl = settings.TenderUrl(tender.Id),
            SourceUrl = tender.SourceUrl,
            Company = profile?.Name ?? string.Empty,
            Briefing = tender.BriefingAt is { } briefing
                ? $"{(tender.BriefingCompulsory == true ? "Compulsory briefing" : "Briefing")} {NotificationModel.Sast(briefing)}{(tender.BriefingVenue is null ? string.Empty : ", " + tender.BriefingVenue)}"
                : string.Empty,
            DashboardUrl = settings.DashboardBaseUrl,
        };

        var classification = await db.Tenders.GetClassificationAsync(tender.Id, cancellationToken);
        if (classification is not null)
        {
            model.Relevance = classification.Relevance;
            model.Categories = string.Join(", ", classification.CategoryCodes);
        }

        if (profile is not null)
        {
            var match = await db.TenderMatches.FindAsync(tender.Id, profile.Id, cancellationToken);
            if (match is not null)
            {
                model.Relevance = match.Relevance;
            }

            var report = await db.QualificationReports.GetLatestAsync(tender.Id, profile.Id, cancellationToken);
            if (report is not null)
            {
                Apply(model, report, settings.MaxMissingItems);
            }
        }

        return model;
    }

    public static void Apply(NotificationModel model, QualificationReport report, int maxMissingItems)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(report);
        model.Verdict = VerdictText(report.Verdict);
        model.Score = report.Score;
        model.EstimatedFunctionality = report.EstimatedFunctionalityScore;
        model.QuestionCount = report.Questions.Count;
        model.MissingItems = report.MissingMandatory.Select(m => m.Name).Concat(report.MissingDocuments.Select(m => string.IsNullOrWhiteSpace(m.FormCode) ? m.Name : $"{m.FormCode}: {m.Name}")).Take(maxMissingItems).ToList();
    }

    public static string VerdictText(QualificationVerdict verdict) => verdict switch
    {
        QualificationVerdict.Qualify => "Qualify",
        QualificationVerdict.Partial => "Partial: open questions",
        QualificationVerdict.NotQualify => "Do not qualify",
        _ => "not assessed yet",
    };
}
