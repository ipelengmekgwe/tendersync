using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TenderSync.Application.Abstractions.Notifications;
using TenderSync.Application.Features.Notifications;

namespace TenderSync.Infrastructure.Notifications;

public static class NotificationServiceExtensions
{
    /// <summary>
    /// One <see cref="INotificationChannel"/> per channel enum value (PLAN.md §7.6) plus the SMS and WhatsApp senders. Credentials are
    /// read when a channel sends, never at registration, so hosts and tests start without any of them.
    /// </summary>
    public static IServiceCollection AddNotificationChannels(this IServiceCollection services, IConfiguration? configuration)
    {
        var behaviour = services.AddOptions<NotificationOptions>();
        var channels = services.AddOptions<NotificationChannelOptions>();
        if (configuration is not null)
        {
            behaviour.Bind(configuration.GetSection(NotificationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
            channels.Bind(configuration.GetSection(NotificationChannelOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        }

        foreach (var name in new[] { TelegramChannel.ClientName, TeamsChannel.ClientName, ClickatellSmsSender.ClientName })
        {
            services.AddHttpClient(name, client => client.Timeout = TimeSpan.FromSeconds(30)).AddStandardResilienceHandler();
        }

        services.AddSingleton<ISmsSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<NotificationChannelOptions>>();
            return options.Value.Sms.Provider.Equals(NotificationChannelOptions.SmsOptions.ClickatellProvider, StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<ClickatellSmsSender>(provider)
                : new NoOpSmsSender();
        });
        services.AddSingleton<IWhatsAppSender, NoOpWhatsAppSender>();

        services.AddSingleton<INotificationChannel, EmailChannel>();
        services.AddSingleton<INotificationChannel, TelegramChannel>();
        services.AddSingleton<INotificationChannel, TeamsChannel>();
        services.AddSingleton<INotificationChannel, SmsChannel>();
        services.AddSingleton<INotificationChannel, WhatsAppChannel>();
        return services;
    }
}
