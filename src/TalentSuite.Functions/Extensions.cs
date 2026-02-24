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
            options.Enabled = bool.TryParse(configuration[$"{prefix}Enabled"], out var enabled) && enabled;
            options.FrontendBaseUrl = configuration[$"{prefix}FrontendBaseUrl"] ?? options.FrontendBaseUrl;
            options.FromEmail = configuration[$"{prefix}FromEmail"] ?? options.FromEmail;
            options.FromDisplayName = configuration[$"{prefix}FromDisplayName"] ?? options.FromDisplayName;
            options.SmtpHost = configuration[$"{prefix}SmtpHost"] ?? options.SmtpHost;
            options.SmtpPort = int.TryParse(configuration[$"{prefix}SmtpPort"], out var port)
                ? port
                : options.SmtpPort;
            options.SmtpEnableSsl = bool.TryParse(configuration[$"{prefix}SmtpEnableSsl"], out var ssl)
                ? ssl
                : options.SmtpEnableSsl;
            options.SmtpUsername = configuration[$"{prefix}SmtpUsername"] ?? options.SmtpUsername;
            options.SmtpPassword = configuration[$"{prefix}SmtpPassword"] ?? options.SmtpPassword;
        });

        return services;
    }
}
