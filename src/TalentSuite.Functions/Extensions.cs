using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalentSuite.Functions.GoogleDriveSync;

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
                "FRONTEND_PUBLIC_ORIGIN") ?? options.FrontendBaseUrl;

            options.FromEmail = ResolveConfigValue(configuration,
                $"{prefix}FromEmail",
                $"{EmailOptions.SectionName}__FromEmail") ?? options.FromEmail;

            options.FromDisplayName = ResolveConfigValue(configuration,
                $"{prefix}FromDisplayName",
                $"{EmailOptions.SectionName}__FromDisplayName") ?? options.FromDisplayName;

            options.SmtpHost = ResolveConfigValue(configuration,
                $"{prefix}SmtpHost",
                $"{EmailOptions.SectionName}__SmtpHost") ?? options.SmtpHost;

            var smtpPortValue = ResolveConfigValue(configuration,
                $"{prefix}SmtpPort",
                $"{EmailOptions.SectionName}__SmtpPort");
            options.SmtpPort = int.TryParse(smtpPortValue, out var port)
                ? port
                : options.SmtpPort;

            var smtpEnableSslValue = ResolveConfigValue(configuration,
                $"{prefix}SmtpEnableSsl",
                $"{EmailOptions.SectionName}__SmtpEnableSsl");
            options.SmtpEnableSsl = bool.TryParse(smtpEnableSslValue, out var ssl)
                ? ssl
                : options.SmtpEnableSsl;

            options.SmtpUsername = ResolveConfigValue(configuration,
                $"{prefix}SmtpUsername",
                $"{EmailOptions.SectionName}__SmtpUsername") ?? options.SmtpUsername;

            options.SmtpPassword = ResolveConfigValue(configuration,
                $"{prefix}SmtpPassword",
                $"{EmailOptions.SectionName}__SmtpPassword") ?? options.SmtpPassword;
        });

        return services;
    }

    public static IServiceCollection AddGoogleDriveSyncConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GoogleDriveSyncOptions>(options =>
        {
            var prefix = $"{GoogleDriveSyncOptions.SectionName}:";
            var enabledValue = ResolveConfigValue(configuration,
                $"{prefix}Enabled",
                $"{GoogleDriveSyncOptions.SectionName}__Enabled");
            options.Enabled = bool.TryParse(enabledValue, out var enabled) && enabled;

            options.SourceContainerName = ResolveConfigValue(configuration,
                $"{prefix}SourceContainerName",
                $"{GoogleDriveSyncOptions.SectionName}__SourceContainerName")
                ?? options.SourceContainerName;

            options.DriveFolderId = ResolveConfigValue(configuration,
                $"{prefix}DriveFolderId",
                $"{GoogleDriveSyncOptions.SectionName}__DriveFolderId")
                ?? options.DriveFolderId;

            options.ServiceAccountJson = ResolveConfigValue(configuration,
                $"{prefix}ServiceAccountJson",
                $"{GoogleDriveSyncOptions.SectionName}__ServiceAccountJson");

            options.ServiceAccountJsonBase64 = ResolveConfigValue(configuration,
                $"{prefix}ServiceAccountJsonBase64",
                $"{GoogleDriveSyncOptions.SectionName}__ServiceAccountJsonBase64");
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
