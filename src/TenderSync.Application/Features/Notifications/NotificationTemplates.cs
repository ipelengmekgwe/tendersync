using System.Collections.Concurrent;
using Scriban;
using Scriban.Runtime;
using TenderSync.Domain.Enums;

namespace TenderSync.Application.Features.Notifications;

/// <summary>
/// Scriban templates under Notifications/Templates/{type}.{channel}.sbn (PLAN.md §9), copied to the output folder by the
/// csproj. The first line of the rendered text is the subject; the rest is the body. Lookup falls back from the exact type
/// to its family (closing-soon for the three ClosingSoon types), then to default.{channel}.sbn, then to default.text.sbn,
/// so a new type renders before it has templates of its own. Members are addressed by their C# names (model.BidNumber).
/// </summary>
public sealed class NotificationTemplates
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, Template?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public NotificationTemplates()
        : this(Path.Combine(AppContext.BaseDirectory, "Notifications", "Templates"))
    {
    }

    public NotificationTemplates(string root)
    {
        _root = root;
    }

    public static string Family(NotificationType type) => type switch
    {
        NotificationType.ClosingSoon7d or NotificationType.ClosingSoon3d or NotificationType.ClosingSoon1d => "closing-soon",
        NotificationType.NewMatch => "new-match",
        NotificationType.QualificationReady => "qualification-ready",
        NotificationType.BriefingReminder => "briefing-reminder",
        NotificationType.TenderChanged => "tender-changed",
        NotificationType.AwardPublished => "award-published",
        NotificationType.PipelineFailure => "pipeline-failure",
        NotificationType.DocumentExpiring => "document-expiring",
        NotificationType.DailyDigest => "daily-digest",
        NotificationType.Test => "test",
        _ => type.ToString().ToLowerInvariant(),
    };

    /// <summary>Email and Telegram render HTML; Teams gets markdown text; SMS and WhatsApp share the plain text templates.</summary>
    public static string ChannelName(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "email",
        NotificationChannel.Telegram => "telegram",
        NotificationChannel.Teams => "teams",
        _ => "text",
    };

    public RenderedNotification Render(NotificationType type, NotificationChannel channel, NotificationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var template = Find(type, channel) ?? throw new InvalidOperationException($"No notification template for {type} on {channel}; expected {Path.Combine(_root, $"{Family(type)}.{ChannelName(channel)}.sbn")} or a default.");
        var context = new TemplateContext { MemberRenamer = member => member.Name, EnableRelaxedMemberAccess = true, LoopLimit = 10_000 };
        var globals = new ScriptObject();
        globals.Import(model, renamer: member => member.Name);
        context.PushGlobal(globals);
        var text = template.Render(context).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        var subject = (newline < 0 ? text : text[..newline]).Trim();
        var body = (newline < 0 ? string.Empty : text[(newline + 1)..]).Trim();
        if (subject.Length == 0)
        {
            throw new InvalidOperationException($"Template {Family(type)}.{ChannelName(channel)} rendered an empty subject line.");
        }

        return new RenderedNotification(subject, body.Length == 0 ? subject : body);
    }

    public bool Exists(NotificationType type, NotificationChannel channel) => Find(type, channel) is not null;

    private Template? Find(NotificationType type, NotificationChannel channel)
    {
        var channelName = ChannelName(channel);
        foreach (var name in new[] { $"{Family(type)}.{channelName}", $"default.{channelName}", "default.text" })
        {
            var template = _cache.GetOrAdd(name, Load);
            if (template is not null)
            {
                return template;
            }
        }

        return null;
    }

    private Template? Load(string name)
    {
        var path = Path.Combine(_root, name + ".sbn");
        if (!File.Exists(path))
        {
            return null;
        }

        var template = Template.Parse(File.ReadAllText(path), path);
        if (template.HasErrors)
        {
            throw new InvalidOperationException($"Template {path} has errors: {string.Join("; ", template.Messages)}");
        }

        return template;
    }
}

public sealed record RenderedNotification(string Subject, string Body);
