using System;
using Spectre.Console.Cli;

namespace Arius.Cli.Utils;

internal sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly IServiceProvider provider;

    public TypeResolver(IServiceProvider provider)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public object Resolve(Type type)
    {
        if (type == null)
            return null;

        return provider.GetService(type);
    }

    public void Dispose()
    {
        if (provider is IDisposable disposable)
            disposable.Dispose();
    }
}