using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;

namespace Arius.CommandLines
{
    internal class RestoreCommand
    {
        /*
         * arius restore
               --accountname <accountname> 
               --accountkey <accountkey> 
               --passphrase <passphrase>
              (--download)
         */

        //public static Command GetCommand()
        //{
        //    var restoreCommand = new Command("restore", "Restore from blob");

        //    var accountNameOption = new Option<string>("--accountname",
        //            "Blob Account Name");
        //    accountNameOption.AddAlias("-n");
        //    accountNameOption.IsRequired = true;
        //    restoreCommand.AddOption(accountNameOption);


        //    var accountKeyOption = new Option<string>("--accountkey",
        //        "Account Key");
        //    accountKeyOption.AddAlias("-k");
        //    accountKeyOption.IsRequired = true;
        //    restoreCommand.AddOption(accountKeyOption);

        //    var passphraseOption = new Option<string>("--passphrase",
        //        "Passphrase");
        //    passphraseOption.AddAlias("-p");
        //    passphraseOption.IsRequired = true;
        //    restoreCommand.AddOption(passphraseOption);

        //    var containerOption = new Option<string>("--container",
        //        getDefaultValue: () => "arius",
        //        description: "Blob container to use");
        //    containerOption.AddAlias("-c");
        //    restoreCommand.AddOption(containerOption);

        //    var downloadOption = new Option<bool>("--download",
        //        "List the differences between the local and the remote, without making any changes to remote");
        //    restoreCommand.AddOption(downloadOption);

        //    var pathArgument = new Argument<string>("path",
        //        getDefaultValue: () => Environment.CurrentDirectory,
        //        "Path to archive. Default: current directory");
        //    restoreCommand.AddArgument(pathArgument);

        //    restoreCommand.Handler = CommandHandler.Create<string, string, string, string, bool, string>((accountName, accountKey, passphrase, container, download, path) =>
        //    {
        //        var archive = new AriusRemoteArchive(accountName, accountKey, container);

        //        return Execute(archive, passphrase, download, path);
        //    });

        //    return restoreCommand;
        //}

        //public static int Execute(AriusRemoteArchive archive, string passphrase, bool download, string path)
        //{
        //    if (Directory.Exists(path))
        //    {
        //        // Synchronize a folder
        //        var root = new AriusRootDirectory(path);

        //        Synchronize(archive, root, passphrase);

        //        if (download)
        //            Download(archive, root, passphrase);
        //    }
        //    else if (File.Exists(path) && path.EndsWith(".arius"))
        //    {
        //        // Restore one file

        //    }
        //    else
        //    {
        //        throw new NotImplementedException();
        //    }
        //    return 0;
        //}



        //private static int Synchronize(AriusRemoteArchive archive, AriusRootDirectory root, string passphrase)
        //{
        //    var cbn = archive.GetRemoteEncryptedAriusManifests().ToImmutableArray();

        //    Console.Write($"Getting {cbn.Length} manifests... ");

        //    var pointerEntriesperManifest = cbn
        //        .AsParallel()
        //        .ToImmutableDictionary(
        //            ream => ream,
        //            ream => ream.GetAriusPointerFileEntries(passphrase).ToList());

        //    Console.WriteLine($"Done. {pointerEntriesperManifest.Values.Count()} files in latest version of remote");


        //    Console.WriteLine($"Synchronizing state of local folder with remote... ");

        //    // 1. FILES THAT EXIST REMOTE BUT NOT LOCAL --> TO BE CREATED
        //    var createdPointers = pointerEntriesperManifest
        //        .AsParallel()
        //        .WithDegreeOfParallelism(1)
        //        .SelectMany(p => p.Value
        //                .Where(afpe => !root.Exists(afpe))
        //                .Select(afpe =>
        //                {
        //                    var apf = AriusPointerFile.Create(root, afpe, p.Key);
        //                    Console.WriteLine($"File '{apf.RelativeLocalContentFileName}' created");

        //                    return apf;
        //                }))
        //        .ToImmutableArray();

        //    Console.WriteLine();

        //    // 2. FILES THAT EXIST LOCAL BUT NOT REMOTE --> TO BE DELETED
        //    var relativeNamesThatShouldExist = pointerEntriesperManifest.Values.SelectMany(x => x).Select(x => x.RelativeName); //root.GetFullName(x));

        //    root.GetAriusPointerFiles()
        //        .Where(apf => !relativeNamesThatShouldExist.Contains(apf.RelativeLocalContentFileName))
        //        .AsParallel()
        //        .ForAll(apfe =>
        //        {
        //            File.Delete(apfe.FullName);

