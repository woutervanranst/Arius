using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Tests.Helpers.Fixtures;
using System.Text;
using Zio;

namespace Arius.Core.Tests.Helpers.Fakes;

internal record FakeFile(FilePair FilePair, Hash OriginalHash, byte[] OriginalContent, UPath OriginalPath, DateTime OriginalCreationDateTimeUtc, DateTime OriginalLastWriteTimeUtc);

internal static class FakeFileBuilder
{
    public static FakeFile WithSourceFolderHavingFilePair(this IFileSystem fileSystem, UPath binaryFileRelativeName, FilePairType type, int sizeInBytes, int? seed = null, FileAttributes attributes = FileAttributes.Normal, DateTime? creationTimeUtc = null, DateTime? lastWriteTimeUtc = null)
    {
        var content = GenerateRandomContent(sizeInBytes, seed);
        return WithSourceFolderHavingFilePair(fileSystem, binaryFileRelativeName, type, content, attributes, creationTimeUtc, lastWriteTimeUtc);
    }

    public static FakeFile WithSourceFolderHavingFilePair(this IFileSystem fileSystem, UPath binaryFileRelativeName, FilePairType type, byte[] content, FileAttributes attributes = FileAttributes.Normal, DateTime? creationTimeUtc = null, DateTime? lastWriteTimeUtc = null)
    {
        if (!binaryFileRelativeName.IsAbsolute)
            throw new ArgumentException("Should start with /");

        if ((attributes & FileAttributes.Hidden) != 0 && !binaryFileRelativeName.GetName().StartsWith("."))
            throw new ArgumentException("Hidden files should start with a dot (.) in their name by Linux convention");

        creationTimeUtc  ??= DateTime.FromFileTimeUtc(0); // ensure minimum valid Win32 file time (1601-01-01) instead of DateTime.MinValue
        lastWriteTimeUtc ??= DateTime.FromFileTimeUtc(0); 

        var fe = new FileEntry(fileSystem, binaryFileRelativeName);
        fe.Directory.Create();

        var createBinary  = type is FilePairType.BinaryFileWithPointerFile or FilePairType.BinaryFileOnly;
        var createPointer = type is FilePairType.BinaryFileWithPointerFile or FilePairType.PointerFileOnly;

        var binaryFileHash = ComputeSha256String(FixtureBase.PASSPHRASE, content);


        // 1. Create the Binary File if needed
        if (createBinary)
        {
            using (var binaryStream = fileSystem.OpenFile(binaryFileRelativeName, FileMode.Create, FileAccess.Write))
            {
                binaryStream.Write(content);
            }

            fileSystem.SetAttributes(binaryFileRelativeName, attributes);

            fileSystem.SetCreationTimeUtc(binaryFileRelativeName, creationTimeUtc.Value);
            fileSystem.SetLastWriteTimeUtc(binaryFileRelativeName, lastWriteTimeUtc.Value);
        }

        // 2. Create the Pointer File if needed
        if (createPointer)
        {
            var       pointerPath   = binaryFileRelativeName.GetPointerFilePath();
            using var pointerStream = fileSystem.OpenFile(pointerPath, FileMode.Create, FileAccess.Write);

            var pointerData = Encoding.UTF8.GetBytes($"{{\"BinaryHash\":\"{binaryFileHash}\"}}");
            pointerStream.Write(pointerData);

            fileSystem.SetCreationTimeUtc(pointerPath, creationTimeUtc.Value);
            fileSystem.SetLastWriteTimeUtc(pointerPath, lastWriteTimeUtc.Value);
        }

        // 3. Build the FilePair
        var fp = FilePair.FromBinaryFileFileEntry(new FileEntry(fileSystem, binaryFileRelativeName));

        return new FakeFile(fp, binaryFileHash, content, binaryFileRelativeName, creationTimeUtc.Value, lastWriteTimeUtc.Value);
    }


    public static FakeFile WithDuplicate(this FakeFile fpwhc, UPath binaryFileRelativeName)
    {
        // TODO replicate attributes from BinaryFile if there is any
        return WithSourceFolderHavingFilePair(fpwhc.FilePair.FileSystem, binaryFileRelativeName, fpwhc.FilePair.Type, fpwhc.OriginalContent, creationTimeUtc: fpwhc.OriginalCreationDateTimeUtc, lastWriteTimeUtc: fpwhc.OriginalLastWriteTimeUtc);
    }


    private static byte[] GenerateRandomContent(int sizeInBytes, int? seed)
    {
        var random = seed is null ? new Random() : new Random(seed.Value);
        var data = new byte[sizeInBytes];
        random.NextBytes(data);
        return data;
    }

    private static Hash ComputeSha256String(string passphrase, byte[] data)
    {
        var hasher = new Sha256Hasher(passphrase);
        return hasher.GetHashAsync(data).Result;
    }
}