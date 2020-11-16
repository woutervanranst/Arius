//using Azure.Storage;
//using Azure.Storage.Blobs;
//using Azure.Storage.Blobs.Models;
//using Azure.Storage.Sas;
//using System;
//using System.Collections.Generic;
//using System.Collections.Immutable;
//using System.IO;
//using System.Linq;
//using System.Runtime.InteropServices;
//using Microsoft.Extensions.Azure;

//namespace Arius
//{
//    internal class AriusRemoteArchive
//    {
//        static AriusRemoteArchive()
//        {
//            /*
//             * = Path.GetPathRoot(Environment.SystemDirectory)
//             * where /R . azcopy
//             * https://blog.stephencleary.com/2012/08/asynchronous-lazy-initialization.html
//             * https://devblogs.microsoft.com/pfxteam/asynclazyt/
//             */

//            AzCopyPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\ModelBuilder\AzCopyService\azcopy.exe";
//        }

//        private static readonly string AzCopyPath;

//        public AriusRemoteArchive(string accountName, string accountKey, string container)
//        {
//            _skc = new StorageSharedKeyCredential(accountName, accountKey);

//            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";

//            // Create a BlobServiceClient object which will be used to create a container client
//            var bsc = new BlobServiceClient(connectionString);
//            //var bsc = new BlobServiceClient(new Uri($"{accountName}", _skc));

//            var bcc = bsc.GetBlobContainerClient(container);

//            if (!bcc.Exists())
//            {
//                Console.Write($"Creating container {container}... ");
//                bcc = bsc.CreateBlobContainer(container);
//                Console.WriteLine("Done");
//            }

//            _bcc = bcc;
//        }
//        private readonly BlobContainerClient _bcc;
//        private readonly StorageSharedKeyCredential _skc;

//        public bool Exists(string file)
//        {
//            return _bcc.GetBlobClient(file).Exists();
//        }


//        public void Upload(IEnumerable<AriusFile> files, AccessTier tier)
//        {
//            files.GroupBy(af => af.DirectoryName)
//                .AsParallel() // Kan nog altijd gebeuren als we LocalContentFiles uit verschillende directories uploaden //TODO TEST DIT
//                    .WithDegreeOfParallelism(1)
//                .ForAll(g => Upload(g.Key, g.Select(af => Path.GetRelativePath(g.Key, af.FullName)).ToArray(), tier));
//        }
//        private void Upload(string dir, string[] fileNames, AccessTier tier)
//        {
//            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names
//            //Note the \* after the {dir}\*

//            string arguments;
//            var sas = GetContainerSasUri(_bcc, _skc);
//            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
//                arguments = $@"copy '{dir}\*' '{_bcc.Uri}?{sas}' --include-path '{string.Join(';', fileNames)}' --block-blob-tier={tier} --overwrite=false";
//            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//                arguments = $@"copy ""{dir}\*"" ""{_bcc.Uri}?{sas}"" --include-path ""{string.Join(';', fileNames)}"" --block-blob-tier={tier} --overwrite=false";
//            else
//                throw new NotImplementedException("OS Platform is not Windows or Linux");

//            var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

//            var p = new ExternalProcess(AzCopyPath);

//            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
//                out int completed, out int failed, out int skipped, out string finalJobStatus);

//            if (completed != fileNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
//                throw new ApplicationException($"Not all files were transferred."); // Raw AzCopy output{Environment.NewLine}{o}");
//        }

//        public void Download(ImmutableArray<string> chunkNames, DirectoryInfo downloadDirectory)
//        {
//            //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names-1

//            //azcopy copy 'https://<storage-account-name>.<blob or dfs>.core.windows.net/<container-or-directory-name><SAS-token>' '<local-directory-path>' --include-pattern <semicolon-separated-file-list-with-wildcard-characters>

//            string arguments;
//            var sas = GetContainerSasUri(_bcc, _skc);
//            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
//                arguments = $@"copy '{_bcc.Uri}/*?{sas}' '{downloadDirectory.FullName}'  --include-pattern '{string.Join(';', chunkNames)}'";
//            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//                arguments = $@"copy ""{_bcc.Uri}/*?{sas}"" ""{downloadDirectory.FullName}""  --include-pattern ""{string.Join(';', chunkNames)}""";
//            else
//                throw new NotImplementedException("OS Platform is not Windows or Linux");

//            var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";

//            var p = new ExternalProcess(AzCopyPath);

//            p.Execute(arguments, regex, "completed", "failed", "skipped", "finalJobStatus",
//                out int completed, out int failed, out int skipped, out string finalJobStatus);

