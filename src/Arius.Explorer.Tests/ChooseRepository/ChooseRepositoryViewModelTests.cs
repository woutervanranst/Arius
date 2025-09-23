using Arius.Core.Features.Queries.ContainerNames;
using Arius.Explorer.ChooseRepository;
using Arius.Explorer.Settings;
using Arius.Explorer.Shared.Extensions;
using Mediator;
using NSubstitute;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Explorer.Tests.ChooseRepository;

public class ChooseRepositoryViewModelTests
{
    [Fact]
    public void InitialState_WhenRepositoryIsNull_HasEmptyFields()
    {
        var mediator = Substitute.For<IMediator>();

        using var viewModel = new ChooseRepositoryViewModel(
            mediator,
            TimeSpan.FromMilliseconds(1));

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

        using var viewModel = new ChooseRepositoryViewModel(
            mediator,
            TimeSpan.FromMilliseconds(1));

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
        var mediator = Substitute.For<IMediator>();

        using var viewModel = new ChooseRepositoryViewModel(
            mediator,
            TimeSpan.FromMilliseconds(1));

        viewModel.LocalDirectoryPath = "C:/data";
        viewModel.AccountName        = "account";
        viewModel.AccountKey         = "secret-key";
        viewModel.ContainerName      = "container";
        viewModel.Passphrase         = "secret-pass";

        viewModel.OpenRepositoryCommand.Execute(null);

        var repository = viewModel.Repository.ShouldNotBeNull();
        repository.LocalDirectoryPath.ShouldBe("C:/data");
        repository.AccountName.ShouldBe("account");
        repository.ContainerName.ShouldBe("container");
        repository.AccountKeyProtected.ShouldBe("secret-key".Protect());
        repository.PassphraseProtected.ShouldBe("secret-pass".Protect());

    }

    [Fact]
    public async Task AccountCredentialsChange_LoadsContainerNamesOnSuccess()
    {
        var mediator = Substitute.For<IMediator>();

        mediator
            .CreateStream(Arg.Is<ContainerNamesQuery>(q => q.AccountName == "account" && q.AccountKey == "key"), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable("container-a", "container-b"));

        using var viewModel = new ChooseRepositoryViewModel(
            mediator,
            TimeSpan.FromMilliseconds(1));

        viewModel.AccountName = "account";
        viewModel.AccountKey  = "key";

        await WaitForAsync(() => viewModel.ContainerNames.Count == 2);

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
            .Returns(_ => ThrowingAsyncEnumerable<string>(new InvalidOperationException("boom")));

        using var viewModel = new ChooseRepositoryViewModel(
            mediator,
            TimeSpan.FromMilliseconds(1));

        viewModel.AccountName = "account";
        viewModel.AccountKey  = "key";

        await WaitForAsync(() => viewModel.StorageAccountError);

        viewModel.IsLoading.ShouldBeFalse();
        viewModel.ContainerNames.ShouldBeEmpty();
        viewModel.ContainerName.ShouldBe(string.Empty);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 1000)
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

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(params string[] items)
    {
        foreach (var item in items)
        {
            yield return item;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<T> ThrowingAsyncEnumerable<T>(Exception exception)
    {
        await Task.Yield();
        throw exception;
    }
}
