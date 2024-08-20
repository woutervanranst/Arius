using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Arius.Core.Facade;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddArius(this IServiceCollection services)
    {
        // Add FluentValidation validators
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // Add MediatR
        services.AddMediatR(config =>
        {
            // Add Handlers
            config.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());

            // Add pipeline validation behavior
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });


        return services;
    }
}