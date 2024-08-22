using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Storage.Azure;

namespace Arius.Core.New;

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

        services.AddSingleton<IStorageAccountFactory, AzureStorageAccountFactory>();


        return services;
    }
}