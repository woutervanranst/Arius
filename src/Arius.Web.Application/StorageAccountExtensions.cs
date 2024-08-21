using Arius.Web.Domain;

namespace Arius.Web.Application;

public static class StorageAccountExtensions
{
    public static StorageAccountViewModel ToViewModel(this StorageAccount storageAccount)
    {
        return new StorageAccountViewModel
        {
            Id          = storageAccount.Id,
            AccountName = storageAccount.AccountName,
            AccountKey  = storageAccount.AccountKey
        };
    }

    public static StorageAccount ToDomainModel(this StorageAccountViewModel viewModel)
    {
        return new StorageAccount
        {
            Id          = viewModel.Id,
            AccountName = viewModel.AccountName,
            AccountKey  = viewModel.AccountKey
        };
    }
}