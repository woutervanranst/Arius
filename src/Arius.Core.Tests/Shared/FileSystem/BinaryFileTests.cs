using Arius.Core.Shared.FileSystem;
using Shouldly;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests.Shared.FileSystem;

public class BinaryFileTests
{
    [Fact]
    public void OpenWrite_Should_Truncate()
    {
        using var ms = new MemoryFileSystem();
        var       bf = BinaryFile.FromFileEntry(new FileEntry(ms, "/test"));

        bf.WriteAllText("long text");

        using (var s = bf.OpenWrite(1024))
        {
            s.WriteByte(44);
        }

        var x = bf.ReadAllBytes();

        x.Length.ShouldBe(1);
        x[0].ShouldBe((byte)44);
    }
}