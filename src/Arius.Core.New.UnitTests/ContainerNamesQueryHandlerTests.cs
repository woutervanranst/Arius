using Arius.Core.Domain.Storage;
using Arius.Core.New.Queries.ContainerNames;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;
using NSubstitute;

namespace Arius.Core.New.UnitTests;

public class ContainerNamesQueryHandlerTests : IClassFixture<RequestHandlerFixture>
{
    private readonly RequestHandlerFixture fixture;

    public ContainerNamesQueryHandlerTests(RequestHandlerFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(ServiceConfiguration.Mocked)]
    [InlineData(ServiceConfiguration.Real)]
    public async Task Handle_ShouldReturnContainerNames(ServiceConfiguration configuration)
    {
        // Arrange
        var storageAccountFactory = fixture.GetStorageAccountFactory(configuration);
        var mediator              = fixture.GetMediator(configuration);
        var storageAccount        = Substitute.For<IStorageAccount>();

        var request = new ContainerNamesQuery
        {
            StorageAccount = fixture.GetStorageAccountOptions(configuration)
        };

        if (configuration == ServiceConfiguration.Mocked)
        {
            var containers = new List<IContainer>
            {
                Substitute.For<IContainer>(),
                Substitute.For<IContainer>(),
            };

            containers[0].Name.Returns("container1");
            containers[1].Name.Returns("container2");

            storageAccountFactory.GetStorageAccount(Arg.Any<StorageAccountOptions>()).Returns(storageAccount);
            storageAccount.GetContainers(Arg.Any<CancellationToken>()).Returns(containers.ToAsyncEnumerable());
        }

        // Act
        var result  = await mediator.CreateStream(request).ToListAsync();
        
        // Assert
        if (configuration == ServiceConfiguration.Mocked)
        {
            result.Should().ContainInOrder("container1", "container2");
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}