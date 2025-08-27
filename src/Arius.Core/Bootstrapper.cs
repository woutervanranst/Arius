using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;

namespace Arius.Core;

public record AriusConfiguration
{
    [Range(0, 10, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
    public int MaxTokens { get; set; } = 10; // Default value
}

public static class Bootstrapper
{
    public static IServiceCollection AddArius(this IServiceCollection services, Action<AriusConfiguration> configureOptions)
    {
        services.AddOptions<AriusConfiguration>()
            .Configure(configureOptions)
            .ValidateDataAnnotations()
            .ValidateOnStart(); // this will validate & throw when the DI container resolves it OR on app.Run();

        //services.AddAzureClients(builder => // add Extensions.Azure
        //{
        //    builder.AddBlobServiceClient()
        //});

        // Add FluentValidation validators
        //services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly()); // zie https://www.milanjovanovic.tech/blog/cqrs-validation-with-mediatr-pipeline-and-fluentvalidation#running-validation-from-the-use-case

        // Add Mediator
        services.AddMediator();

        //services.AddSingleton<IStorageAccountFactory, AzureStorageAccountFactory>();
        //services.AddSingleton<AzureContainerFactory>();
        //services.AddSingleton<AzureRemoteRepositoryFactory>();

        //services.AddSingleton<ICryptoService, CryptoService>();
        //services.AddSingleton<IFileSystem, LocalFileSystem>();
        //services.AddSingleton<PointerFileSerializer>();

        return services;
    }
}

