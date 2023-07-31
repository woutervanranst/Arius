using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Services;

internal class FileService
{
    private readonly ILogger<FileService> logger;
    private readonly IHashValueProvider   hvp;

    public FileService(ILogger<FileService> logger, IHashValueProvider hvp)
    {
        this.logger = logger;
        this.hvp    = hvp;
    }

    // --------- GetExistingPointerFile ---------

    /// <summary>
    /// Return the EXISTING PointerFile for the given BinaryFileInfo.
    /// If the PointerFile does not exist, returns NULL.
    /// </summary>
    public PointerFile GetExistingPointerFile(DirectoryInfo root, BinaryFileInfo bfi)
    {
        return GetExistingPointerFile(root, FileSystemService.GetPointerFileInfo(bfi));
    }
    /// <summary>
    /// Return the EXISTING PointerFile Get the PointerFile corresponding to the PointerFileEntry, if it exists.
    /// If it does not, return null
    /// </summary>
    /// <param name="pfe"></param>
    /// <returns></returns>
    public PointerFile GetExistingPointerFile(DirectoryInfo root, PointerFileEntry pfe)
    {
        return GetExistingPointerFile(root, FileSystemService.GetPointerFileInfo(root, pfe));
    }
    /// <summary>
    /// Return the EXISTING PointerFile for the PointerFileInfo.
    /// If the PointerFile does not exist, returns NULL.
    /// </summary>
    public PointerFile GetExistingPointerFile(DirectoryInfo root, PointerFileInfo pfi)
    {
        if (!pfi.Exists) //TODO check op LastWriteTimeUtc ook?
            return null;

        var bh = ReadPointerFile(pfi.FullName);
        
        return new PointerFile(root, pfi, bh);
    }
    /// <summary>
    /// Return the EXISTING equivalent (in Name and LastWriteTime) PointerFile for the BinaryFile.
    /// If it does not exist, return NULL.
    /// </summary>
    public PointerFile GetExistingPointerFile(BinaryFile bf)
    {
        var pfi = FileSystemService.GetPointerFileInfo(bf);

        if (!pfi.Exists || pfi.LastWriteTimeUtc != File.GetLastWriteTimeUtc(bf.FullName))
            return null;

        return new PointerFile(bf.Root, pfi, bf.BinaryHash);
    }
    

    /// <summary>
    /// Return the EXISTING BinaryFile for the given BinaryFileInfo.
    /// If a corresponing PointerFile is present and the `fastHash` option is specified, use the hash of the PointerFile for speed
    /// If the BinaryFile does not exist, returns NULL
    /// </summary>
    public async Task<BinaryFile> GetExistingBinaryFileAsync(DirectoryInfo root, BinaryFileInfo bfi, bool fastHash)
    {
        if (!bfi.Exists)
            return null;

        var relativeName = Path.GetRelativePath(root.FullName, bfi.FullName);
        var bh = await GetPointerFileHash();
        return new BinaryFile(root, bfi, bh);


        async Task<BinaryHash> GetPointerFileHash()
        {
            BinaryHash hash = default;
            if (fastHash)
            {
                var pfFullName = bfi.PointerFileFullName;

                if (File.Exists(pfFullName))
                {
                    // We are doing FastHash and the PointerFile exists
                    hash = ReadPointerFile(pfFullName);

                    logger.LogInformation($"Found PointerFile for BinaryFile '{relativeName}' using fasthash '{hash.ToShortString()}'");

                    return hash;
                }
            }

            logger.LogInformation($"Found BinaryFile '{relativeName}'. Hashing...");

            var (MBps, _, seconds) = await new Stopwatch().GetSpeedAsync(bfi.Length, async () =>
                hash = await hvp.GetBinaryHashAsync(bfi.FullName));

            logger.LogInformation($"Found BinaryFile '{relativeName}'. Hashing... done in {seconds}s at {MBps} MBps. Hash: '{hash.ToShortString()}'");

            return hash;
        }
    }
    
    /// <summary>
    /// Return the EXISTING BinaryFile that corresponds with the given PointerFile
    /// If the BinaryFile does not exist, return NULL
    /// 
    /// If `assertHash` is true, ensure the hash of the BinaryFile matches.
    /// If the hash does not match, throws an InvalidOperationException
    /// </summary>
    /// <param name="pf"></param>
    /// <param name="assertHash"></param>
    /// <returns></returns>
    public async Task<BinaryFile> GetExistingBinaryFileAsync(PointerFile pf, bool assertHash)
    {
        var bfi = FileSystemService.GetBinaryFileInfo(pf);

        return await GetExistingBinaryFileAsync(pf.Root, bfi, pf.BinaryHash, assertHash);
    }

    public async Task<BinaryFile> GetExistingBinaryFileAsync(DirectoryInfo root, PointerFileEntry pfe, bool assertHash)
    {
        var bfi = FileSystemService.GetBinaryFileInfo(root, pfe);

        return await GetExistingBinaryFileAsync(root, bfi, pfe.BinaryHash, assertHash);
    }

