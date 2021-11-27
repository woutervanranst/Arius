using System;
using Spectre.Console.Cli;

namespace Arius.Cli.Infrastructure
{
    public sealed class TypeResolver : ITypeResolver, IDisposable
    {
        private readonly IServiceProvider provider;

        public TypeResolver(IServiceProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public object Resolve(Type type)
        {
            if (type == null)
            {
                return null;
            }

            /* NOTE: as of 20/11/21 -- when receiving a System.MethodAccessException: 'Attempt by method 'Microsoft.Extensions.Logging.Configuration.LoggerProviderConfigurationFactory.GetConfiguration(System.Type)' to access method 'Microsoft.Extensions.Logging.ProviderAliasUtilities.GetAlias(System.Type)' failed.'
             * This is due to a package version conflict between
             *      Karambolo.Extensions.Logging.File:3.2.1
             * and 
             *      Microsoft.Extensions.Configuration.Json:6.0.0
             *      Microsoft.Extensions.DependencyInjection:6.0.0
             *      Microsoft.Extensions.Logging:6.0.0
             *
             * Use Microsoft.Extensions.Hosting:6.0.0 instead
             */

            return provider.GetService(type);
        }

        public void Dispose()
        {
            if (provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}