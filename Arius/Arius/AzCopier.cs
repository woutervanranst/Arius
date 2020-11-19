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

    internal class AzCopier : IBlobCopier
    {
        public AzCopier(ICommandExecutorOptions options, 
            ILogger<AzCopier> logger, 
            RemoteBlobFactory factory)
        {
            var o = (IAzCopyUploaderOptions) options;
            _contentAccessTier = o.Tier;

            _factory = factory;
            _logger = logger;

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
        private readonly RemoteBlobFactory _factory;
        private readonly ILogger<AzCopier> _logger;


        public void Upload(IEnumerable<ILocalFile> filesToUpload, BlobContainerClient target)
        {
            throw new NotImplementedException();
        }

        public void Download(IEnumerable<IBlob> blobsToDownload)
        {
            throw new NotImplementedException();
        }

        public void Download(string directoryName, DirectoryInfo target)
        {
            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-blobs#download-a-directory
            //azcopy copy 'https://<storage-account-name>.<blob or dfs>.core.windows.net/<container-name>/<directory-path>' '<local-directory-path>' --recursive

            string arguments;
            var sas = GetContainerSasUri(_bcc, _skc);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                arguments = $@"copy '{_bcc.Uri}/{directoryName}/*?{sas}' '{target.FullName}' --recursive";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                arguments = $@"copy ""{_bcc.Uri}/{directoryName}/*?{sas}"" ""{target.FullName}"" --recursive";
            else
                throw new NotImplementedException("OS Platform is not Windows or Linux");

            var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

            var p = new ExternalProcess(_AzCopyPath.Result);

            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
                out string rawOutput,
                out int completed, out int failed, out int skipped, out string finalJobStatus);

            if (failed > 0 || skipped > 0 || finalJobStatus != "Completed")
                throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{rawOutput}");
        }

        //public IEnumerable<IBlob> Upload<K>(IEnumerable<K> chunksToUpload) where K : T
        //{
        //    AccessTier tier;

        //    if (typeof(K).IsAssignableTo(typeof(IEncrypted<IChunk<ILocalContentFile>>)))
        //        tier = _contentAccessTier;
        //    else if (typeof(K).IsAssignableTo(typeof(IEncrypted<IManifestFile>)))
        //        tier = AccessTier.Cool;
        //    else
        //        throw new NotImplementedException();

        //    var remoteBlobItemNames = chunksToUpload.GroupBy(af => af.DirectoryName)
        //        .AsParallel() // Kan nog altijd gebeuren als we LocalContentFiles uit verschillende directories uploaden //TODO TEST DIT
        //            .WithDegreeOfParallelism(1)
        //        .SelectMany(g =>
        //        {
        //            var fileNames = g.Select(af => Path.GetRelativePath(g.Key, af.FullName)).ToArray();

        //            Upload(g.Key, fileNames, tier);

        //            return fileNames;
        //        })
        //        .ToImmutableArray(); //dat staat hier want anders worden de files niet geupload. De Remote<K> s worden pas aangemaakt als ze gevraagd worden

        //    //return remoteBlobItemNames.Select(filename => (IRemote<K>)new RemoteEncryptedContentBlob(filename));
        //    return remoteBlobItemNames.Select(blobItemName => _factory.Create(blobItemName));
        //}

        //private void Upload(string dir, string[] fileNames, AccessTier tier)
        //{
        //    //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names
        //    //Note the \* after the {dir}\*

        //    string arguments;
        //    var sas = GetContainerSasUri(_bcc, _skc);
        //    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        //        arguments = $@"copy '{dir}\*' '{_bcc.Uri}?{sas}' --include-path '{string.Join(';', fileNames)}' --block-blob-tier={tier} --overwrite=false";
        //    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        //        arguments = $@"copy ""{dir}\*"" ""{_bcc.Uri}?{sas}"" --include-path ""{string.Join(';', fileNames)}"" --block-blob-tier={tier} --overwrite=false";
        //    else
        //        throw new NotImplementedException("OS Platform is not Windows or Linux");

        //    var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

        //    var p = new ExternalProcess(_AzCopyPath.Result);

        //    p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
        //        out string rawOutput,
        //        out int completed, out int failed, out int skipped, out string finalJobStatus);

        //    if (completed != fileNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
        //        throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{rawOutput}");
        //}



        //public void Download(ImmutableArray<string> chunkNames, DirectoryInfo downloadDirectory)
        //{
        //    //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names-1

        //    //azcopy copy 'https://<storage-account-name>.<blob or dfs>.core.windows.net/<container-or-directory-name><SAS-token>' '<local-directory-path>' --include-pattern <semicolon-separated-file-list-with-wildcard-characters>

        //    string arguments;
        //    var sas = GetContainerSasUri(_bcc, _skc);
        //    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        //        arguments = $@"copy '{_bcc.Uri}/*?{sas}' '{downloadDirectory.FullName}'  --include-pattern '{string.Join(';', chunkNames)}'";
        //    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        //        arguments = $@"copy ""{_bcc.Uri}/*?{sas}"" ""{downloadDirectory.FullName}""  --include-pattern ""{string.Join(';', chunkNames)}""";
        //    else
        //        throw new NotImplementedException("OS Platform is not Windows or Linux");

        //    var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

        //    var p = new ExternalProcess(AzCopyPath);

        //    p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
        //        out int completed, out int failed, out int skipped, out string finalJobStatus);

        //    if (completed != chunkNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
        //        throw new ApplicationException($"Not all files were transferred."); // Raw AzCopy output{Environment.NewLine}{o}");
        //}

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