    /// <summary>
    /// Return the EXISTING BinaryFile for the given BinaryFileInfo
    /// If the BinaryFile does not exist, return NULL
    ///
    /// If an `assertHash` is true, ensure the hash of the BinaryFile matches.
    /// If the hash does not match, throws an InvalidOperationException
    /// </summary>
    private async Task<BinaryFile> GetExistingBinaryFileAsync(DirectoryInfo root, BinaryFileInfo bfi, BinaryHash hash, bool assertHash)
    {
        if (!bfi.Exists)
            return null;

        if (assertHash)
        {
            var bfh = await hvp.GetBinaryHashAsync(bfi);
            if (bfh != hash)
                throw new InvalidOperationException($"The existing BinaryFile {bfi} is out of sync (invalid hash) with the PointerFile. The hash of the BinaryFile is '{bfh.ToShortString()}' and the expected hash is '{hash.ToShortString()}. Delete the BinaryFile and try again. If the file is intact, was it archived with fasthash and another password?");
        }

        return new BinaryFile(root, bfi, hash);
    }


    // -------- Create PointerFile ---------

    /// <summary>
    /// Create the PointerFile for this BinaryFile
    /// If it exists, ensure it is correct
    /// If it does not exist, create it
    /// </summary>
    public (bool created, PointerFile pf) CreatePointerFileIfNotExists(BinaryFile bf)
    {
        var pfi = FileSystemService.GetPointerFileInfo(bf);

        return CreatePointerFileIfNotExists(bf.Root, pfi, bf.BinaryHash, bf.CreationTimeUtc, bf.LastWriteTimeUtc);
    }
    /// <summary>
    /// Create the PointerFile for this PointerFileEntry
    /// If it exists, ensure it is correct
    /// If it does not exist, create it
    /// </summary>
    public (bool created, PointerFile pf) CreatePointerFileIfNotExists(DirectoryInfo root, PointerFileEntry pfe)
    {
        var pfi = FileSystemService.GetPointerFileInfo(root, pfe);

        return CreatePointerFileIfNotExists(root, pfi, pfe.BinaryHash, pfe.CreationTimeUtc!.Value, pfe.LastWriteTimeUtc!.Value);
    }
    private (bool created, PointerFile pf) CreatePointerFileIfNotExists(DirectoryInfo root, PointerFileInfo pfi, BinaryHash hash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        if (pfi.Exists)
        {
            // If the PointerFile exists, ensure it is correct
            var bh = ReadPointerFile(pfi.FullName);

            if (bh != hash)
            {
                // TODO is this path tested?
                logger.LogWarning($"The PointerFile {pfi} is broken. Overwriting");
                pfi.Delete();
                return CreatePointerFileIfNotExists(root, pfi, hash, creationTimeUtc, lastWriteTimeUtc);
            }

            return (false, GetExistingPointerFile(root, pfi));
        }
        else
        {
            // If the PointerFile does not exist, create it
            WritePointerFile(pfi, hash, creationTimeUtc, lastWriteTimeUtc);

            return (true, GetExistingPointerFile(root, pfi));
        }
    }
    
    
    // -------- Read/Write PointerFile ---------
    private BinaryHash ReadPointerFile(string pfFullName)
    {
        if (!File.Exists(pfFullName))
            throw new ArgumentException($"'{pfFullName}' does not exist");

        try
        {
            var pfc = JsonSerializer.Deserialize<PointerFileContents>(File.ReadAllBytes(pfFullName)); // TODO refactor to async - BUT THIS CASCASES ALL THE WAY UP
            var bh  = new BinaryHash(pfc.BinaryHash);

            if (!hvp.IsValid(bh))
                throw new ArgumentException($"'{pfFullName}' is not a valid PointerFile");

            return bh;
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"'{pfFullName}' is not a valid PointerFile", e);
        }
    }
    private void WritePointerFile(PointerFileInfo pfi, BinaryHash hash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        pfi.Directory.CreateIfNotExists();

        var pfc  = new PointerFileContents(hash.Value);
        var json = JsonSerializer.SerializeToUtf8Bytes(pfc); //ToUtf8 is faster https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to?pivots=dotnet-6-0#serialize-to-utf-8

        //using var s = pfi.OpenWrite();
        //s.Write(json);
        //s.Close();
        File.WriteAllBytes(pfi.FullName, json); //TODO bug in netcore6rc1 - WriteAllBytes keeps the file open?
        //TODO to Async but this cascades all the way up

        pfi.CreationTimeUtc = creationTimeUtc;
        pfi.LastWriteTimeUtc = lastWriteTimeUtc;

        logger.LogInformation($"Created PointerFile '{pfi.FullName}'");
    }
    private record PointerFileContents(string BinaryHash);
}