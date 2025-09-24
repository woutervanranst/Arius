using Arius.Core.Features.Queries.PointerFileEntries;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.FakeLogger;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;
using Directory = Arius.Core.Features.Queries.PointerFileEntries.Directory;
using File = Arius.Core.Features.Queries.PointerFileEntries.File;

namespace Arius.Core.Tests.Features.Queries.PointerFileEntries;

public class PointerFileEntriesQueryHandlerTests : IClassFixture<FixtureWithFileSystem>
{
    private readonly FixtureWithFileSystem fixture;
    private readonly StateCache            stateCache;
    private readonly FakeLoggerFactory     fakeLoggerFactory = new();

    public PointerFileEntriesQueryHandlerTests(FixtureWithFileSystem fixture)
    {
        this.fixture = fixture;
        stateCache   = new StateCache(fixture.RepositoryOptions.AccountName, fixture.RepositoryOptions.ContainerName);
    }

    [Fact]
    public async Task Handle_WithValidRequest_ShouldReturnMatchingPointerFileEntries()
    {
        var handler = new PointerFileEntriesQueryHandler(fakeLoggerFactory);

        // Create actual files on disk using the fixture
        var file1 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.None, "/folder with space/only PointerFileEntry 1.txt")
            .WithRandomContent(10, 1)
            .Build();

        var file2 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, "/folder 2/subfolder with space/PointerFile and PointerFileEntry 2.txt")
            .WithRandomContent(10, 2)
            .Build();

        var file3 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, "/folder 2/BinaryFile and PointerFileEntry 3.txt")
            .WithRandomContent(10, 3)
            .Build();

        var file4 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileWithPointerFile, "/BinaryFile and PointerFile and PointerFileEntry 4.txt")
            .WithRandomContent(10, 4)
            .Build();

        var file5 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, "/PointerFile 5.txt")
            .WithRandomContent(10, 5)
            .Build();

        var file6 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, "/BinaryFile 6.txt")
            .WithRandomContent(10, 6)
            .Build();

        var file7 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileWithPointerFile, "/BinaryFile and PointerFile 7.txt")
            .WithRandomContent(10, 7)
            .Build();

        var file8 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.None, "/None 8.txt")
            .WithRandomContent(10, 8)
            .Build();


        // Create a real StateRepository using fixture state cache
        var stateRepository = new StateRepositoryBuilder()
            .WithBinaryProperty(file1.OriginalHash, file1.OriginalContent.Length, pfes =>
            {
                pfes.WithPointerFileEntry(file1.OriginalPath);
            })
            .WithBinaryProperty(file2.OriginalHash, file2.OriginalContent.Length, pfes =>
            {
                pfes.WithPointerFileEntry(file2.OriginalPath);
            })
            .WithBinaryProperty(file3.OriginalHash, file3.OriginalContent.Length, pfes =>
            {
                pfes.WithPointerFileEntry(file3.OriginalPath);
            })
            .WithBinaryProperty(file4.OriginalHash, file4.OriginalContent.Length, pfes =>
            {
                pfes.WithPointerFileEntry(file4.OriginalPath);
            })
            // Do not add file5, 6, 7, 8
            .Build(stateCache, "test-state");

        // Create mock archive storage that returns our state
        var mockStorage = new MockArchiveStorageBuilder(fixture)
            .Build();

        var query = new PointerFileEntriesQuery
        {
            AccountName   = fixture.RepositoryOptions.AccountName,
            AccountKey    = fixture.RepositoryOptions.AccountKey,
            ContainerName = fixture.RepositoryOptions.ContainerName,
            Passphrase    = fixture.RepositoryOptions.Passphrase,

            LocalPath = fixture.TestRunSourceFolder,
            Prefix    = "/"
        };

        var handlerContext = await new HandlerContextBuilder(query, fakeLoggerFactory)
            .WithArchiveStorage(mockStorage)
            .WithStateRepository(stateRepository)
            .BuildAsync();

        // Act
        var results = await handler.Handle(handlerContext, CancellationToken.None).ToListAsync();

        // Assert
        var directories = results.OfType<Directory>().ToArray();
        directories.ShouldContain(x => x.RelativeName == "/folder with space/");
        directories.ShouldContain(x => x.RelativeName == "/folder 2/");
        directories.Length.ShouldBe(2);

        var files = results.OfType<File>().ToArray();
        files.ShouldContain(x =>
            x.PointerFileEntry == "/BinaryFile and PointerFile and PointerFileEntry 4.txt.pointer.arius" &&
            x.PointerFileName == "/BinaryFile and PointerFile and PointerFileEntry 4.txt.pointer.arius" &&
            x.BinaryFileName == "/BinaryFile and PointerFile and PointerFileEntry 4.txt");
        files.ShouldContain(x => 
            x.PointerFileEntry == null &&
            x.PointerFileName == null &&
            x.BinaryFileName == "/BinaryFile 6.txt");
        files.ShouldContain(x =>
            x.PointerFileEntry == null &&
            x.PointerFileName == "/BinaryFile and PointerFile 7.txt.pointer.arius" &&
            x.BinaryFileName == "/BinaryFile and PointerFile 7.txt");
        files.ShouldContain(x =>
            x.PointerFileEntry == null &&
            x.PointerFileName == "/PointerFile 5.txt.pointer.arius" &&
            x.BinaryFileName == null);
        files.Length.ShouldBe(4);


        // Arrange
        query = query with { Prefix = "/folder 2/subfolder with space/" };

        handlerContext = await new HandlerContextBuilder(query, fakeLoggerFactory)
            .WithArchiveStorage(mockStorage)
            .WithStateRepository(stateRepository)
            .BuildAsync();

        // Act
        results = await handler.Handle(handlerContext, CancellationToken.None).ToListAsync();

        // Assert
        directories = results.OfType<Directory>().ToArray();
        directories.Length.ShouldBe(0);

        files = results.OfType<File>().ToArray();
        files.ShouldContain(x =>
            x.PointerFileEntry == "/folder 2/subfolder with space/PointerFile and PointerFileEntry 2.txt.pointer.arius" &&
            x.PointerFileName == "/folder 2/subfolder with space/PointerFile and PointerFileEntry 2.txt.pointer.arius" &&
            x.BinaryFileName == null);


        // Arrange
        query = query with { Prefix = "/folder 2/" };

        handlerContext = await new HandlerContextBuilder(query, fakeLoggerFactory)
            .WithArchiveStorage(mockStorage)
            .WithStateRepository(stateRepository)
            .BuildAsync();

        // Act
        results = await handler.Handle(handlerContext, CancellationToken.None).ToListAsync();

        // Assert
        directories = results.OfType<Directory>().ToArray();
        directories.ShouldContain(x => x.RelativeName == "/folder 2/subfolder with space/");
        directories.Length.ShouldBe(1);

        files = results.OfType<File>().ToArray();
        files.ShouldContain(x =>
            x.PointerFileEntry == "/folder 2/BinaryFile and PointerFileEntry 3.txt.pointer.arius" &&
            x.PointerFileName == null &&
            x.BinaryFileName == "/folder 2/BinaryFile and PointerFileEntry 3.txt");
    }
}