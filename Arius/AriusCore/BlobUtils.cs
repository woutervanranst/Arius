using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace AriusCore
{
    class BlobUtils
    {
        public BlobUtils(string accountName, string accountKey)
        {
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);

            //Create a unique name for the container
            string containerName = "arius";

            // Create the container and return a container client object
            _bcc = new Lazy<Task<BlobContainerClient>>(async () => await blobServiceClient.CreateBlobContainerAsync(containerName));
        }

        private Lazy<Task<BlobContainerClient>> _bcc;

        public async Task<string> Upload(string file)
        {
            string extension;

            if (file.EndsWith(".7z.arius"))
                extension = ".7z.arius";
            else
                throw new NotImplementedException();


            var blobName = $"{Guid.NewGuid()}{extension}";

            // Get a reference to a blob
            BlobClient blobClient = (await _bcc.Value).GetBlobClient(blobName);

            Console.WriteLine("Uploading to Blob storage as blob:\n\t {0}\n", blobClient.Uri);

            // Open the file and upload its data
            using FileStream uploadFileStream = File.OpenRead(file);
            var r = await blobClient.UploadAsync(uploadFileStream, true);
            uploadFileStream.Close();

            await blobClient.SetAccessTierAsync(AccessTier.Cool);

            //Delete the file on source
            File.Delete(file);

            //return r.Value;
            return blobName;
        }
    }
}
