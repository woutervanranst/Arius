using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Storage.Sas;

namespace Arius
{
    internal class AriusRemoteArchive
    {
        static AriusRemoteArchive()
        {
            /*
             * = Path.GetPathRoot(Environment.SystemDirectory)
             * where /R . azcopy
             * https://blog.stephencleary.com/2012/08/asynchronous-lazy-initialization.html
             * https://devblogs.microsoft.com/pfxteam/asynclazyt/
             */

            AzCopyPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\ModelBuilder\AzCopyService\azcopy.exe";
        }

        private static readonly string AzCopyPath;

        public AriusRemoteArchive(string accountName, string accountKey, string container)
        {
            _skc = new StorageSharedKeyCredential(accountName, accountKey);
            
            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

            // Create a BlobServiceClient object which will be used to create a container client
            var bsc = new BlobServiceClient(connectionString);
            //var bsc = new BlobServiceClient(new Uri($"{accountName}", _skc));

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
        private readonly StorageSharedKeyCredential _skc;

        public bool Exists(string file)
        {
            return _bcc.GetBlobClient(file).Exists();
        }

        
        public void Upload(IEnumerable<EncryptedAriusManifestFile> manifests)
        {
            Upload(manifests, AccessTier.Cool);
        }
        public void Upload(IEnumerable<AriusFile> files, AccessTier tier)
        {
            files.GroupBy(af => af.DirectoryName)
                .AsParallel() // Kan nog altijd gebeuren als we ContentFIles uit verschillende directories uploaden
                    .WithDegreeOfParallelism(1)         
                .ForAll(g => Upload(g.Key, g.Select(af => Path.GetRelativePath(g.Key, af.FullName)).ToArray(), tier));
        }
        private void Upload(string dir, string[] fileNames, AccessTier tier)
        {
            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names
            //Note the \* after the {dir}\*

            string arguments;
            var sas = GetContainerSasUri(_bcc, _skc);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                arguments =
                    $@"copy '{dir}\*' '{sas}' --include-path '{string.Join(';', fileNames)}' --block-blob-tier={tier} --overwrite=false";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                arguments =
                    $@"copy ""{dir}\*"" ""{sas}"" --include-path ""{string.Join(';', fileNames)}"" --block-blob-tier={tier} --overwrite=false";
            else
                throw new NotImplementedException("OS Platform is not Windows or Linux");

            var regex =
                @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

            var p = new ExternalProcess(AzCopyPath);

            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus", 
                out int completed, out int failed, out int skipped, out string finalJobStatus);

            if (completed != fileNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
                throw new ApplicationException($"Not all files were transferred."); // Raw AzCopy output{Environment.NewLine}{o}");
        }

        private static string GetContainerSasUri(BlobContainerClient container, StorageSharedKeyCredential sharedKeyCredential, string storedPolicyName = null)
        {
            var sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = container.Name,
                Resource = "c",
            };

            if (storedPolicyName == null)
            {
                sasBuilder.StartsOn = DateTimeOffset.UtcNow;
                sasBuilder.ExpiresOn = DateTimeOffset.UtcNow.AddHours(24);
                sasBuilder.SetPermissions(BlobContainerSasPermissions.All); //TODO Restrict?
            }
            else
            {
                sasBuilder.Identifier = storedPolicyName;
            }

            string sasToken = sasBuilder.ToSasQueryParameters(sharedKeyCredential).ToString();

            return $"{container.Uri}?{sasToken}";
        }

        

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

        public IEnumerable<BlobItem> GetEncryptedManifestFileBlobItems()
        {
            return _bcc.GetBlobs()
                .Where(b => b.Name.EndsWith(".manifest.7z.arius"));
                //.Select(b => b.Name);
        }

        public IEnumerable<BlobItem> GetEncryptedAriusChunkBlobItems()
        {
            return _bcc.GetBlobs()
                .Where(b => b.Name.EndsWith(".7z.arius") && !b.Name.EndsWith(".manifest.7z.arius"));
            //.Select(b => encryptedar)
        }
    }
}