        //            Console.WriteLine($"Pointer for '{apfe.RelativeLocalContentFileName}' deleted");
        //        });

        //    DirectoryExtensions.DeleteEmptySubdirectories(root.FullName);




        //    /*
        //     * Test cases
        //     *      empty dir
        //     *      dir with files > not to be touched?
        //     *      dir with pointers - too many pointers > to be deleted
        //     *      dir with pointers > not enough pointers > to be synchronzed
        //     *      remote with isdeleted and local present > should be deleted
        //     *      remote with !isdeleted and local not present > should be created
        //     *      also in subdirectories
        //     *      in ariusfile : de verschillende extensions
        //     *      files met duplicates enz upload download
        //     *      al 1 file lokaal > kopieert de rest
        //     *      restore > normal binary file remains untouched
        //     * directory more than 2 deep without other files
        //     *  download > local files exist s> don't download all
        //     * */


        //    return 0;
        //}

        //private static int Download(AriusRemoteArchive archive, AriusRootDirectory root, string passphrase)
        //{
        //    var pointerFilesPerRemoteEncryptedManifest = root.GetAriusPointerFiles()
        //        .AsParallel()
        //        .Where(apf => !File.Exists(apf.LocalContentFileFullName)) //TODO test dit
        //        .GroupBy(apf => apf.EncryptedManifestName)
        //        .ToImmutableDictionary(
        //            apf => archive.GetRemoteEncryptedAriusManifestByBlobItemName(apf.Key),
        //            apf => apf.ToList());

        //    var chunkNamesToDownload = pointerFilesPerRemoteEncryptedManifest.Keys
        //        .AsParallel()
        //        .SelectMany(ream => ream.GetEncryptedChunkNames(passphrase))
        //        .Distinct()
        //        .ToImmutableArray();

        //    var downloadDirectory = new DirectoryInfo(Path.Combine(root.FullName, ".ariusdownload"));
        //    if (downloadDirectory.Exists)
        //        downloadDirectory.Delete(true);
        //    downloadDirectory.Create();
        //    archive.Download(chunkNamesToDownload, downloadDirectory);

        //    var unencryptedChunks = downloadDirectory.GetFiles()
        //        .AsParallel()
        //        .Select(fi =>
        //        {
        //            var szu = new SevenZipUtils();

        //            var extractedFile = fi.FullName.TrimEnd(".7z.arius");
        //            szu.DecryptFile(fi.FullName, extractedFile, passphrase);
        //            fi.Delete();

        //            return new FileInfo(extractedFile);
        //        })
        //        .ToImmutableDictionary(x => x.Name, x => x);

        //    var pointersWithChunksPerHash = pointerFilesPerRemoteEncryptedManifest.Keys
        //        .GroupBy(ream => ream.Hash)
        //        .ToDictionary(
        //            g => g.Key,
        //            g => new
        //            {
        //                PointerFileEntry = g.SelectMany(ream => ream.GetAriusPointerFileEntries(passphrase)).ToImmutableArray(),
        //                UnencryptedChunks = g.SelectMany(ream => ream.GetEncryptedChunkNames(passphrase).Select(ecn => unencryptedChunks[ecn.TrimEnd(".7z.arius")])).ToImmutableArray()
        //            });

        //    pointersWithChunksPerHash
        //        .AsParallel()
        //        .ForAll(p =>
        //        {
        //            Restore(root, p.Value.PointerFileEntry, p.Value.UnencryptedChunks);
        //        });

        //    downloadDirectory.Delete();
        //    root.GetAriusPointerFiles().AsParallel().ForAll(apf => apf.Delete());

        //    return 0;
        //}

        //public static void Restore(AriusRootDirectory root, ImmutableArray<RemoteEncryptedAriusManifest.AriusManifest.AriusPointerFileEntry> apfes, ImmutableArray<FileInfo> chunks)
        //{
        //    if (chunks.Length == 1)
        //    {
        //        //No dedup
        //        var chunk = chunks.Single();

        //        chunk.MoveTo(root.GetFullName(apfes.First()));

        //        chunk.CreationTimeUtc = apfes.First().CreationTimeUtc!.Value;
        //        chunk.LastWriteTimeUtc = apfes.First().LastWriteTimeUtc!.Value;

        //        apfes.Skip(1)
        //            .AsParallel()
        //            .ForAll(apfe =>
        //            {
        //                var copy = chunk.CopyTo(root.GetFullName(apfe), true);

        //                copy.CreationTimeUtc = apfe.CreationTimeUtc!.Value;
        //                copy.LastWriteTimeUtc = apfe.LastWriteTimeUtc!.Value;
        //            });
        //    }
        //    else
        //    {


        //    }

        //}
    }
}
