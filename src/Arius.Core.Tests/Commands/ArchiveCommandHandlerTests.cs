using Arius.Core.Commands;
using Arius.Core.Tests.Builders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using System.Runtime.InteropServices;

namespace Arius.Core.Tests.Commands;

public class ArchiveCommandHandlerTests : IDisposable
{
    private readonly FakeLogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandHandler             handler;
    private readonly Fixture                           fixture;

    public ArchiveCommandHandlerTests()
    {
        fixture = new ();
        logger  = new();
        handler = new ArchiveCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }



    [Fact]
    public async Task RunArchiveCommand()
    {
        var logger = new FakeLogger<ArchiveCommandHandler>();

        // TODO Make this better
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var c = new ArchiveCommandBuilder(fixture)
            .WithLocalRoot(isWindows ? 
                new DirectoryInfo("C:\\Users\\WouterVanRanst\\Downloads\\Photos-001 (1)") : 
                new DirectoryInfo("/mnt/c/Users/WouterVanRanst/Downloads/Photos-001 (1)"))
            .Build();
        await handler.Handle(c, CancellationToken.None);

    }

    public void Dispose()
    {
        fixture?.Dispose();
    }
}