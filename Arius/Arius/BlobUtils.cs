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
                process.OutputDataReceived += (sender, data) => output += data.Data; //System.Diagnostics.Debug.WriteLine(data.Data);
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

                    var dir = filesGroupedPerDirectory.Key;
                    var sas = GetContainerSasUri(_bcc, _skc);
                    var files = string.Join(';', filesGroupedPerDirectory.Select(af => Path.GetRelativePath(dir, af.FullName)));

                    string arguments;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                        arguments = $"copy '{dir}' '{sas}' --include-path '{files}'";
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        arguments = $"copy \"{dir}\" \"{sas}\" --include-path \"{files}\"";
                    else
                        throw new NotImplementedException("OS Platform is not Windows or Linux");

                    ShellExecute(_azCopyPath, arguments);
                });

            

            //	azcopy copy 'C:\myDirectory' 'https://mystorageaccount.file.core.windows.net/myfileshare?sAiqIddM845yiyFwdMH481QA8%3D' --include-path 'photos;documents\myFile.txt'





            /*
             *
$env:AZCOPY_CRED_TYPE = "Anonymous";
./azcopy.exe copy "C:\Users\Wouter\Documents\Test\*" "https://vanranstarius.blob.core.windows.net/series/?sv=2019-10-10&se=2020-12-07T07%3A15%3A50Z&sr=c&sp=rwl&sig=HHqytgEpAe3PRyj4VdoK16RCtDl83uMer6cYzlCKNTQ%3D" --overwrite=prompt --from-to=LocalBlob --blob-type Detect --follow-symlinks --put-md5 --follow-symlinks --list-of-files "C:\Users\Wouter\AppData\Local\Temp\stg-exp-azcopy-edbfb78c-2128-4031-a2d6-7b2c6c986ec5.txt" --recursive;
$env:AZCOPY_CRED_TYPE = "";

             *
             */
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
