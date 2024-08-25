using Arius.Core.Domain.Storage;
using Arius.Core.New.Queries.GetStateDbVersions;
using FluentAssertions;
using NSubstitute;

namespace Arius.Core.New.UnitTests;

public class GetRepositoryVersionsQueryHandlerTests : IClassFixture<CommandHandlerFixture>
{
    private readonly CommandHandlerFixture fixture;

    public GetRepositoryVersionsQueryHandlerTests(CommandHandlerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task Handle_ShouldReturnRepositoryVersions(ServiceConfiguration configuration)
    {
        // Arrange
        var storageAccountFactory = fixture.GetStorageAccountFactory(configuration);
        var mediator              = fixture.GetMediator(configuration);
        var storageAccount        = Substitute.For<IRepository>();

        var request = new GetRepositoryVersionsQuery
        {
            Repository = fixture.GetRepositoryOptions(configuration)
        };

        if (configuration == ServiceConfiguration.Mocked)
        {
            var repositoryVersions = new List<RepositoryVersion>
            {
                new() { Name = "v1.0" },
                new() { Name = "v2.0" }
            };

            storageAccountFactory.GetRepository(Arg.Any<RepositoryOptions>()).Returns(storageAccount);
            storageAccount.GetRepositoryVersions().Returns(repositoryVersions.ToAsyncEnumerable());
        }

        // Act
        var result = await mediator.CreateStream(request).ToListAsync();

        // Assert
        if (configuration == ServiceConfiguration.Mocked)
        {
            result.Select(r => r.Name).Should().ContainInOrder("v1.0", "v2.0");
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}