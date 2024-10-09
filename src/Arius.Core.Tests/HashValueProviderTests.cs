using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.Tests.Extensions;
using Arius.Core.Tests.Fixtures;
using FluentAssertions;

namespace Arius.Core.Tests;

public class HashValueProviderTests : TestBase
{
    protected override AriusFixture GetFixture()
    {
        return new FixtureBuilder()
            .WithRealStorageAccountFactory()
            .WithUniqueContainerName()
            .Build();
    }

    protected override void ConfigureOnceForFixture()
    {
        GivenPopulatedSourceFolder();
    }

    [Fact]
    public async Task GetHashAsync_ShouldProvideConsistentHashes()
    {
        var file = Fixture.TestRunRootFolder.GetFileFullName("testfile");
        FileUtils.CreateZeroFile(file, 1024);

        var salt      = "woutervanranst";

        //var arius3Hasher = new Arius.Core.Services.SHA256Hasher(salt);
        //var arius3HasherHash = arius3Hasher.GetBinaryHash(file).Value.BytesToHexString();

        var arius3HasherHash = "88e667ac1167d96e7c42ec65daf9c096d374263a313c60b6d307ec3938300f98";

        var arius4Hasher = new Arius.Core.Infrastructure.Services.SHA256Hasher(salt);
        var arius4HasherHash     = (await arius4Hasher.GetHashAsync(BinaryFile.FromFullName(null, file))).Value.BytesToHexString();

        arius3HasherHash.Should().Be(arius4HasherHash);
    }
}