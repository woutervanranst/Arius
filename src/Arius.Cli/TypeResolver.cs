using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Arius.Cli;

public class TypeResolver : ITypeResolver, IDisposable
{
    private readonly ServiceProvider _provider;

    public TypeResolver(ServiceProvider provider)
    {
        _provider = provider;
    }

    public object Resolve(Type type)
    {
        return _provider.GetService(type);
    }

    public void Dispose()
    {
        _provider.Dispose();
    }
}