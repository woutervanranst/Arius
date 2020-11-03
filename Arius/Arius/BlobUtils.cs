using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arius
{
    class BlobUtils
    {
        public BlobUtils(string accountName, string accountKey, string container)
        {
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient bsc = new BlobServiceClient(connectionString);
            //var bcc = await bsc.CreateBlobContainerAsync(container, );

            var bcc = bsc.GetBlobContainerClient(container);

            if (!bcc.Exists())
            {
                Console.Write($"Creating container {container}... ");
                bcc = bsc.CreateBlobContainer(container);
                Console.WriteLine("Done");
            }

            _bcc = bcc;
        }
        private readonly BlobContainerClient _bcc;

        public bool Exists(string file)
        {
            return _bcc.GetBlobClient(file).Exists();
        }

        public void Upload(string fileName, string blobName, AccessTier tier)
        {
            var bc = _bcc.GetBlobClient(blobName);

            using FileStream uploadFileStream = File.OpenRead(fileName);
            var r = bc.Upload(uploadFileStream, true);
            uploadFileStream.Close();

            bc.SetAccessTier(tier);
        }

        public void Download(string blobName, string fileName)
        {
            var bc = _bcc.GetBlobClient(blobName);

            bc.DownloadTo(fileName);
        }
    }
}
