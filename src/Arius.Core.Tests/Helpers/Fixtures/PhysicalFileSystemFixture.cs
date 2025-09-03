using Arius.Core.Shared.FileSystem;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests.Helpers.Fixtures;

public class PhysicalFileSystemFixture : FixtureBase
{
    public override IFileSystem FileSystem { get; }
    public DirectoryInfo TestRunSourceFolder { get; }

    public PhysicalFileSystemFixture()
    {
        TestRunSourceFolder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "Arius.Core.Tests", $"{DateTime.Now:yyyyMMddTHHmmss}_{Guid.CreateVersion7()}"));
        TestRunSourceFolder.Create();

        var pfs = new PhysicalFileSystem();
        var sfs = new SubFileSystem(pfs, pfs.ConvertPathFromInternal(TestRunSourceFolder.FullName));
        FileSystem = new FilePairFileSystem(sfs);
    }

    public override void Dispose()
    {
        FileSystem.Dispose();
        if (TestRunSourceFolder.Exists)
        {
            TestRunSourceFolder.Delete(recursive: true);
        }
    }
}