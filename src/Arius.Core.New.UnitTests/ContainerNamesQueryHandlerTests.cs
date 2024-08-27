using Arius.Core.New.Queries.ContainerNames;
using Arius.Core.New.UnitTests.Fixtures;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public class ContainerNamesQueryHandlerTests : TestBase
{
    protected override IAriusFixture ConfigureFixture()
    {
        return FixtureBuilder.Create()
            .WithMockedStorageAccountFactory()
            .WithFakeCryptoService()
            .Build();
    }

    [Fact]
    public async Task Handle_ShouldReturnContainerNames()
    {
        // Arrange
        GivenAzureStorageAccountWithContainers("container1", "container2");

        var request = new ContainerNamesQuery
        {
            StorageAccount = Fixture.StorageAccountOptions
        };

        // Act
        var result = await WhenMediatorRequest(request).ToListAsync();

        // Assert
        result.Should().ContainInOrder("container1", "container2");
    }
}