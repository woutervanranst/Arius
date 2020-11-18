using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;

namespace Arius
{
    internal interface IAzCopyUploaderOptions : ICommandExecutorOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Container { get; init; }
        public AccessTier Tier { get; init; }
    }

    internal class AzCopyUploader<T> : IUploader<T> where T : IFile
    {
        public AzCopyUploader(ICommandExecutorOptions options, ILogger<AzCopyUploader<T>> logger)
        {
            var o = (IAzCopyUploaderOptions) options;
            _contentAccessTier = o.Tier;

            //Search async for the AZCopy Library (on another thread)
            _AzCopyPath = Task.Run(() => ExternalProcess.FindFullName(logger, "azcopy.exe", "azcopy"));

            _skc = new StorageSharedKeyCredential(o.AccountName, o.AccountKey);

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={o.AccountName};AccountKey={o.AccountKey};EndpointSuffix=core.windows.net";

            // Create a BlobServiceClient object which will be used to create a container client
            var bsc = new BlobServiceClient(connectionString);
            //var bsc = new BlobServiceClient(new Uri($"{accountName}", _skc));

            var bcc = bsc.GetBlobContainerClient(o.Container);

            if (!bcc.Exists())
            {
                logger.LogInformation($"Creating container {o.Container}... ");
                bcc = bsc.CreateBlobContainer(o.Container);
                logger.LogInformation("Done");
            }

            _bcc = bcc;
        }

        private readonly Task<string> _AzCopyPath;
        private readonly BlobContainerClient _bcc;
        private readonly StorageSharedKeyCredential _skc;
        private readonly AccessTier _contentAccessTier;

        public IEnumerable<IRemote<K>> Upload<K>(IEnumerable<K> chunksToUpload) where K : T
        {
            AccessTier tier;

            if (typeof(K).IsAssignableTo(typeof(IEncrypted<IChunk<ILocalContentFile>>)))
                tier = _contentAccessTier;
            else if (typeof(K).IsAssignableTo(typeof(IEncrypted<IManifestFile>)))
                tier = AccessTier.Cool;
            else
                throw new NotImplementedException();

            var remoteBlobs = chunksToUpload.GroupBy(af => af.DirectoryName)
                .AsParallel() // Kan nog altijd gebeuren als we LocalContentFiles uit verschillende directories uploaden //TODO TEST DIT
                    .WithDegreeOfParallelism(1)
                .SelectMany(g =>
                {
                    var fileNames = g.Select(af => Path.GetRelativePath(g.Key, af.FullName)).ToArray();
                    
                    Upload(g.Key, fileNames, tier);

                    return fileNames.Select(filename => (IRemote<K>)new RemoteEncryptedContentBlob(filename));
                })
                .ToImmutableArray();

            return remoteBlobs;
        }

        private void Upload(string dir, string[] fileNames, AccessTier tier)
        {
            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names
            //Note the \* after the {dir}\*

            string arguments;
            var sas = GetContainerSasUri(_bcc, _skc);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                arguments = $@"copy '{dir}\*' '{_bcc.Uri}?{sas}' --include-path '{string.Join(';', fileNames)}' --block-blob-tier={tier} --overwrite=false";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                arguments = $@"copy ""{dir}\*"" ""{_bcc.Uri}?{sas}"" --include-path ""{string.Join(';', fileNames)}"" --block-blob-tier={tier} --overwrite=false";
            else
                throw new NotImplementedException("OS Platform is not Windows or Linux");

            var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

            var p = new ExternalProcess(_AzCopyPath.Result);

            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
                out string rawOutput,
                out int completed, out int failed, out int skipped, out string finalJobStatus);

            if (completed != fileNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
                throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{rawOutput}");
        }



        public IEnumerable<T> Download(IEnumerable<IRemote<T>> chunksToDownload)
        {
            throw new NotImplementedException();
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

            return sasToken;
        }



        /*
         * TODO voor t foefke progress:
         *
         * azcopy.exe jobs show <jobid>


            Job ae15d86d-81ad-a54f-55b3-472d0bc93041 summary
            Number of File Transfers: 0
            Number of Folder Property Transfers: 0
            Total Number Of Transfers: 0
            Number of Transfers Completed: 0
            Number of Transfers Failed: 0
            Number of Transfers Skipped: 0
            Percent Complete (approx): 100.0 <--------
            Final Job Status: InProgress
         */
    }
}
