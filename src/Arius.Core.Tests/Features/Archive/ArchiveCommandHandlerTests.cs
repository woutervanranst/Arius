using Arius.Core.Features.Archive;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using System.Runtime.InteropServices;

namespace Arius.Core.Tests.Features.Archive;

public class ArchiveCommandHandlerTests : IClassFixture<PhysicalFileSystemFixture>, IDisposable
{
    private readonly PhysicalFileSystemFixture        fixture;
    private readonly FakeLogger<ArchiveCommandHandler> logger;
    private readonly ArchiveCommandHandler             handler;

    public ArchiveCommandHandlerTests(PhysicalFileSystemFixture fixture)
    {
        this.fixture = fixture;
        logger       = new();
        handler      = new ArchiveCommandHandler(logger, NullLoggerFactory.Instance, fixture.AriusConfiguration);
    }


    [Fact]
    public async Task RunArchiveCommand() // NOTE this one is skipped in CI
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

    [Fact(Skip = "TODO")]

    public void UpdatedCreationTimeOrLastWriteTimeShouldBeUpdatedInStateDatabase()
    {

    }

    public void Dispose()
    {
        fixture?.Dispose();
    }
}