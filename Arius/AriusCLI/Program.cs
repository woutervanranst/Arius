using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AriusCLI
{
    class Program
    {
        static int Main(string[] args)
        {
            //setup our DI
            var serviceProvider = new ServiceCollection()
                //.AddLogging()
                .AddSingleton<Archive>()
                .AddSingleton<SevenZipUtils>()
                .BuildServiceProvider();

            var a = serviceProvider.GetService<Archive>();

            var rootCommand = new RootCommand();
            rootCommand.AddCommand(a.GetArchiveCommand());

            rootCommand.Description = "ROOTCOMMAND DESCR";

            return rootCommand.InvokeAsync(args).Result;
        }
    }

    class Archive
    {
        public Archive(SevenZipUtils szu)
        {
            _szu = szu;
        }
        private readonly SevenZipUtils _szu;

        /*
             *  arius archive 
                    --accountname <accountname> 

                    --accountkey <accountkey> 
                    --passphrase <passphrase>
                    (--container <containername>) 
                    (--keep-local)
                    (--tier=(hot/cool/archive))
                    (--min-size=<minsizeinMB>)
                    (--simulate)
             * */
        public Command GetArchiveCommand()
        {
            var archiveCommand = new Command("archive", "Archive to blob");

            var accountNameOption = new Option<string>("--accountname",
                    "Blob Account Name");
            accountNameOption.AddAlias("-n");
            accountNameOption.IsRequired = true;
            archiveCommand.AddOption(accountNameOption);

            var accountKeyOption = new Option<string>("--accountkey",
                "Account Key");
            accountKeyOption.AddAlias("-k");
            accountKeyOption.IsRequired = true;
            archiveCommand.AddOption(accountKeyOption);

            var passphraseOption = new Option<string>("--passphrase",
                "Passphrase");
            passphraseOption.AddAlias("-p");
            passphraseOption.IsRequired = true;
            archiveCommand.AddOption(passphraseOption);

            var containerOption = new Option<string>("--container",
                getDefaultValue: () => "arius",
                description: "Blob container to use");
            containerOption.AddAlias("-c");
            archiveCommand.AddOption(containerOption);

            var keepLocalOption = new Option<bool>("--keep-local",
                "Do not delete the local copies of the file after a successful upload");
            archiveCommand.AddOption(keepLocalOption);

            var tierOption = new Option<string>("--tier",
                getDefaultValue: () => "archive",
                description: "Storage tier to use. Defaut: archive");
            tierOption.AddValidator(o =>
            {
                // As per https://github.com/dotnet/command-line-api/issues/476#issuecomment-476723660
                var tier = o.GetValueOrDefault<string>();

                string[] tiers = { "hot", "cool", "archive" };
                if (!tiers.Contains(tier))
                    return $"{tier} is not a valid tier (hot|cool|archive)";

                return string.Empty;
            });
            archiveCommand.AddOption(tierOption);

            var minSizeOption = new Option<int>("--min-size",
                getDefaultValue: () => 1,
                description: "Minimum size of files to archive in MB");
            archiveCommand.AddOption(minSizeOption);

            var simulateOption = new Option<bool>("--simulate",
                "List the differences between the local and the remote, without making any changes to remote");
            archiveCommand.AddOption(simulateOption);

            //var pathArgument = new Argument<DirectoryInfo>("path", 
            //    getDefaultValue: () => new DirectoryInfo(Environment.CurrentDirectory),
            //    "Path to archive. Default: current directory");
            //pathArgument.ExistingOnly();    // https://github.com/dotnet/command-line-api/blob/main/docs/model-binding.md#file-system-types
            //archiveCommand.AddArgument(pathArgument);

            var pathArgument = new Argument<string>("path",
                getDefaultValue: () => Environment.CurrentDirectory,
                "Path to archive. Default: current directory");
            archiveCommand.AddArgument(pathArgument);

            ArchiveDelegate archiveCommandHandler = Execute;

            archiveCommand.Handler = CommandHandler.Create(archiveCommandHandler);

            return archiveCommand;
        }

        delegate Task<int> ArchiveDelegate(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path);

        private async Task<int> Execute(string accountName, string accountKey, string passphrase, string container, bool keepLocal, string tier, int minSize, bool simulate, string path)
        {
            var bu = new BlobUtils(accountName, accountKey, container);

            var di = new DirectoryInfo(path);

            var accessTier = tier switch
            {
                "hot" => AccessTier.Hot,
                "cool" => AccessTier.Cool,
                "archive" => AccessTier.Archive,
                _ => throw new NotImplementedException()
            };

            return await Execute(passphrase, bu, keepLocal, accessTier, minSize, simulate, di);
        }

        private async Task<int> Execute(string passphrase, BlobUtils bu, bool keepLocal, AccessTier tier, int minSize, bool simulate, DirectoryInfo dir)
        {
            foreach (var fi in dir.GetFiles())
            {
                if (fi.Length < minSize * 1024 * 1024)
                    continue;

                if (fi.Name.EndsWith(".arius"))
                    continue;

                
                Console.WriteLine($"Archiving file: {fi.FullName}");
                var source = fi.FullName;
                var encryptedSource = Path.Combine(dir.FullName, $"{fi.Name}.7z.arius");
                var blobTarget = $"{Guid.NewGuid()}.7z.arius";
                var localTarget = Path.Combine(dir.FullName, $"{fi.Name}.arius");

                Console.Write("Encrypting... ");
                _szu.Encrypt(source, encryptedSource, passphrase);
                Console.WriteLine("Done");

                Console.Write("Uploading... ");
                bu.Upload(encryptedSource, blobTarget, tier);
                Console.WriteLine("Done");

                Console.Write("Creating local pointer... ");
                File.WriteAllText(localTarget, blobTarget, Encoding.UTF8);
                Console.WriteLine("Done");

                if (keepLocal)
                {
                    throw new NotImplementedException();
                }
                else
                {
                    Console.Write("Deleting local file... ");
                    File.Delete(source);
                    Console.WriteLine("Done");
                }

                File.Delete(encryptedSource);


                Console.WriteLine("");
            }

            return 0;
        }
    }

    class SevenZipUtils
    {
        public void Encrypt(string sourceFile, string targetFile, string password)
        {
            var lib = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), Environment.Is64BitProcess ? "x64" : "x86", "7z.dll");
            SevenZip.SevenZipBase.SetLibraryPath(lib);

            var compressor = new SevenZip.SevenZipCompressor();
            compressor.ArchiveFormat = SevenZip.OutArchiveFormat.SevenZip;
            compressor.CompressionLevel = SevenZip.CompressionLevel.None;
            compressor.EncryptHeaders = true;
            compressor.ZipEncryptionMethod = SevenZip.ZipEncryptionMethod.Aes256;

            compressor.CompressFilesEncrypted(targetFile, password, sourceFile);
        }

        //internal class ZipUtils
        //{
        //    public ZipUtils(string passphrase)
        //    {
        //        _passphrase = passphrase;
        //    }

        //    private string _passphrase;

        //    private string _sevenZipPath = @"C:\Program Files\7-Zip\7z.exe";

        //    public void Compress(string sourceFile, string targetFile)
        //    {
        //        try
        //        {
        //            using (var proc = new Process())
        //            {
        //                proc.StartInfo.FileName = _sevenZipPath;

        //                // -mhe=on      = HEADER ENCRYPTION
        //                // -mx0         = NO COMPRESSION
        //                proc.StartInfo.Arguments = $"a -p{_passphrase} \"{targetFile}\" \"{sourceFile}\" -mhe=on -mx0";

        //                proc.StartInfo.UseShellExecute = false;
        //                proc.StartInfo.RedirectStandardOutput = true;
        //                proc.StartInfo.RedirectStandardError = true;

        //                bool hasError = false;
        //                string errorMsg = string.Empty;

        //                proc.OutputDataReceived += (sender, data) => System.Diagnostics.Debug.WriteLine(data.Data);
        //                proc.ErrorDataReceived += (sender, data) =>
        //                {
        //                    if (data.Data == null)
        //                        return;

        //                    System.Diagnostics.Debug.WriteLine(data.Data);

        //                    hasError = true;
        //                    errorMsg += data.Data;
        //                };

        //                proc.Start();
        //                proc.BeginOutputReadLine();
        //                proc.BeginErrorReadLine();

        //                proc.WaitForExit();

        //                if (proc.ExitCode != 0 || hasError)
        //                {
        //                    //7z output codes https://superuser.com/questions/519114/how-to-write-error-status-for-command-line-7-zip-in-variable-or-instead-in-te

        //                    if (File.Exists(targetFile))
        //                        File.Delete(targetFile);

        //                    throw new ApplicationException($"Error while compressing :  {errorMsg}");
        //                }
        //            }
        //        }
        //        catch (Win32Exception e) when (e.Message == "The system cannot find the file specified.")
        //        {
        //            //7zip not installed
        //            throw new ApplicationException("7Zip CLI Not Installed", e);
        //        }
        //    }
        //}
    }

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

        public void Upload(string sourceFile, string targetFile, AccessTier tier)
        {
            var bc = _bcc.GetBlobClient(targetFile);

            // Open the file and upload its data
            using FileStream uploadFileStream = File.OpenRead(sourceFile);
            var r = bc.Upload(uploadFileStream, true);
            uploadFileStream.Close();

            bc.SetAccessTier(tier);
        }
    }
}
