using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZioFileSystem.AzureBlobStorage;

public class ArchiveCommandHandler
{
    private readonly BlobContainerClient containerClient;
    private readonly string _passphrase;
    private readonly Dictionary<Hash, TaskCompletionSource> uploadingHashes = new();
    private readonly StateRepository stateRepo;
    private readonly SHA256Hasher hasher;

    public ArchiveCommandHandler(BlobContainerClient containerClient, string passphrase)
    {
        this.containerClient = containerClient;
        _passphrase = passphrase;

        stateRepo = new StateRepository();

        hasher = new SHA256Hasher("wouter");
    }

    

    public async Task UploadFileAsync(FilePair filePair)
    {
        //if (filePair.BinaryFile?.FileSystem is not PhysicalFileSystem)
        //    throw new ArgumentException("Source file must be in a PhysicalFileSystem", nameof(filePair));
        //if (filePair.PointerFile is not null && filePair.PointerFile.FileSystem is not PhysicalFileSystem)
        //    throw new ArgumentException("Source file must be in a PhysicalFileSystem", nameof(filePair));

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

            await bbc.SetAccessTierAsync(AccessTier.Cool);

            // Add to db
            stateRepo.AddBinaryProperty(new BinaryPropertiesDto { Hash = h.Value, StorageTier = StorageTier.Cool });

            // remove from temp
            MarkAsUploaded(h);
        }
        else
        {
            await uploadTask;
        }

        // Write the PointerFileEntry
        stateRepo.UpsertPointerFileEntry(new PointerFileEntryDto { Hash = h.Value, RelativeName = filePair.BinaryFile.FullName });

        // Write the Pointer
        CreatePointerFile(filePair.BinaryFile, h);
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

    private void CreatePointerFile(BinaryFile bf, Hash h)
    {
        var pf = bf.GetPointerFile();

        var pfc = new PointerFileContents(h.ToLongString());

        var json = JsonSerializer.SerializeToUtf8Bytes(pfc);
        pf.WriteAllBytes(json);

        pf.CreationTime = bf.CreationTime;
        pf.LastWriteTime = bf.LastWriteTime;

        //var xx = ReadPointerFile(bf.FileSystem, pfPath);
        //if (bf.FileSystem.FileExists(pfPath))
        //{
        //}
        //else
        //{
        //}

        //var pfPath = bf.Path.ChangeExtension($"{bf.ExtensionWithDot}{PointerFile.Extension}");
        //var pfc = new PointerFileContents(h.ToLongString());

        //var json = JsonSerializer.SerializeToUtf8Bytes(pfc);
        //bf.FileSystem.WriteAllBytes(pfPath, json);

        //bf.FileSystem.SetCreationTime(pfPath, bf.CreationTime);
        //bf.FileSystem.SetLastWriteTime(pfPath, bf.LastWriteTime);
    }

    //private (PointerFile pf, Hash h) ReadPointerFile(IFileSystem fs, UPath pfPath)
    //{
    //    var pf = new PointerFile(fs, pfPath);
    //    var json = pf.ReadAllBytes(); // throws a FileNotFoundException if not exists
    //    var pfc = JsonSerializer.Deserialize<PointerFileContents>(json);
    //    var h = new Hash(pfc.BinaryHash);

    //    return (pf, h);
    //}

    private record PointerFileContents(string BinaryHash);
}