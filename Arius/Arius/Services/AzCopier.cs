﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Models;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;

namespace Arius.Services
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
            ILogger<AzCopier> logger)
        {
            _options = (IAzCopyUploaderOptions)options;
            _logger = logger;

            //Search async for the AZCopy Library (on another thread)
            _AzCopyPath = Task.Run(() => ExternalProcess.FindFullName(logger, "azcopy.exe", "azcopy"));
            //    .ContinueWith(tsk =>
            //{
            //    if (tsk.IsFaulted)
            //        throw tsk.Exception;

            //    return tsk.Result;

            //}, TaskScheduler.FromCurrentSynchronizationContext());
            //.ContinueWith(t =>
            //{
            //    if (t.IsFaulted)
            //        throw t.Exception;

            //    return string.Empty;
            //}, TaskScheduler..OnlyOnFaulted);
            //TODO Error handling back to main thread


            _skc = new StorageSharedKeyCredential(_options.AccountName, _options.AccountKey);

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={_options.AccountName};AccountKey={_options.AccountKey};EndpointSuffix=core.windows.net";

            // Create a BlobServiceClient object which will be used to create a container client
            var bsc = new BlobServiceClient(connectionString);
            //var bsc = new BlobServiceClient(new Uri($"{accountName}", _skc));

            _bcc = bsc.GetBlobContainerClient(_options.Container);
            
        }

        private readonly Task<string> _AzCopyPath;
        private readonly BlobContainerClient _bcc;
        private readonly StorageSharedKeyCredential _skc;
        private readonly IAzCopyUploaderOptions _options;
        private readonly ILogger<AzCopier> _logger;


        //public void Upload(IEnumerable<IFile> fileToUpload, AccessTier tier, string remoteDirectoryName, bool overwrite = false)
        //{ 
        //    Upload(fileToUpload.Directory.FullName, $"/{remoteDirectoryName}", new[] { fileToUpload.Name }, tier, overwrite);
        //}

        //private void Upload(string localDirectoryFullName, string remoteDirectoryName, string[] fileNames, AccessTier tier, bool overwrite)
        //{
        //    _logger.LogInformation($"Uploading {fileNames.Count()} files to '{remoteDirectoryName}'");

        //    //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names
        //    //Note the \* after the {dir}\*
        //    //Syntax 2: https://github.com/Azure/azure-storage-azcopy/wiki/Listing-specific-files-to-transfer

        //    var listOfFilesFullName = Path.GetTempFileName();
        //    File.WriteAllLines(listOfFilesFullName, fileNames);

        //    var sas = GetContainerSasUri(_bcc, _skc);
        //    string arguments = $@"copy ""{Path.Combine(localDirectoryFullName, "*")}"" ""{_bcc.Uri}{remoteDirectoryName}?{sas}"" --list-of-files ""{listOfFilesFullName}"" --block-blob-tier={tier} --overwrite={overwrite}";

        //    var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

        //    var p = new ExternalProcess(_AzCopyPath.Result);

        //    p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
        //        out string rawOutput,
        //        out int completed, out int failed, out int skipped, out string finalJobStatus);

        //    _logger.LogInformation($"{completed} files uploaded, job status '{finalJobStatus}'");

        //    if (completed != fileNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
        //        throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{rawOutput}");
        //}


        /// <summary>
        /// Upload IEncryptedChunkFiles or IEncryptedManifestFiles
        /// </summary>
        public void Upload(IEnumerable<IFile> filesToUpload, AccessTier tier, string remoteDirectoryName, bool overwrite = false)
        {
            filesToUpload.GroupBy(af => af.Directory.FullName)
                .AsParallel() // Kan nog altijd gebeuren als we LocalContentFiles uit verschillende directories uploaden //TODO TEST DIT
                .WithDegreeOfParallelism(1)
                .ForAll(g =>
                {
                    var fileNames = g.Select(af => Path.GetRelativePath(g.Key, af.FullName)).ToArray();

                    Upload(g.Key, $"/{remoteDirectoryName}", fileNames, tier, overwrite);
                });
        }

        private void Upload(string localDirectoryFullName, string remoteDirectoryName, string[] fileNames, AccessTier tier, bool overwrite)
        {
            _logger.LogInformation($"Uploading {fileNames.Count()} files to '{remoteDirectoryName}'");

            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names
            //Note the \* after the {dir}\*
            //Syntax 2: https://github.com/Azure/azure-storage-azcopy/wiki/Listing-specific-files-to-transfer

            var listOfFilesFullName = Path.GetTempFileName();
            File.WriteAllLines(listOfFilesFullName, fileNames);

            var sas = GetContainerSasUri(_bcc, _skc);
            string arguments = $@"copy ""{Path.Combine(localDirectoryFullName, "*")}"" ""{_bcc.Uri}{remoteDirectoryName}?{sas}"" --list-of-files ""{listOfFilesFullName}"" --block-blob-tier={tier} --overwrite={overwrite}";

            var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

            var p = new ExternalProcess(_AzCopyPath.Result);

            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
                out string rawOutput,
                out int completed, out int failed, out int skipped, out string finalJobStatus);

            File.Delete(listOfFilesFullName);

            _logger.LogInformation($"{completed} files uploaded, job status '{finalJobStatus}'");

            if (completed != fileNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
                throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{rawOutput}");
        }




        /// <summary>
        /// Download all files in the given remoteDirectoryName to the local target
        /// </summary>
        public void Download(string remoteDirectoryName, DirectoryInfo target)
        {
            if (!_bcc.GetBlobs(prefix: remoteDirectoryName).Any())
            {
                _logger.LogInformation($"No files to download in '{remoteDirectoryName}', skipping AzCopy");
                return;
            }

            _logger.LogInformation($"Downloading remote '{remoteDirectoryName}' to '{target.FullName}'");

            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-blobs#download-a-directory
            //azcopy copy 'https://<storage-account-name>.<blob or dfs>.core.windows.net/<container-name>/<directory-path>' '<local-directory-path>' --recursive

            string arguments;
            var sas = GetContainerSasUri(_bcc, _skc);
            arguments = $@"copy ""{_bcc.Uri}/{remoteDirectoryName}/*?{sas}"" ""{target.FullName}"" --recursive";

            var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

            var p = new ExternalProcess(_AzCopyPath.Result);

            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
                out string rawOutput, out int completed, out int failed, out int skipped, out string finalJobStatus);

            _logger.LogInformation($"{completed} files downloaded, job status '{finalJobStatus}'");

            if (failed > 0 || skipped > 0 || finalJobStatus != "Completed")
                throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{rawOutput}");
        }

        /// <summary>
        /// Download the blobsToDownload to the specified target
        /// </summary>
        public void Download(string remoteDirectoryName, IEnumerable<Blob> blobsToDownload, DirectoryInfo target)
        {
            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-blobs#specify-multiple-complete-file-names
            //azcopy copy '<local-directory-path>' 'https://<storage-account-name>.<blob or dfs>.core.windows.net/<container-name>' --include-path <semicolon-separated-file-list>
            //Syntax 2: https://github.com/Azure/azure-storage-azcopy/wiki/Listing-specific-files-to-transfer

            var listOfFilesFullName = Path.GetTempFileName();
            File.WriteAllLines(listOfFilesFullName, blobsToDownload.Select(b => b.Name));

            var sas = GetContainerSasUri(_bcc, _skc);
            string arguments = $@"copy ""{_bcc.Uri}/{remoteDirectoryName}/*?{sas}"" ""{target.FullName}""  --list-of-files ""{listOfFilesFullName}""";

            var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

            var p = new ExternalProcess(_AzCopyPath.Result);

            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
                out string rawOutput, out int completed, out int failed, out int skipped, out string finalJobStatus);

            _logger.LogInformation($"{completed} files downloaded, job status '{finalJobStatus}'");

            if (failed > 0 || skipped > 0 || finalJobStatus != "Completed")
                throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{rawOutput}{Environment.NewLine}");
            //$"AzCopy Log:{String.Join(Environment.NewLine, File.ReadAllLines((new DirectoryInfo("/home/runner/.azcopy/")).GetFiles().First().FullName))}");
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
