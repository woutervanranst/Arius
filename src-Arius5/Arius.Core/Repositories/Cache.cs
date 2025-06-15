using Arius.Core.Services;
using WouterVanRanst.Utils.Extensions;

namespace Arius.Core.Repositories;

internal class Cache<T> where T : class
{
    private readonly Func<string, T?>      getCached;
    private readonly Func<string, Task<T>> loadInCache;

    public Cache(Func<string, T?> getCached, Func<string, Task<T>> loadInCache)
    {
        this.getCached   = getCached;
        this.loadInCache = loadInCache;
    }

    public async Task<T> GetOrLoadAsync(string id)
    {
        var cached = getCached(id);
        if (cached is not null)
        {
            return cached;
        }
        var loaded = await loadInCache(id).ConfigureAwait(false);
        return loaded;
    }
}

internal class StateRepositoryCache
{
    private readonly DirectoryInfo localCacheRoot;
    private readonly BlobStorage   blobStorage;
    private readonly string        passphrase;

    private readonly Cache<FileInfo> stateCache;

    public StateRepositoryCache(DirectoryInfo localCacheRoot, BlobStorage blobStorage, string passphrase)
    {
        this.localCacheRoot = localCacheRoot;
        this.blobStorage    = blobStorage;
        this.passphrase     = passphrase;

        stateCache = new Cache<FileInfo>(GetCached, LoadInCacheAsync);
    }

    private FileInfo GetLocalFileInfo(string version) => localCacheRoot.GetFileInfo($"{version}.db");

    private FileInfo? GetCached(string version)
    {
        if (GetLocalFileInfo(version) is { Exists: true } cachedCopy)
            return cachedCopy;
        else
            return null;
    }

    private async Task<FileInfo> LoadInCacheAsync(string version)
    {
        var x = GetLocalFileInfo(version);
        x.CreateDirectoryIfNotExists();

        // 1. Get the blob from storage
        await using var blobStream = await blobStorage.OpenReadStateAsync(version);
        await using var targetFileStream = x.OpenWrite();
        await blobStream.CopyToAsync(targetFileStream);

        //// 2. Get the decrypted and decompressed stream
        //await using var decryptionStream = await blobStream.GetDecryptionStreamAsync(passphrase);

        //// 3. Write to the target file
        //await using var targetFileStream = x.OpenWrite();
        //await decryptionStream.CopyToAsync(targetFileStream);
        await targetFileStream.FlushAsync(); // Explicitly flush

        return x;
    }

    public async Task<FileInfo?> GetLocalCacheAsync(string version)
    {
        return await stateCache.GetOrLoadAsync(version);
    }

    internal async Task UploadLocalCacheAsync(string version, CancellationToken cancellationToken = default)
    {
        var localFileInfo = GetLocalFileInfo(version);

        await using var sourceFileStream = localFileInfo.OpenRead();
        await using var blobStream = await blobStorage.OpenWriteStateAsync(version, cancellationToken);
        await sourceFileStream.CopyToAsync(blobStream, cancellationToken);

        await blobStream.FlushAsync(cancellationToken);
    }
}
