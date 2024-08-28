using Arius.Core.Domain.Storage;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using Arius.Core.New.Commands.Archive;
using Arius.Core.New.UnitTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.New.UnitTests;

public class ArchiveCommandTests : TestBase
{
    protected override IAriusFixture ConfigureFixture()
    {
        return FixtureBuilder.Create()
            //.WithMockedStorageAccountFactory()
            //.WithFakeCryptoService()
            .WithPopulatedSourceFolder()
            .Build();
    }

    [Fact]
    public async Task Handle()
    {
        // Arrange
        var c = new ArchiveCommand
        {
            Repository  = Fixture.RepositoryOptions,
            FastHash    = false,
            RemoveLocal = false,
            Tier        = StorageTier.Hot,
            LocalRoot   = Fixture.SourceFolder,
            VersionName = new RepositoryVersion { Name = "v1.0" }
        };

        // Act
        await WhenMediatorRequest(c);

        // Assert
    }
}

public class ArchiveCommandBlocks_Index_Tests : TestBase
{
    protected override IAriusFixture ConfigureFixture()
    {
        return FixtureBuilder.Create()
            .WithSourceFolderHaving(
                new FixtureBuilder.FileDescription("file1.bin", 0, FileAttributes.Normal),                            // Binary file without a pointer
                
                new FixtureBuilder.FileDescription("file2.bin.pointer.arius", 0, FileAttributes.Normal),                    // Pointer file without a binary file
                
                new FixtureBuilder.FileDescription("file3.bin", 0, FileAttributes.Normal),                            // Binary file with a matching pointer file
                new FixtureBuilder.FileDescription("file3.bin.pointer.arius", 0, FileAttributes.Normal),                    // Pointer file with a matching binary file
                
                new FixtureBuilder.FileDescription("folder1/file4.bin", 0, FileAttributes.Normal),                    // Binary file in a folder
                
                new FixtureBuilder.FileDescription("folder2/file4.bin.pointer.arius", 0, FileAttributes.Normal)             // Pointer file with the same name in another folder
            )
            .Build();
    }

    [Fact]
    public void Index_StagedFiles_AsExpected()
    {
        // Arrange
        var fs = new LocalFileSystem(NullLogger<LocalFileSystem>.Instance);
        
        // Act
        var stagedFiles = ArchiveCommandHandler.IndexFiles(fs, Fixture.SourceFolder).ToList();

    }
}