using Arius.Core.Models;
using Arius.Core.Services;
using Shouldly;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests.Services;

public class Sha256HasherTest
{
    [Fact]
    public async Task GetHashAsync_Bytes_ProducesConsistentHash()
    {
        var hasher = new Sha256Hasher("woutervanranst");

        var zeroBytes = new byte[1024];

        var hash1 = await hasher.GetHashAsync(zeroBytes);

        var arius3HasherHash = (Hash)"88e667ac1167d96e7c42ec65daf9c096d374263a313c60b6d307ec3938300f98";
        hash1.ShouldBe(arius3HasherHash);
    }

    [Fact]
    public async Task GetHashAsync_Stream_ProducesConsistentHash()
    {
        var hasher = new Sha256Hasher("woutervanranst");

        var zeroBytes = new byte[1024];

        var f  = Path.GetTempFileName();
        File.WriteAllBytes(f, zeroBytes);

        var fs = new PhysicalFileSystem();
        var p = fs.ConvertPathFromInternal(f);
        var bf = BinaryFile.FromFileEntry(new FileEntry(new PhysicalFileSystem(), p));

        var hash1 = await hasher.GetHashAsync(bf);

        var arius3HasherHash = (Hash)"88e667ac1167d96e7c42ec65daf9c096d374263a313c60b6d307ec3938300f98";
        hash1.ShouldBe(arius3HasherHash);
    }
}