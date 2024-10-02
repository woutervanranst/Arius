using Arius.Core.Domain;
using Arius.Core.Domain.Repositories;
using Arius.Core.Domain.Services;
using Arius.Core.Domain.Storage;
using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Repositories;
using Arius.Core.Infrastructure.Storage.Azure;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Arius.Core.New;

public static class IServiceCollectionExtensions
{
    public static IServiceCollection AddArius(this IServiceCollection services, Action<AriusConfiguration> configureOptions)
    {
        services.Configure(configureOptions); // TODO add validation

        // Add FluentValidation validators
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // Add MediatR
        services.AddMediatR(config =>
        {
            // Add Handlers
            config.RegisterServicesFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());

            // Add pipeline validation behavior
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddSingleton<IStorageAccountFactory, AzureStorageAccountFactory>();
        services.AddSingleton<AzureContainerFactory>();
        services.AddSingleton<IRemoteStateRepository, SqliteRemoteStateRepository>();
        services.AddSingleton<AzureRemoteRepositoryFactory>();

        services.AddSingleton<ICryptoService, CryptoService>();
        services.AddSingleton<IFileSystem, LocalFileSystem>();

        return services;
    }
}