//            if (completed != chunkNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
//                throw new ApplicationException($"Not all files were transferred."); // Raw AzCopy output{Environment.NewLine}{o}");
//        }

//        private static string GetContainerSasUri(BlobContainerClient container, StorageSharedKeyCredential sharedKeyCredential, string storedPolicyName = null)
//        {
//            var sasBuilder = new BlobSasBuilder()
//            {
//                BlobContainerName = container.Name,
//                Resource = "c",
//            };

//            if (storedPolicyName == null)
//            {
//                sasBuilder.StartsOn = DateTimeOffset.UtcNow;
//                sasBuilder.ExpiresOn = DateTimeOffset.UtcNow.AddHours(24);
//                sasBuilder.SetPermissions(BlobContainerSasPermissions.All); //TODO Restrict?
//            }
//            else
//            {
//                sasBuilder.Identifier = storedPolicyName;
//            }

//            string sasToken = sasBuilder.ToSasQueryParameters(sharedKeyCredential).ToString();

//            return sasToken;
//        }



//        /*
//         * TODO voor t foefke progress:
//         *
//         * azcopy.exe jobs show <jobid>


//            Job ae15d86d-81ad-a54f-55b3-472d0bc93041 summary
//            Number of File Transfers: 0
//            Number of Folder Property Transfers: 0
//            Total Number Of Transfers: 0
//            Number of Transfers Completed: 0
//            Number of Transfers Failed: 0
//            Number of Transfers Skipped: 0
//            Percent Complete (approx): 100.0 <--------
//            Final Job Status: InProgress
//         */


//        //public void Download(string blobName, string fileName)
//        //{
//        //    var bc = _bcc.GetBlobClient(blobName);

//        //    bc.DownloadTo(fileName);
//        //}


//        public IEnumerable<RemoteEncryptedAriusManifest> GetRemoteEncryptedAriusManifests()
//        {
//            return _bcc.GetBlobs()
//                .Where(b => b.Name.EndsWith(".manifest.7z.arius"))
//                .Select(bi => new RemoteEncryptedAriusManifest(this, bi)); //TODO extract "validators" to some other class?
//        }

//        public IEnumerable<RemoteEncryptedAriusChunk> GetRemoteEncryptedAriusChunks()
//        {
//            return _bcc.GetBlobs()
//                .Where(b => b.Name.EndsWith(".7z.arius") && !b.Name.EndsWith(".manifest.7z.arius"))
//                .Select(bi => new RemoteEncryptedAriusChunk(this, bi));
//        }

//        public RemoteEncryptedAriusChunk GetRemoteEncryptedAriusChunk(string hash)
//        {
//            var bi = GetBlobItem($"{hash}.7z.arius");

//            return new RemoteEncryptedAriusChunk(this, bi);
//        }

//        public RemoteEncryptedAriusManifest GetRemoteEncryptedAriusManifestByHash(string hash)
//        {
//            var bi = GetBlobItem($"{hash}.manifest.7z.arius");

//            return new RemoteEncryptedAriusManifest(this, bi);
//        }
//        public RemoteEncryptedAriusManifest GetRemoteEncryptedAriusManifestByBlobItemName(string blobItemName)
//        {
//            var bi = GetBlobItem(blobItemName);

//            return new RemoteEncryptedAriusManifest(this, bi);
//        }

//        private BlobItem GetBlobItem(string name)
//        {
//            var bi = _bcc
//                .GetBlobs(prefix: name)
//                .SingleOrDefault(bi => bi.Name == name); //TODO test met nonexisting hash

//            if (bi is null)
//                throw new ArgumentException("NO CHUNK FOR HASH");

//            return bi;
//        }


//        public void UploadEncryptedAriusManifest(string file, string hash) //TODO Strinlgy Typed RemoteEncryptedAriusManifest : RemoteFile ?
//        {
//            var blobName = $"{hash}.manifest.7z.arius";
//            var bc = _bcc.GetBlobClient(blobName);

//            using var s = File.Open(file, FileMode.Open, FileAccess.Read);

//            if (bc.Exists())
//            {
//                bc.Upload(s, overwrite: true);
//            }
//            else
//            {
//                bc.Upload(s);
//                bc.SetAccessTier(AccessTier.Cool);
//            }
            
//            s.Close();
//        }

//        public void Download(string blobName, string file)
//        {
//            var bc = _bcc.GetBlobClient(blobName);

//            if (!bc.Exists())
//                throw new ArgumentException("CONTENT/MANIFEST DOES NOT EXIST"); //TODO

//            bc.DownloadTo(file);
//        }
//    }
//}
