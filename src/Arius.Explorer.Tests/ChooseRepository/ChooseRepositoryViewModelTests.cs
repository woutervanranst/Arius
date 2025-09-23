using Arius.Core.Features.Queries.ContainerNames;
using Arius.Explorer.ChooseRepository;
using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Extensions;
using Mediator;
using NSubstitute;
using Shouldly;

namespace Arius.Explorer.Tests.ChooseRepository;

public class ChooseRepositoryViewModelTests
{
    [Fact]
    public void InitialState_WhenRepositoryIsNull_HasEmptyFields()
    {
        var mediator = Substitute.For<IMediator>();

        using var viewModel = new ChooseRepositoryViewModel(mediator, TimeSpan.FromMilliseconds(1));

        viewModel.Repository.ShouldBeNull();
        viewModel.LocalDirectoryPath.ShouldBe(string.Empty);
        viewModel.AccountName.ShouldBe(string.Empty);
        viewModel.AccountKey.ShouldBe(string.Empty);
        viewModel.ContainerName.ShouldBe(string.Empty);
        viewModel.Passphrase.ShouldBe(string.Empty);
        viewModel.ContainerNames.ShouldBeEmpty();
        viewModel.IsLoading.ShouldBeFalse();
        viewModel.StorageAccountError.ShouldBeFalse();
    }

    [Fact]
    public void SettingRepository_PopulatesFields_UsingUnprotectedValues()
    {
        var mediator = Substitute.For<IMediator>();

        using var viewModel = new ChooseRepositoryViewModel(mediator, TimeSpan.FromMilliseconds(1));

        var repository = new RepositoryOptions
        {
            LocalDirectoryPath  = "C:/data",
            AccountName         = "account",
            AccountKeyProtected = "account-key".Protect(),
            ContainerName       = "container",
            PassphraseProtected = "passphrase".Protect()
        };

        viewModel.Repository = repository;

        viewModel.LocalDirectoryPath.ShouldBe("C:/data");
        viewModel.AccountName.ShouldBe("account");
        viewModel.AccountKey.ShouldBe(repository.AccountKey);
        viewModel.ContainerName.ShouldBe("container");
        viewModel.Passphrase.ShouldBe(repository.Passphrase);
    }


    [Fact]
    public void OpenRepositoryCommand_BuildsRepositoryOptions()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();

        // Start from an empty viewmodel
        using var viewModel = new ChooseRepositoryViewModel(mediator, TimeSpan.FromMilliseconds(1));

        // Act -- Simulate the user choosing a repository
        viewModel.LocalDirectoryPath = "C:/data";
        viewModel.AccountName        = "account";
        viewModel.AccountKey         = "secret-key";
        viewModel.ContainerName      = "container";
        viewModel.Passphrase         = "secret-pass";

        viewModel.OpenRepositoryCommand.Execute(null);

        // Assert
        var repository = viewModel.Repository.ShouldNotBeNull();
        repository.LocalDirectoryPath.ShouldBe("C:/data");
        repository.AccountName.ShouldBe("account");
        repository.ContainerName.ShouldBe("container");
        repository.AccountKey.ShouldBe("secret-key");
        repository.Passphrase.ShouldBe("secret-pass");

    }

    [Fact]
    public async Task AccountCredentialsChange_LoadsContainerNamesOnSuccess()
    {
        var mediator = Substitute.For<IMediator>();

        mediator
            .CreateStream(Arg.Is<ContainerNamesQuery>(q => q.AccountName == "account" && q.AccountKey == "key"), Arg.Any<CancellationToken>())
            .Returns(_ => new[] { "container-a", "container-b" }.ToAsyncEnumerable());

        using var viewModel = new ChooseRepositoryViewModel(mediator, TimeSpan.FromMilliseconds(1));

        viewModel.AccountName = "account";
        viewModel.AccountKey  = "key";

        await WaitForDebouncerAsync(() => viewModel.ContainerNames.Count == 2);

        viewModel.StorageAccountError.ShouldBeFalse();
        viewModel.IsLoading.ShouldBeFalse();
        viewModel.ContainerNames.ShouldBe(new[] { "container-a", "container-b" });
        viewModel.ContainerName.ShouldBe("container-a");

        mediator.Received(1).CreateStream(Arg.Any<ContainerNamesQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountCredentialsChange_SetsErrorWhenMediatorThrows()
    {
        var mediator = Substitute.For<IMediator>();

        mediator
            .CreateStream(Arg.Any<ContainerNamesQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));

        using var viewModel = new ChooseRepositoryViewModel(mediator, TimeSpan.FromMilliseconds(1));

        viewModel.AccountName = "account";
        viewModel.AccountKey  = "key";

        await WaitForDebouncerAsync(() => viewModel.StorageAccountError);

        viewModel.IsLoading.ShouldBeFalse();
        viewModel.ContainerNames.ShouldBeEmpty();
        viewModel.ContainerName.ShouldBe(string.Empty);
    }


    private static async Task WaitForDebouncerAsync(Func<bool> condition, int timeoutMilliseconds = 1000)
    {
        var timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
        var start   = DateTime.UtcNow;

        while (!condition())
        {
            if (DateTime.UtcNow - start > timeout)
            {
                throw new TimeoutException("Condition was not met within the allotted time.");
            }

            await Task.Delay(10);
        }
    }
}
