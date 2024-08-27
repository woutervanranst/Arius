using Arius.Core.New.Queries.GetStateDbVersions;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public class GetRepositoryVersionsQueryHandlerTests : TestBase
{
    protected override IAriusFixture ConfigureFixture()
    {
        return new MockAriusFixture();
    }

    [Fact]
    public async Task Handle_ShouldReturnRepositoryVersions()
    {
        // Arrange
        GivenAzureRepositoryWithVersions("v1.0", "v2.0");

        var request = new GetRepositoryVersionsQuery
        {
            Repository = Fixture.RepositoryOptions
        };

        // Act
        var result = await WhenMediatorRequest(request).ToListAsync();

        // Assert
        result.Select(r => r.Name).Should().ContainInOrder("v1.0", "v2.0");
    }
}