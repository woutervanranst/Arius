using System;
using System.IO;
using System.Text.Json;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Services;

internal class PointerService
{
    public PointerService(ILogger<PointerService> logger, IHashValueProvider hvp)
    {
        this.logger = logger;
        this.hvp = hvp;
    }

    private readonly ILogger<PointerService> logger;
    private readonly IHashValueProvider hvp;


    /// <summary>
    /// Create a pointer from a BinaryFile
    /// </summary>
    public (bool created, PointerFile pf) CreatePointerFileIfNotExists(BinaryFile bf)
    {
        var target = new FileInfo(GetPointerFileFullName(bf));

        return CreatePointerFileIfNotExists(
            target: target,
            root: bf.Root,
            binaryHash: bf.Hash,
            creationTimeUtc: File.GetCreationTimeUtc(bf.FullName),
            lastWriteTimeUtc: File.GetLastWriteTimeUtc(bf.FullName));
    }

    /// <summary>
    /// Create a pointer from a PointerFileEntry
    /// </summary>
    public (bool created, PointerFile pf) CreatePointerFileIfNotExists(DirectoryInfo root, PointerFileEntry pfe)
    {
        var target = new FileInfo(Path.Combine(root.FullName, pfe.RelativeName));

        return CreatePointerFileIfNotExists(
            target: target,
            root: root,
            binaryHash: pfe.BinaryHash,
            creationTimeUtc: pfe.CreationTimeUtc!.Value,
            lastWriteTimeUtc: pfe.LastWriteTimeUtc!.Value);
    }

    private (bool created, PointerFile pf) CreatePointerFileIfNotExists(FileInfo target, DirectoryInfo root, BinaryHash binaryHash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        if (!target.Exists)
        {
            // Create the PointerFile
            if (!target.Directory!.Exists) target.Directory.Create();

            var pfc = new PointerFileContents { BinaryHash = binaryHash.Value };
            var json = JsonSerializer.SerializeToUtf8Bytes(pfc); //ToUtf8 is faster https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to?pivots=dotnet-6-0#serialize-to-utf-8
            
            if (target.Directory is var d && !d.Exists) d.Create();
            using var s = target.OpenWrite();
            s.Write(json);
            s.Close();
            //File.WriteAllBytes(target.FullName, json); //TODO bug in netcore6rc1 - WriteAllBytes keeps the file open?

            //FileInfo does not work on Linux according to https://stackoverflow.com/a/17126045/1582323
            //pointerFileInfo.CreationTimeUtc = creationTimeUtc;
            //pointerFileInfo.LastWriteTimeUtc = lastWriteTimeUtc;
            File.SetCreationTimeUtc(target.FullName, creationTimeUtc);
            File.SetLastWriteTimeUtc(target.FullName, lastWriteTimeUtc);

            logger.LogInformation($"Created PointerFile '{target.GetRelativeName(root)}'");

            return (true, new PointerFile(root, target, binaryHash));
        }
        else
        {
            // The PointerFile exists
            var pf = OpenPointerFile(root, target);

            //Check whether the contents of the PointerFile are correct / is it a valid PointerFile / does the hash it refer to match the binaryHash (eg. not in the case of 0 bytes or ...)
            if (!pf.Hash.Equals(binaryHash))
            {
                //throw new ApplicationException($"The PointerFile {pf.RelativeName} is out of sync. Delete the file and restart the operation."); //TODO TEST

                //Graceful - Recreate the pointer
                logger.LogWarning($"The PointerFile {pf.RelativeName} is broken. Overwriting");
                target.Delete();
                (_, pf) = CreatePointerFileIfNotExists(target, root, binaryHash, creationTimeUtc, lastWriteTimeUtc);
            }

            return (false, pf);
        }
    }

    private struct PointerFileContents
    {
        public string BinaryHash { get; init; }
    }



    /// <summary>
    /// Get the PointerFile for the given FileInfo assuming it is in the root.
    /// If the FileInfo is for a PointerFile, return the PointerFile.
    /// If the FileInfo is for a BinaryFile, return the equivalent (in name and LastWriteTime) PointerFile, if it exists.
    /// If it does not exist, return null.
    /// </summary>
    public PointerFile GetPointerFile(FileInfo fi) => GetPointerFile(fi.Directory, fi);

    /// <summary>
    /// Get the PointerFile for the given FileInfo with the given root.
    /// If the FileInfo is for a PointerFile, return the PointerFile.
    /// If the FileInfo is for a BinaryFile, return the equivalent (in name and LastWriteTime) PointerFile, if it exists.
    /// If it does not exist, return null.
    /// </summary>
    public PointerFile GetPointerFile(DirectoryInfo root, FileInfo fi)
    {
        if (fi.IsPointerFile())
        {
            return OpenPointerFile(root, fi);
        }
        else
        {
            var pfi = new FileInfo(GetPointerFileFullName(fi.FullName));

            if (!pfi.Exists)
                return null; // if the PointerFile does not exist, return null

            if (File.Exists(fi.FullName) && pfi.LastWriteTimeUtc != fi.LastWriteTimeUtc) // TODO PointerComparer instead?
                return null; // if the BinaryFile exists but the LastWriteTime does not match, return null

            return OpenPointerFile(root, pfi);
        }
    }

