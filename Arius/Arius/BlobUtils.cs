using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Arius
{
    class BlobUtils
    {
        //public BlobUtils(string accountName, string accountKey, string container)
        //{
        //    var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

        //    // Create a BlobServiceClient object which will be used to create a container client
        //    BlobServiceClient bsc = new BlobServiceClient(connectionString);
        //    //var bcc = await bsc.CreateBlobContainerAsync(container, );

        //    var bcc = bsc.GetBlobContainerClient(container);

        //    if (!bcc.Exists())
        //    {
        //        Console.Write($"Creating container {container}... ");
        //        bcc = bsc.CreateBlobContainer(container);
        //        Console.WriteLine("Done");
        //    }

        //    _bcc = bcc;
        //}
        //private readonly BlobContainerClient _bcc;

        //public bool Exists(string file)
        //{
        //    return _bcc.GetBlobClient(file).Exists();
        //}

        //public void Upload(string fileName, string blobName, AccessTier tier)
        //{
        //    var bc = _bcc.GetBlobClient(blobName);

        //    // TODO BlobUploadOptions > ProgressHandler
        //    // TransferOptions = new StorageTransferOptions { MaximumConcurrency


        //    //using FileStream uploadFileStream = File.OpenRead(fileName);
        //    //var r = bc.Upload(uploadFileStream, true);
        //    //uploadFileStream.Close();

        //    var buo = new BlobUploadOptions
        //    {
        //        AccessTier = tier,
        //        TransferOptions = new StorageTransferOptions
        //        {
        //            MaximumConcurrency = 128
        //        }
        //    };

        //    var r = bc.Upload(fileName, buo);

        //    bc.SetAccessTier(tier);
        //}

        //public void Download(string blobName, string fileName)
        //{
        //    var bc = _bcc.GetBlobClient(blobName);

        //    bc.DownloadTo(fileName);
        //}

        //public IEnumerable<string> GetContentBlobNames()
        //{
        //    foreach (var b in _bcc.GetBlobs())
        //    {
        //        if (!b.Name.EndsWith(".manifest.7z.arius") && b.Name.EndsWith(".arius"))
        //            yield return b.Name; //Return the .arius files, not the .manifest.  
        //    }
        //}
    }
}
