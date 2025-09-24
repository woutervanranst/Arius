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
            .WithActualFile(FilePairType.PointerFileOnly, "/folder with space/file on disk and staterepo 1.txt")
            .WithRandomContent(10, 1)
            .Build();

        var file2 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, "/folder 2/subfolder with space/file on disk 2.txt")
            .WithRandomContent(10, 2)
            .Build();

        var file3 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.BinaryFileOnly, "/folder 2/subfolder/file on disk.txt")
            .WithRandomContent(10, 3)
            .Build();

        var file4 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, "/file on disk and staterepo 4.txt")
            .WithRandomContent(10, 4)
            .Build();


        // Create a real StateRepository using fixture state cache
        var stateRepository = new StateRepositoryBuilder()
            .WithBinaryProperty(file1.OriginalHash, file1.OriginalContent.Length, pfes =>
            {
                pfes.WithPointerFileEntry("/folder with space/file on disk and staterepo 1.txt");
            })
            // file2 does not exist
            //.WithBinaryProperty(file2.OriginalHash, file2.OriginalContent.Length, pfes =>
            //{
            //    pfes.WithPointerFileEntry("/folder/subfolder/file2.txt");
            //})
            .WithBinaryProperty(file4.OriginalHash, file4.OriginalContent.Length, pfes =>
            {
                pfes.WithPointerFileEntry("/file on disk and staterepo 4.txt");
            })
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

        // If we get here, we expect certain results
        results.OfType<Directory>().ShouldContain(x => x.RelativeName == "/folder with space/");
        results.OfType<Directory>().ShouldContain(x => x.RelativeName == "/folder 2/");
        results.OfType<File>().ShouldContain(x => x.RelativeName == "/folder/file1.txt.pointer.arius");
        results.OfType<File>().ShouldContain(x => x.RelativeName == "/folder/subfolder/file2.txt.pointer.arius");
        results.OfType<File>().ShouldContain(x => x.RelativeName == "/other/file3.txt.pointer.arius");
    }
}