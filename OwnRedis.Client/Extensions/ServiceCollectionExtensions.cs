using Microsoft.Extensions.DependencyInjection;
using OwnRedis.Core.Inrerfaces;

namespace OwnRedis.Client.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOwnRedis(this IServiceCollection services, string serverUrl)
    {
        services.AddHttpClient<ICacheMethodsService, OwnRedisClient>(client =>
        {
            client.BaseAddress = new Uri(serverUrl);
        });

        return services;
    }
}