using System.Text.Json;
using Arius.Core.Domain;
using Arius.Core.Domain.Storage.FileSystem;

namespace Arius.Core.Infrastructure.Storage.LocalFileSystem;

public static class PointerFileSerializer
{
    /// <summary>
    /// Write the PointerFile to disk
    /// </summary>
    public static IPointerFileWithHash Create(IBinaryFileWithHash bfwh) => Create(bfwh.Root, bfwh.RelativeName + IPointerFile.Extension, bfwh.Hash, bfwh.CreationTimeUtc.Value, bfwh.LastWriteTimeUtc.Value);
    public static IPointerFileWithHash Create(DirectoryInfo root, PointerFileEntry pfe) => Create(root, pfe.RelativeName + IPointerFile.Extension, pfe.Hash, pfe.CreationTimeUtc, pfe.LastWriteTimeUtc);
    public static IPointerFileWithHash Create(DirectoryInfo root, string relativeName, Hash hash, DateTime creationTimeUtc, DateTime lastWriteTimeUtc)
    {
        var pfwh = PointerFileWithHash.FromRelativeName(root, relativeName, hash);

        Directory.CreateDirectory(pfwh.Path);

        var pfc  = new PointerFileContents(hash.Value.BytesToHexString());
        var json = JsonSerializer.SerializeToUtf8Bytes(pfc); //ToUtf8 is faster https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-how-to?pivots=dotnet-6-0#serialize-to-utf-8
        System.IO.File.WriteAllBytes(pfwh.FullName, json);

        pfwh.CreationTimeUtc  = creationTimeUtc;
        pfwh.LastWriteTimeUtc = lastWriteTimeUtc;

        return pfwh;
    }


    /// <summary>
    /// Get a PointerFile with Hash by reading the value in the PointerFile
    /// </summary>
    /// <returns></returns>
    public static IPointerFileWithHash FromExistingPointerFile(IPointerFile pf)
    {
        if (!System.IO.File.Exists(pf.FullName))
            throw new ArgumentException($"'{pf.FullName}' does not exist");

        try
        {
            var json = System.IO.File.ReadAllBytes(pf.FullName);
            var pfc  = JsonSerializer.Deserialize<PointerFileContents>(json);
            var h    = new Hash(pfc.BinaryHash);

            return PointerFileWithHash.FromFullName(pf.Root, pf.FullName, h);
        }
        catch (JsonException e)
        {
            throw new ArgumentException($"'{pf.FullName}' is not a valid PointerFile", e);
        }
    }

    private record PointerFileContents(string BinaryHash);
}