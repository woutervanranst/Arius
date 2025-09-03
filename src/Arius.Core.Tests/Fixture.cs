using Arius.Core.Shared.Extensions;
using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System.Text;
using Zio;
using Zio.FileSystems;

namespace Arius.Core.Tests;

internal record FilePairWithHash(FilePair FilePair, Hash Hash);

public abstract class FixtureBase : IDisposable
{
    public TestRemoteRepositoryOptions  RepositoryOptions  { get; }
    public IOptions<AriusConfiguration> AriusConfiguration { get; }

    protected FixtureBase()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<FixtureBase>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        RepositoryOptions = configuration.GetSection("RepositoryOptions").Get<TestRemoteRepositoryOptions>();

        var ariusConfig = new AriusConfiguration();
        configuration.Bind(ariusConfig);
        AriusConfiguration = Options.Create(ariusConfig);
    }

    public abstract void Dispose();
}

public record TestRemoteRepositoryOptions
{
    public string AccountName { get; init; }

    public string AccountKey { get; init; }

    //[JsonIgnore]
    public string ContainerName { get; set; }

    public string Passphrase { get; init; }
}

public class Fixture : FixtureBase
{
    public IFileSystem   FileSystem          { get; }
    public DirectoryInfo TestRunSourceFolder { get; }

    public Fixture()
    {
        TestRunSourceFolder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "Arius.Core.Tests", $"{DateTime.Now:yyyyMMddTHHmmss}_{Guid.CreateVersion7()}"));
        TestRunSourceFolder.Create();

        var pfs = new PhysicalFileSystem();
        var sfs = new SubFileSystem(pfs, pfs.ConvertPathFromInternal(TestRunSourceFolder.FullName));
        FileSystem = new FilePairFileSystem(sfs);
    }

    internal FilePairWithHash GivenSourceFolderHavingFilePair(UPath binaryFileRelativeName, FilePairType type, int sizeInBytes, int? seed = default, FileAttributes attributes = FileAttributes.Normal)
    {
        if (!binaryFileRelativeName.IsAbsolute)
            throw new ArgumentException("Should start with /");

        if ((attributes & FileAttributes.Hidden) != 0 && !binaryFileRelativeName.GetName().StartsWith("."))
            throw new ArgumentException("Hidden files should start with a dot (.) in their name by Linux convention");

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
            binaryFileHash = ComputeSha256String(RepositoryOptions.Passphrase , content);
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

    public override void Dispose()
    {
        FileSystem.Dispose();
    }
}
