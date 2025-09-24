using Arius.Core.Shared.FileSystem;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;
using Zio;

namespace Arius.Core.Tests.Shared.FileSystem;

public class BinaryFileTests : IClassFixture<FixtureWithFileSystem>
{
    private readonly FixtureWithFileSystem fixture;

    public BinaryFileTests(FixtureWithFileSystem fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void OpenWrite_Should_Truncate()
    {
        var       bf = BinaryFile.FromFileEntry(new FileEntry(fixture.FileSystem, "/test"));

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