using Arius.Core.Features.Queries.PointerFileEntries;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.StateRepositories;
using Arius.Core.Tests.Helpers.Builders;
using Arius.Core.Tests.Helpers.FakeLogger;
using Arius.Core.Tests.Helpers.Fakes;
using Arius.Core.Tests.Helpers.Fixtures;
using Shouldly;

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
            .WithActualFile(FilePairType.PointerFileOnly, "/folder/file1.txt")
            .WithRandomContent(10, 1)
            .Build();

        var file2 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, "/folder/subfolder/file2.txt")
            .WithRandomContent(10, 2)
            .Build();

        var file3 = new FakeFileBuilder(fixture)
            .WithActualFile(FilePairType.PointerFileOnly, "/other/file3.txt")
            .WithRandomContent(10, 3)
            .Build();

        // Create a real StateRepository using fixture state cache
        var stateRepository = new StateRepositoryBuilder()
            .WithBinaryProperty(file1.OriginalHash, 100, pfes =>
            {
                pfes.WithPointerFileEntry("/folder/file1.txt");
            })
            .WithBinaryProperty(file2.OriginalHash, 200, pfes =>
            {
                pfes.WithPointerFileEntry("/folder/subfolder/file2.txt");
            })
            .WithBinaryProperty(file3.OriginalHash, 300, pfes =>
            {
                pfes.WithPointerFileEntry("/other/file3.txt");
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
        results.ShouldContain("/folder/file1.txt.pointer.arius");
        results.ShouldContain("/folder/subfolder/file2.txt.pointer.arius");
        results.ShouldContain("/other/file3.txt.pointer.arius");
    }
}