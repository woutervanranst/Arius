using System.Text.Json;
using Arius.Core.Domain;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Infrastructure.Storage.LocalFileSystem;

public class PointerFileSerializer
{
    private readonly ILogger<PointerFileSerializer> logger;

    public PointerFileSerializer(ILogger<PointerFileSerializer> logger)
    {
        this.logger = logger;
    }

    public enum CreationResult
    {
        Created,
        Overwritten,
        Existed
    }

    /// <summary>
    /// Write the PointerFile to disk
    /// </summary>
    public (CreationResult Created, IPointerFileWithHash PointerFileWithHash) CreateIfNotExists(IBinaryFileWithHash bfwh) => CreateIfNotExists(bfwh.Root, bfwh.RelativeName + IPointerFile.Extension, bfwh.Hash, bfwh.CreationTimeUtc.Value, bfwh.LastWriteTimeUtc.Value);
    public (CreationResult Created, IPointerFileWithHash PointerFileWithHash) CreateIfNotExists(DirectoryInfo root, PointerFileEntry pfe) => CreateIfNotExists(root, pfe.RelativeName + IPointerFile.Extension, pfe.Hash, pfe.CreationTimeUtc, pfe.LastWriteTimeUtc);
    public (CreationResult Created, IPointerFileWithHash PointerFileWithHash) CreateIfNotExists(DirectoryInfo root, string relativeName, Hash hash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        var  pfwh    = PointerFileWithHash.FromRelativeName(root, relativeName, hash);
        var existed = false;

        if (pfwh.Exists)
        {
            existed = true;
            try
            {
                var existing = FromExistingPointerFile(pfwh);

                if (existing.Hash == hash &&
                    existing.CreationTimeUtc == creationTimeUtc &&
                    existing.LastWriteTimeUtc == lastWriteTimeUtc)
                    return (CreationResult.Existed, pfwh);
            }
            catch (InvalidDataException)
            {
                logger.LogWarning("The existing PointerFile {pointerFile} was broken. Overwriting...", pfwh);
            }
        }

        Directory.CreateDirectory(pfwh.Path);

        var pfc  = new PointerFileContents(hash.Value.BytesToHexString());
        var json = JsonSerializer.SerializeToUtf8Bytes(pfc); //ToUtf8 is faster https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to?pivots=dotnet-6-0#serialize-to-utf-8
        System.IO.File.WriteAllBytes(pfwh.FullName, json);

        pfwh.CreationTimeUtc  = creationTimeUtc;
        pfwh.LastWriteTimeUtc = lastWriteTimeUtc;

        return (existed ? CreationResult.Overwritten : CreationResult.Created, pfwh);
    }


    /// <summary>
    /// Retrieves a <see cref="IPointerFileWithHash"/> by reading and deserializing the contents of the specified <see cref="IPointerFile"/>.
    /// </summary>
    /// <param name="pf">The pointer file to be read and processed.</param>
    /// <returns>An instance of <see cref="IPointerFileWithHash"/> containing the pointer file data and its associated hash.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the specified file does not exist at <paramref name="pf"/>.</exception>
    /// <exception cref="InvalidDataException">Thrown if the file contains invalid data or is not a valid pointer file.</exception>
    public IPointerFileWithHash FromExistingPointerFile(IPointerFile pf)
    {
        if (!System.IO.File.Exists(pf.FullName))
            throw new FileNotFoundException($"'{pf.FullName}' does not exist");

        try
        {
            var json = System.IO.File.ReadAllBytes(pf.FullName);
            var pfc  = JsonSerializer.Deserialize<PointerFileContents>(json);
            var h    = new Hash(pfc.BinaryHash);

            return PointerFileWithHash.FromFullName(pf.Root, pf.FullName, h);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"'{pf.FullName}' is not a valid PointerFile", e);
        }
    }

    private record PointerFileContents(string BinaryHash);
}