using Microsoft.Extensions.DependencyInjection;

namespace TalentSuite.Functions.CommentEmail;

public static class Extensions
{
    public static IServiceCollection AddCommentEmail(this IServiceCollection services)
    {
        services.AddSingleton<ICommentMentionEmailSender, SmtpCommentMentionEmailSender>();
        return services;
    }
}
