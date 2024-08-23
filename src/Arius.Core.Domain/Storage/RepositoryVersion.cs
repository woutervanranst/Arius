namespace Arius.Core.Domain.Storage;

public record RepositoryVersion
{
    public required string Name { get; init; }
    //public async Task<StorageTier> GetStorageTierAsync() => await Blob.GetStorageTierAsync();
}