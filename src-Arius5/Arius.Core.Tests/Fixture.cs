using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using System.Text;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests;

public record FilePairWithHash(FilePair FilePair, Hash Hash);

public class Fixture : IDisposable
{
    private const string passphrase = "woutervanranst";

    private readonly DirectoryInfo testRunSourceFolder;
    public           IFileSystem   FileSystem { get; }

    public Fixture()
    {
        testRunSourceFolder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "Arius.Core.Tests", DateTime.Now.ToString("yyyyMMddHHmmss")));
        testRunSourceFolder.Create();

        var pfs = new PhysicalFileSystem();
        var sfs = new SubFileSystem(pfs, pfs.ConvertPathFromInternal(testRunSourceFolder.FullName));
        FileSystem = new FilePairFileSystem(sfs);
    }

    public FilePairWithHash GivenSourceFolderHavingFilePair(UPath binaryFileRelativeName, FilePairType type, int sizeInBytes, int? seed = default, FileAttributes attributes = FileAttributes.Normal)
    {
        if (!binaryFileRelativeName.IsAbsolute)
            throw new ArgumentException("Should start with /");

        var fe = new FileEntry(FileSystem, binaryFileRelativeName);
        fe.Directory.Create();

        var createBinary  = type is FilePairType.BinaryFileWithPointerFile or FilePairType.BinaryFileOnly;
        var createPointer = type is FilePairType.BinaryFileWithPointerFile or FilePairType.PointerFileOnly;

        Hash? binaryFileHash  = null;
        Hash? pointerFileHash = null;

        // 1. Create the Binary File if needed
        if (createBinary)
        {
            var       content      = GenerateRandomContent(sizeInBytes, seed);
            using var binaryStream = FileSystem.OpenFile(binaryFileRelativeName, FileMode.Create, FileAccess.Write);
            binaryStream.Write(content);
            FileSystem.SetAttributes(binaryFileRelativeName, attributes);
            binaryFileHash = ComputeSha256String(passphrase, content);
        }

        // 2. Create the Pointer File if needed
        if (createPointer)
        {
            var       pointerPath   = binaryFileRelativeName.GetPointerFilePath();
            using var pointerStream = FileSystem.OpenFile(pointerPath, FileMode.Create, FileAccess.Write);
            pointerFileHash = binaryFileHash ?? GenerateRandomSha256(seed);

            var pointerData = Encoding.UTF8.GetBytes($"{{\"BinaryHash\":\"{pointerFileHash}\"}}");
            pointerStream.Write(pointerData);
        }

        // 3. Build the FilePair
        var fp = FilePair.FromBinaryFileFileEntry(new FileEntry(FileSystem, binaryFileRelativeName));
        
        var finalHash = pointerFileHash ?? binaryFileHash ?? throw new InvalidOperationException();

        return new FilePairWithHash(fp, finalHash);


        static byte[] GenerateRandomContent(int sizeInBytes, int? localSeed)
        {
            var random = localSeed is null ? new Random() : new Random(localSeed.Value);
            var data   = new byte[sizeInBytes];
            random.NextBytes(data);
            return data;
        }

        static Hash ComputeSha256String(string passphrase, byte[] data)
        {
            var hasher = new Sha256Hasher(passphrase);
            return hasher.GetHashAsync(data).Result;
        }

        static Hash GenerateRandomSha256(int? localSeed)
        {
            return GenerateRandomContent(32, localSeed);
        }
    }

    public void Dispose()
    {
        FileSystem.Dispose();
    }
}