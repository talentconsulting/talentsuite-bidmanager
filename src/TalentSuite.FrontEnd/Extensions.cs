using TalentSuite.FrontEnd.Mappers;

namespace TalentSuite.FrontEnd;

public static class Extensions
{
    public static IServiceCollection AddBidMappings(this IServiceCollection services)
    {
        services.AddSingleton<BidMapper>();

        return services;
    }
}
