using Microsoft.Extensions.DependencyInjection;

namespace Arius.Core.Application.Tests;

public class ApplicationTestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; private set; }

    public ApplicationTestFixture()
    {
        var services = new ServiceCollection();
        services.AddArius();

        ServiceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}