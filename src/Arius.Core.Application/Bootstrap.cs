using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Arius.Core.Application;

public static class Bootstrap
{
    public static IServiceCollection AddArius(this IServiceCollection services)
    {
        services.AddMediatR(config => config.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        return services;
    }
}