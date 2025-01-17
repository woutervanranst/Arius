using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZioFileSystem.AzureBlobStorage;

public class ArchiveCommandHandler
{
    private readonly BlobContainerClient containerClient;
    private readonly string _passphrase;
    private readonly AccessTier _targetAccessTier;
    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();
    private readonly StateRepository stateRepo;
    private readonly SHA256Hasher hasher;

    public ArchiveCommandHandler(BlobContainerClient containerClient, string passphrase, AccessTier targetAccessTier)
    {
        this.containerClient = containerClient;
        _passphrase = passphrase;
        _targetAccessTier = targetAccessTier;

        stateRepo = new StateRepository();

        hasher = new SHA256Hasher("wouter");
    }

    

    public async Task UploadFileAsync(FilePair filePair)
    {
        // 1. Hash the file
        var h = await hasher.GetHashAsync(filePair);

        // 2. Check if the Binary is already present. If the binary is not present, check if the Binary is already being uploaded
        var (needsToBeUploaded, uploadTask) = GetUploadStatus(h);
        
        
        // 3. Upload the Binary, if needed
        if (needsToBeUploaded)
        {
            var bbc = containerClient.GetBlockBlobClient($"chunks/{h.ToLongString()}");

            var ss = filePair.BinaryFile.OpenRead();
            var ts = await OpenWriteAsync(bbc, throwOnExists: false);

            await ss.CopyToCompressedEncryptedAsync(ts, _passphrase);

            var actualTier = await SetAccessTier(bbc, ts.Position);

            // Add to db
            stateRepo.AddBinaryProperty(new BinaryPropertiesDto
            {
                Hash = h.Value,
                OriginalSize = ss.Length,
                ArchivedSize = ts.Position,
                StorageTier = actualTier.ToStorageTier()
            });

            // remove from temp
            MarkAsUploaded(h);
        }
        else
        {
            await uploadTask;
        }

        // Write the Pointer
        var pf = filePair.GetOrCreatePointerFile(h);

        // Write the PointerFileEntry
        stateRepo.UpsertPointerFileEntry(new PointerFileEntryDto
        {
            Hash = h.Value,
            RelativeName = pf.Path.FullName,
            CreationTimeUtc = pf.CreationTime,
            LastWriteTimeUtc = pf.LastWriteTime
        });

        
    }

    private async Task<AccessTier> SetAccessTier(BlockBlobClient bbc, long length)
    {
        var actualTier = GetPolicyAccessTier(length);
        await bbc.SetAccessTierAsync(actualTier);
        return actualTier;

        AccessTier GetPolicyAccessTier(long length)
        {
            const long oneMegaByte = 1024 * 1024; // TODO Derive this from the IArchiteCommandOptions?

            if (_targetAccessTier == AccessTier.Archive && length <= oneMegaByte)
                return AccessTier.Cold; //Bringing back small files from archive storage is REALLY expensive. Only after 5.5 years, it is cheaper to store 1M in Archive

            return _targetAccessTier;
        }
    }

    

    private (bool needsToBeUploaded, Task uploadTask) GetUploadStatus(Hash h)
    {
        var bp = stateRepo.GetBinaryProperty(h);

        lock (uploadingHashes)
        {
            if (bp is null)
            {
                if (uploadingHashes.TryGetValue(h, out var tcs))
                {
                    // Already uploading
                    return (false, tcs.Task);
                }
                else
                {
                    // To be uploaded
                    tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    uploadingHashes.Add(h, tcs);

                    return (true, tcs.Task);
                }
            }
            else
            {
                // Already uploaded
                return (false, Task.CompletedTask);
            }
        }
    }

    private void MarkAsUploaded(Hash h)
    {
        lock (uploadingHashes)
        {
            uploadingHashes.Remove(h, out var tcs);
            tcs.SetResult();
        }
    }

    private static async Task<Stream> OpenWriteAsync(BlockBlobClient bbc, /*string contentType = ICryptoService.ContentType, */IDictionary<string, string>? metadata = default, bool throwOnExists = true, CancellationToken cancellationToken = default)
    {
        var bbowo = new BlockBlobOpenWriteOptions();

        //NOTE the SDK only supports OpenWriteAsync with overwrite: true, therefore the ThrowOnExistOptions workaround
        if (throwOnExists)
            bbowo.OpenConditions = new BlobRequestConditions { IfNoneMatch = new ETag("*") }; // as per https://github.com/Azure/azure-sdk-for-net/issues/24831#issue-1031369473
        if (metadata is not null)
            bbowo.Metadata = metadata;
        //bbowo.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };

        return await bbc.OpenWriteAsync(overwrite: true, options: bbowo, cancellationToken: cancellationToken);
    }

    //private PointerFile CreatePointerFile(BinaryFile bf, Hash h)
    //{
    //    var pf = bf.GetPointerFile();

    //    pf.Write(h, bf.CreationTime, bf.LastWriteTime);

    //    return pf;
    //}
}