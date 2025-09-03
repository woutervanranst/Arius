using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Tests.Helpers.Fixtures;
using System.Text;
using Zio;

namespace Arius.Core.Tests.Helpers.Extensions;

internal record FilePairWithHash(FilePair FilePair, Hash Hash);

internal static class FileSystemExtensions
{
    public static FilePairWithHash WithSourceFolderHavingFilePair(this IFileSystem fileSystem, UPath binaryFileRelativeName, FilePairType type, int sizeInBytes, int? seed = default, FileAttributes attributes = FileAttributes.Normal)
    {
        if (!binaryFileRelativeName.IsAbsolute)
            throw new ArgumentException("Should start with /");

        if ((attributes & FileAttributes.Hidden) != 0 && !binaryFileRelativeName.GetName().StartsWith("."))
            throw new ArgumentException("Hidden files should start with a dot (.) in their name by Linux convention");

        var fe = new FileEntry(fileSystem, binaryFileRelativeName);
        fe.Directory.Create();

        var createBinary = type is FilePairType.BinaryFileWithPointerFile or FilePairType.BinaryFileOnly;
        var createPointer = type is FilePairType.BinaryFileWithPointerFile or FilePairType.PointerFileOnly;

        Hash? binaryFileHash = null;
        Hash? pointerFileHash = null;

        // 1. Create the Binary File if needed
        if (createBinary)
        {
            var content = GenerateRandomContent(sizeInBytes, seed);
            using (var binaryStream = fileSystem.OpenFile(binaryFileRelativeName, FileMode.Create, FileAccess.Write))
            {
                binaryStream.Write(content);
            }

            fileSystem.SetAttributes(binaryFileRelativeName, attributes);
            binaryFileHash = ComputeSha256String(FixtureBase.PASSPHRASE, content);
        }

        // 2. Create the Pointer File if needed
        if (createPointer)
        {
            var pointerPath = binaryFileRelativeName.GetPointerFilePath();
            using var pointerStream = fileSystem.OpenFile(pointerPath, FileMode.Create, FileAccess.Write);
            pointerFileHash = binaryFileHash ?? GenerateRandomSha256(seed);

            var pointerData = Encoding.UTF8.GetBytes($"{{\"BinaryHash\":\"{pointerFileHash}\"}}");
            pointerStream.Write(pointerData);
        }

        // 3. Build the FilePair
        var fp = FilePair.FromBinaryFileFileEntry(new FileEntry(fileSystem, binaryFileRelativeName));
        
        var finalHash = pointerFileHash ?? binaryFileHash ?? throw new InvalidOperationException();

        return new FilePairWithHash(fp, finalHash);
    }

    private static byte[] GenerateRandomContent(int sizeInBytes, int? localSeed)
    {
        var random = localSeed is null ? new Random() : new Random(localSeed.Value);
        var data = new byte[sizeInBytes];
        random.NextBytes(data);
        return data;
    }

    private static Hash ComputeSha256String(string passphrase, byte[] data)
    {
        var hasher = new Sha256Hasher(passphrase);
        return hasher.GetHashAsync(data).Result;
    }

    private static Hash GenerateRandomSha256(int? localSeed)
    {
        return GenerateRandomContent(32, localSeed);
    }
}