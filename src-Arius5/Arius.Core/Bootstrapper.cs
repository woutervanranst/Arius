using Microsoft.Extensions.DependencyInjection;

namespace Arius.Core;

public record AriusConfiguration();

public static class Bootstrapper
{
    public static IServiceCollection AddArius(this IServiceCollection services, Action<AriusConfiguration> configureOptions)
    {
        services.Configure(configureOptions); // TODO add validation

        // Add FluentValidation validators
        //services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly()); // zie https://www.milanjovanovic.tech/blog/cqrs-validation-with-mediatr-pipeline-and-fluentvalidation#running-validation-from-the-use-case

        // Add MediatR
        services.AddMediatR(config =>
        {
            // Add Handlers
            config.RegisterServicesFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());

            // Add pipeline validation behavior
            //config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        //services.AddSingleton<IStorageAccountFactory, AzureStorageAccountFactory>();
        //services.AddSingleton<AzureContainerFactory>();
        //services.AddSingleton<AzureRemoteRepositoryFactory>();

        //services.AddSingleton<ICryptoService, CryptoService>();
        //services.AddSingleton<IFileSystem, LocalFileSystem>();
        //services.AddSingleton<PointerFileSerializer>();

        return services;
    }
}

