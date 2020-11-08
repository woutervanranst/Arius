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
    internal class BlobUtils
    {
        static BlobUtils()
        {
            /*
             * = Path.GetPathRoot(Environment.SystemDirectory)
             * where /R . azcopy
             * https://blog.stephencleary.com/2012/08/asynchronous-lazy-initialization.html
             * https://devblogs.microsoft.com/pfxteam/asynclazyt/
             */

            _azCopyPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\ModelBuilder\AzCopyService\azcopy.exe";
        }

        private static readonly string _azCopyPath;

        public BlobUtils(string accountName, string accountKey, string container)
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

        public string ShellExecute(string fileName, string arguments)
        {
            try
            {
                // https://developers.redhat.com/blog/2019/10/29/the-net-process-class-on-linux/

                using var process = new Process();

                bool hasError = false;
                string errorMsg = string.Empty;
                string output = string.Empty;

                var psi = new ProcessStartInfo
                {
                    FileName = fileName,

                    UseShellExecute = false,
                    Arguments = arguments,
                    //ArgumentList = { argumentList },

                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                process.StartInfo = psi;
                process.OutputDataReceived += (sender, data) => output += data.Data + Environment.NewLine; //System.Diagnostics.Debug.WriteLine(data.Data);
                process.ErrorDataReceived += (sender, data) =>
                {
                    if (data.Data == null)
                        return;

                    System.Diagnostics.Debug.WriteLine(data.Data);

                    hasError = true;
                    errorMsg += data.Data;
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();


                process.WaitForExit();

                if (process.ExitCode != 0 || hasError)
                    throw new ApplicationException(errorMsg);

                return output;

            }
            catch (Win32Exception e) // Win32Exception: 'The system cannot find the file specified.'
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public void Upload(params AriusFile[] files)
        {
            var path = _azCopyPath;

            files.GroupBy(af => af.DirectoryName, af => af)
                .AsParallel()
                .WithDegreeOfParallelism(1)
                .ForAll(filesGroupedPerDirectory =>
                {
                    //Syntax https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-files#specify-multiple-complete-file-names
                    //Note the \* after the {dir}\*

                    var dir = filesGroupedPerDirectory.Key;
                    var sas = GetContainerSasUri(_bcc, _skc);
                    var fileNames = filesGroupedPerDirectory.Select(af => Path.GetRelativePath(dir, af.FullName)).ToArray();

                    string arguments;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        arguments = $@"copy '{dir}\*' '{sas}' --include-path '{string.Join(';', fileNames)}' --overwrite=false";
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        arguments = $@"copy ""{dir}\*"" ""{sas}"" --include-path ""{string.Join(';', fileNames)}"" --overwrite=false";
                    else
                        throw new NotImplementedException("OS Platform is not Windows or Linux");

                    var o = ShellExecute(_azCopyPath, arguments);

                    var regex = @$"Number of Transfers Completed: (?<completed>\d*){Environment.NewLine}Number of Transfers Failed: (?<failed>\d*){Environment.NewLine}Number of Transfers Skipped: (?<skipped>\d*){Environment.NewLine}TotalBytesTransferred: (?<totalBytes>\d*){Environment.NewLine}Final Job Status: (?<finalJobStatus>\w*)";
                    var match = Regex.Match(o, regex);

                    if (!match.Success)
                        throw new ApplicationException("REGEX MATCH ERROR");

                    int completed = int.Parse(match.Groups["completed"].Value);
                    int failed = int.Parse(match.Groups["failed"].Value);
                    int skipped = int.Parse(match.Groups["skipped"].Value);

                    string finalJobStatus = match.Groups["finalJobStatus"].Value;

                    if (completed != fileNames.Count() || failed > 0 || skipped > 0 || finalJobStatus != "Completed")
                        throw new ApplicationException($"Not all files were transferred. Raw AzCopy output{Environment.NewLine}{o}");
                });
        }

        private static string GetContainerSasUri(BlobContainerClient container, StorageSharedKeyCredential sharedKeyCredential, string storedPolicyName = null)
        {
            // Create a SAS token that's valid for one hour.
            BlobSasBuilder sasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = container.Name,
                Resource = "c",
            };

            if (storedPolicyName == null)
            {
                sasBuilder.StartsOn = DateTimeOffset.UtcNow;
                sasBuilder.ExpiresOn = DateTimeOffset.UtcNow.AddHours(1);
                sasBuilder.SetPermissions(BlobContainerSasPermissions.All); //TODO Restrict?
            }
            else
            {
                sasBuilder.Identifier = storedPolicyName;
            }

            // Use the key to get the SAS token.
            string sasToken = sasBuilder.ToSasQueryParameters(sharedKeyCredential).ToString();

            //Console.WriteLine("SAS token for blob container is: {0}", sasToken);
            //Console.WriteLine();

            return $"{container.Uri}?{sasToken}";
        }

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
