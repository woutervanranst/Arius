using Arius.Core.Application.Commands;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Arius.Core.Application.Tests;

public class ArchiveCommandHandlerTests : IClassFixture<ApplicationTestFixture>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMediator        _mediator;

    public ArchiveCommandHandlerTests(ApplicationTestFixture fixture)
    {
        _serviceProvider = fixture.ServiceProvider;
        _mediator        = _serviceProvider.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_Should_ArchiveFile()
    {
        // Arrange
        var archiveCommand = new ArchiveCommand { FilePath = "test.txt" };

        // Act
        var result = await _mediator.Send(archiveCommand);

        // Assert
        Assert.Equal(Unit.Value, result);
    }

    [Fact]
    public async Task Handle_Should_Throw_Exception_For_Invalid_FilePath()
    {
        // Arrange
        var archiveCommand = new ArchiveCommand { FilePath = string.Empty };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() => _mediator.Send(archiveCommand));
        Assert.Contains("FilePath cannot be empty.", exception.Message);
    }
}