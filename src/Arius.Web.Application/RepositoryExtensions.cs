using Arius.Web.Domain;

namespace Arius.Web.Application;

public static class RepositoryExtensions
{
    public static RepositoryViewModel ToViewModel(this Repository repository)
    {
        return new RepositoryViewModel
        {
            Id            = repository.Id,
            LocalPath     = repository.LocalPath,
            ContainerName = repository.ContainerName,
            Passphrase    = repository.Passphrase,
            Tier          = repository.Tier,
            RemoveLocal   = repository.RemoveLocal,
            Dedup         = repository.Dedup,
            FastHash      = repository.FastHash
        };
    }

    public static Repository ToDomainModel(this RepositoryViewModel viewModel)
    {
        return new Repository
        {
            Id            = viewModel.Id,
            LocalPath     = viewModel.LocalPath,
            ContainerName = viewModel.ContainerName,
            Passphrase    = viewModel.Passphrase,
            Tier          = viewModel.Tier,
            RemoveLocal   = viewModel.RemoveLocal,
            Dedup         = viewModel.Dedup,
            FastHash      = viewModel.FastHash
        };
    }
}