using Microsoft.Extensions.DependencyInjection;

namespace TalentSuite.Functions.InviteEmail;

public static class Extensions
{
    public static IServiceCollection AddInviteEmail(this IServiceCollection services)
    {
        services.AddSingleton<IInviteEmailSender, SmtpInviteEmailSender>();
        return services;
    }
}