    /// <summary>
    /// Get the equivalent (in name and LastWriteTime) PointerFile if it exists.
    /// If it does not exist, return null.
    /// </summary>
    /// <returns></returns>
    public PointerFile GetPointerFile(BinaryFile bf)
    {
        var pfi = new FileInfo(GetPointerFileFullName(bf));

        if (!pfi.Exists || pfi.LastWriteTimeUtc != File.GetLastWriteTimeUtc(bf.FullName))
            return null;

        return OpenPointerFile(bf.Root, pfi);
    }

    private PointerFile OpenPointerFile(DirectoryInfo root, FileInfo pointerFileInfo)
    {
        if (!pointerFileInfo.IsPointerFile())
            throw new ArgumentException($"'{pointerFileInfo.FullName}' is not a valid PointerFile");

        var bytes = File.ReadAllBytes(pointerFileInfo.FullName);
        PointerFileContents pfc;
        try
        {
            pfc = JsonSerializer.Deserialize<PointerFileContents>(bytes);
        }
        catch (JsonException e)
        {
            // is this a v1 PointerFile?
            if (new BinaryHash(File.ReadAllText(pointerFileInfo.FullName)) is var bh2 && hvp.IsValid(bh2))
            {
                // Upgrade v1
                logger.LogInformation($"Upgrading v1 PointerFile '{pointerFileInfo.FullName}'");
                var cr = pointerFileInfo.CreationTimeUtc;
                var lw = pointerFileInfo.LastWriteTimeUtc;
                pointerFileInfo.Delete();
                CreatePointerFileIfNotExists(pointerFileInfo, root, bh2, cr, lw);
                return new PointerFile(root, pointerFileInfo, bh2);
            }
            else
            {
                var e2 = new ArgumentException($"'{pointerFileInfo.FullName}' is not a valid PointerFile", e);
                logger.LogError(e2);
                
                throw e2;
            }
        }

        var bh = new BinaryHash(pfc.BinaryHash);

        if (!hvp.IsValid(bh))
            throw new ArgumentException($"'{pointerFileInfo.FullName}' is not a valid PointerFile");

        return new PointerFile(root, pointerFileInfo, bh);
    }

    /// <summary>
    /// Get the PointerFile corresponding to the PointerFileEntry, if it exists.
    /// If it does not, return null
    /// </summary>
    /// <param name="pfe"></param>
    /// <returns></returns>
    public PointerFile GetPointerFile(DirectoryInfo root, PointerFileEntry pfe)
    {
        var pfi = new FileInfo(GetPointerFileFullName(root, pfe));

        if (!pfi.Exists) //TODO check op LastWriteTimeUtc ook?
            return null;
            
        return OpenPointerFile(root, pfi);
    }

    internal static string GetPointerFileFullName(BinaryFile bf) => GetPointerFileFullName(bf.FullName);
    internal static string GetPointerFileFullName(FileInfo binaryFileInfo) => GetPointerFileFullName(binaryFileInfo.FullName);
    internal static string GetPointerFileFullName(string binaryFileFullName) => $"{binaryFileFullName}{PointerFile.Extension}";
    internal static string GetPointerFileFullName(DirectoryInfo root, PointerFileEntry pfe) => Path.Combine(root.FullName, pfe.RelativeName);

    
    /// <summary>
    /// Get the local BinaryFile for this pointer if it exists.
    /// If it does not exist, return null.
    /// </summary>
    /// <param name="pf"></param>
    /// <param name="ensureCorrectHash">If we find an existing BinaryFile, calculate the hash and ensure it is correct</param>
    /// <returns></returns>
    public BinaryFile GetBinaryFile(PointerFile pf, bool ensureCorrectHash)
    {
        ArgumentNullException.ThrowIfNull(pf);

        var bfi = new FileInfo(GetBinaryFileFullName(pf));

        return GetBinaryFile(pf.Root, bfi, pf.Hash, ensureCorrectHash);
    }

    public BinaryFile GetBinaryFile(DirectoryInfo root, PointerFileEntry pfe, bool ensureCorrectHash)
    {
        var bfi = new FileInfo(GetBinaryFileFullname(root, pfe));

        return GetBinaryFile(root, bfi, pfe.BinaryHash, ensureCorrectHash);
    }

    private BinaryFile GetBinaryFile(DirectoryInfo root, FileInfo bfi, BinaryHash binaryHash, bool ensureCorrectHash)
    {
        if (!bfi.Exists)
            return null;

        if (ensureCorrectHash)
        {
            var bh2 = hvp.GetBinaryHash(bfi.FullName);
            if (binaryHash != bh2)
            {
                logger.LogWarning($"Hash of {bfi.FullName}: '{bh2}'. Should be: '{binaryHash}'");
                throw new InvalidOperationException($"The existing BinaryFile {bfi.FullName} is out of sync (invalid hash) with the PointerFile. Delete the BinaryFile and try again. If the file is intact, was it archived with fasthash and another password?");
            }
        }

        return new BinaryFile(root, bfi, binaryHash);
    }

    private static string GetBinaryFileFullname(DirectoryInfo root, PointerFileEntry pfe) => GetBinaryFileFullName(GetPointerFileFullName(root, pfe));
    private static string GetBinaryFileFullName(PointerFile pf) => GetBinaryFileFullName(pf.FullName);
    public static string GetBinaryFileFullName(string pointerFileFullName) => pointerFileFullName.TrimEnd(PointerFile.Extension);
    
    public FileInfo GetBinaryFileInfo(PointerFile pf)
    {
        return new FileInfo(pf.FullName.TrimEnd(PointerFile.Extension));
    }
}