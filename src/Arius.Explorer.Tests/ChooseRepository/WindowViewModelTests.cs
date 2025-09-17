using Arius.Explorer.ChooseRepository;
using Arius.Explorer.Shared.Services;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Shouldly;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Arius.Explorer.Tests.ChooseRepository;

public class WindowViewModelTests
{
    [Fact]
    public void SelectLocalDirectoryCommand_UpdatesLocalDirectoryPath_WhenServiceReturnsValue()
    {
        var viewModel = CreateViewModel(out _, out var folderDialogService);
        viewModel.LocalDirectoryPath = @"C:\\Initial";
        folderDialogService
            .BrowseForFolder(viewModel.LocalDirectoryPath)
            .Returns(@"D:\\Repositories\\Updated");

        viewModel.SelectLocalDirectoryCommand.Execute(null);

        viewModel.LocalDirectoryPath.ShouldBe(@"D:\\Repositories\\Updated");
    }

    [Fact]
    public void SelectLocalDirectoryCommand_DoesNotChangePath_WhenServiceReturnsNull()
    {
        var viewModel = CreateViewModel(out _, out var folderDialogService);
        viewModel.LocalDirectoryPath = @"C:\\Initial";
        folderDialogService
            .BrowseForFolder(viewModel.LocalDirectoryPath)
            .Returns((string?)null);

        viewModel.SelectLocalDirectoryCommand.Execute(null);

        viewModel.LocalDirectoryPath.ShouldBe(@"C:\\Initial");
    }

    [Fact]
    public void OpenRepositoryCommand_CanExecuteReflectsCredentialState()
    {
        var viewModel = CreateViewModel(out _, out _);

        viewModel.LocalDirectoryPath = string.Empty;
        viewModel.AccountName = string.Empty;
        viewModel.AccountKey = string.Empty;

        viewModel.OpenRepositoryCommand.CanExecute(null).ShouldBeFalse();

        viewModel.LocalDirectoryPath = @"C:\\Repo";
        viewModel.AccountName = "account";
        viewModel.AccountKey = "key";

        viewModel.OpenRepositoryCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task OpenRepositoryCommand_UpdatesState_WhenServiceReturnsSuccess()
    {
        var viewModel = CreateViewModel(out var repositoryService, out _);

        viewModel.LocalDirectoryPath = @"C:\\Repo";
        viewModel.AccountName = "account";
        viewModel.AccountKey = "key";
        viewModel.ContainerName = "obsolete";

        var result = RepositoryConnectionResult.Success(new[] { "alpha", "beta" });
        repositoryService
            .TryConnectAsync(Arg.Any<RepositoryConnectionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

        await viewModel.OpenRepositoryCommand.ExecuteAsync(null);

        await repositoryService
            .Received(1)
            .TryConnectAsync(
                Arg.Is<RepositoryConnectionRequest>(r =>
                    r.LocalDirectoryPath == @"C:\\Repo" &&
                    r.AccountName == "account" &&
                    r.AccountKey == "key" &&
                    r.ContainerName == "obsolete" &&
                    r.Passphrase == string.Empty),
                Arg.Any<CancellationToken>());

        viewModel.IsLoading.ShouldBeFalse();
        viewModel.StorageAccountError.ShouldBeFalse();
        viewModel.StorageAccountErrorMessage.ShouldBe(string.Empty);
        viewModel.ContainerNames.ShouldBe(new[] { "alpha", "beta" });
        viewModel.ContainerName.ShouldBe("alpha");
    }

    [Fact]
    public async Task OpenRepositoryCommand_PreservesExistingContainers_WhenServiceReturnsFailure()
    {
        var viewModel = CreateViewModel(out var repositoryService, out _);

        viewModel.LocalDirectoryPath = @"C:\\Repo";
        viewModel.AccountName = "account";
        viewModel.AccountKey = "key";
        viewModel.ContainerNames = new ObservableCollection<string> { "existing" };
        viewModel.ContainerName = "existing";
        var originalReference = viewModel.ContainerNames;

        repositoryService
            .TryConnectAsync(Arg.Any<RepositoryConnectionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RepositoryConnectionResult.Failure("failed")));

        await viewModel.OpenRepositoryCommand.ExecuteAsync(null);

        viewModel.IsLoading.ShouldBeFalse();
        viewModel.StorageAccountError.ShouldBeTrue();
        viewModel.StorageAccountErrorMessage.ShouldBe("failed");
        viewModel.ContainerNames.ShouldBeSameAs(originalReference);
        viewModel.ContainerNames.ShouldBe(new[] { "existing" });
        viewModel.ContainerName.ShouldBe("existing");
    }

    [Fact]
    public async Task OpenRepositoryCommand_SetsIsLoadingWhileRequestIsInFlight()
    {
        var viewModel = CreateViewModel(out var repositoryService, out _);

        viewModel.LocalDirectoryPath = @"C:\\Repo";
        viewModel.AccountName = "account";
        viewModel.AccountKey = "key";

        var tcs = new TaskCompletionSource<RepositoryConnectionResult>();
        repositoryService
            .TryConnectAsync(Arg.Any<RepositoryConnectionRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => tcs.Task);

        var executionTask = viewModel.OpenRepositoryCommand.ExecuteAsync(null);

        viewModel.IsLoading.ShouldBeTrue();

        tcs.SetResult(RepositoryConnectionResult.Success(new[] { "alpha" }));
        await executionTask;

        viewModel.IsLoading.ShouldBeFalse();
    }

    private static WindowViewModel CreateViewModel(out IRepositoryConnectionService repositoryService, out IFolderDialogService folderDialogService)
    {
        repositoryService = Substitute.For<IRepositoryConnectionService>();
        folderDialogService = Substitute.For<IFolderDialogService>();
        return new WindowViewModel(repositoryService, folderDialogService);
    }
}
