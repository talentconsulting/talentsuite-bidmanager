using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TalentSuite.Functions;

public static class Extensions
{
    public static IServiceCollection AddEmailConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(options =>
        {
            var prefix = $"{EmailOptions.SectionName}:";
            var enabledValue = ResolveConfigValue(configuration,
                $"{prefix}Enabled",
                $"{EmailOptions.SectionName}__Enabled");
            options.Enabled = bool.TryParse(enabledValue, out var enabled) && enabled;

            options.FrontendBaseUrl = ResolveConfigValue(configuration,
                $"{prefix}FrontendBaseUrl",
                $"{EmailOptions.SectionName}__FrontendBaseUrl",
                "Parameters:InviteFrontendBaseUrl",
                "Parameters__InviteFrontendBaseUrl") ?? options.FrontendBaseUrl;

            options.FromEmail = ResolveConfigValue(configuration,
                $"{prefix}FromEmail",
                $"{EmailOptions.SectionName}__FromEmail",
                "Parameters:InviteFromEmail",
                "Parameters__InviteFromEmail") ?? options.FromEmail;

            options.FromDisplayName = ResolveConfigValue(configuration,
                $"{prefix}FromDisplayName",
                $"{EmailOptions.SectionName}__FromDisplayName",
                "Parameters:InviteFromDisplayName",
                "Parameters__InviteFromDisplayName") ?? options.FromDisplayName;

            options.SmtpHost = ResolveConfigValue(configuration,
                $"{prefix}SmtpHost",
                $"{EmailOptions.SectionName}__SmtpHost",
                "Parameters:InviteSmtpHost",
                "Parameters__InviteSmtpHost") ?? options.SmtpHost;

            var smtpPortValue = ResolveConfigValue(configuration,
                $"{prefix}SmtpPort",
                $"{EmailOptions.SectionName}__SmtpPort",
                "Parameters:InviteSmtpPort",
                "Parameters__InviteSmtpPort");
            options.SmtpPort = int.TryParse(smtpPortValue, out var port)
                ? port
                : options.SmtpPort;

            var smtpEnableSslValue = ResolveConfigValue(configuration,
                $"{prefix}SmtpEnableSsl",
                $"{EmailOptions.SectionName}__SmtpEnableSsl",
                "Parameters:InviteSmtpEnableSsl",
                "Parameters__InviteSmtpEnableSsl");
            options.SmtpEnableSsl = bool.TryParse(smtpEnableSslValue, out var ssl)
                ? ssl
                : options.SmtpEnableSsl;

            options.SmtpUsername = ResolveConfigValue(configuration,
                $"{prefix}SmtpUsername",
                $"{EmailOptions.SectionName}__SmtpUsername",
                "Parameters:InviteSmtpUsername",
                "Parameters__InviteSmtpUsername") ?? options.SmtpUsername;

            options.SmtpPassword = ResolveConfigValue(configuration,
                $"{prefix}SmtpPassword",
                $"{EmailOptions.SectionName}__SmtpPassword",
                "Parameters:InviteSmtpPassword",
                "Parameters__InviteSmtpPassword") ?? options.SmtpPassword;
        });

        return services;
    }

    private static string? ResolveConfigValue(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
