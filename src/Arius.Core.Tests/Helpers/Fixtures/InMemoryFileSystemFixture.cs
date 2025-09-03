using Arius.Core.Shared.FileSystem;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests.Helpers.Fixtures;

public class InMemoryFileSystemFixture : FixtureBase
{
    public override IFileSystem FileSystem { get; }

    public InMemoryFileSystemFixture()
    {
        var mfs = new MemoryFileSystem();
        FileSystem = new FilePairFileSystem(mfs);
    }

    public override void Dispose()
    {
        FileSystem.Dispose();
    }
}