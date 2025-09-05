using Arius.Core.Shared.FileSystem;
using Arius.Core.Shared.Hashing;
using Arius.Core.Tests.Helpers.Fixtures;
using System.Globalization;
using System.Text;
using Zio;

namespace Arius.Core.Tests.Helpers.Fakes;

internal record FakeFile(FilePair FilePair, Hash OriginalHash, byte[] OriginalContent, UPath OriginalPath, DateTime OriginalCreationDateTimeUtc, DateTime OriginalLastWriteTimeUtc);

internal class FakeFileBuilder
{
    private readonly IFileSystem fileSystem;
    private readonly string passphrase;
    private readonly List<(FilePairType type, UPath path)> fileSpecs = new();
    private byte[]? content;
    private int? contentSizeInBytes;
    private int? contentSeed;
    private FileAttributes attributes = FileAttributes.Normal;
    private DateTime? creationTimeUtc;
    private DateTime? lastWriteTimeUtc;

    public FakeFileBuilder(FixtureBase fixture)
    {
        fileSystem = fixture.FileSystem;
        passphrase = FixtureBase.PASSPHRASE;
    }

    public FakeFileBuilder WithActualFile(FilePairType type, UPath path)
    {
        fileSpecs.Add((type, path));
        return this;
    }

    public FakeFileBuilder WithNonExistingFile(UPath path)
    {
        fileSpecs.Add((FilePairType.None, path));
        return this;
    }

    public FakeFileBuilder WithActualFiles(FilePairType type, params UPath[] paths)
    {
        foreach (var path in paths)
        {
            fileSpecs.Add((type, path));
        }
        return this;
    }

    public FakeFileBuilder WithNonExistingFiles(params UPath[] paths)
    {
        foreach (var path in paths)
        {
            fileSpecs.Add((FilePairType.None, path));
        }
        return this;
    }



    public FakeFileBuilder WithRandomContent(int sizeInBytes, int? seed = null)
    {
        contentSizeInBytes = sizeInBytes;
        contentSeed = seed;
        content = null; // Clear any previously set content
        return this;
    }

    public FakeFileBuilder WithContent(byte[] content)
    {
        this.content = content;
        contentSizeInBytes = null;
        contentSeed = null;
        return this;
    }

    public FakeFileBuilder WithAttributes(FileAttributes attributes)
    {
        this.attributes = attributes;
        return this;
    }

    public FakeFileBuilder WithCreationTimeUtc(string dateTime)
    {
        creationTimeUtc = DateTime.ParseExact(dateTime, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return this;
    }

    public FakeFileBuilder WithCreationTimeUtc(DateTime dateTime)
    {
        creationTimeUtc = dateTime;
        return this;
    }

    public FakeFileBuilder WithLastWriteTimeUtc(string dateTime)
    {
        lastWriteTimeUtc = DateTime.ParseExact(dateTime, "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return this;
    }

    public FakeFileBuilder WithLastWriteTimeUtc(DateTime dateTime)
    {
        lastWriteTimeUtc = dateTime;
        return this;
    }

    public FakeFileBuilder WithDuplicate(FakeFile existingFakeFile, UPath path)
    {
        content = existingFakeFile.OriginalContent;
        creationTimeUtc = existingFakeFile.OriginalCreationDateTimeUtc;
        lastWriteTimeUtc = existingFakeFile.OriginalLastWriteTimeUtc;
        fileSpecs.Add((existingFakeFile.FilePair.Type, path));
        return this;
    }

    public FakeFile Build()
    {
        if (fileSpecs.Count > 1)
            throw new InvalidOperationException($"Cannot use Build() with {fileSpecs.Count} files configured. Use BuildMany() instead.");

        var actualContent = GetOrGenerateContent();

        if (fileSpecs.Count == 0)
        {
            // Create memory-only FakeFile with FilePairType.None
            var memoryPath = UPath.Root / "memory-file";
            var hash = ComputeSha256Hash(passphrase, actualContent);
            var filePair = FilePair.FromBinaryFilePath(fileSystem, memoryPath);
            return new FakeFile(filePair, hash, actualContent, memoryPath, GetCreationTime(), GetLastWriteTime());
        }

        var (type, path) = fileSpecs[0];
        return CreateFakeFile(type, path, actualContent);
    }

    public List<FakeFile> BuildMany()
    {
        if (fileSpecs.Count == 0)
            throw new InvalidOperationException("No files configured. Use WithActualFile() or WithActualFiles() before calling BuildMany().");

        var actualContent = GetOrGenerateContent();
        var fakeFiles = new List<FakeFile>();

        foreach (var (type, path) in fileSpecs)
        {
            fakeFiles.Add(CreateFakeFile(type, path, actualContent));
        }

        return fakeFiles;
    }

    private byte[] GetOrGenerateContent()
    {
        if (content is not null)
            return content;

        if (contentSizeInBytes.HasValue)
            return GenerateRandomContent(contentSizeInBytes.Value, contentSeed);

        // Default to empty content
        return Array.Empty<byte>();
    }

    private FakeFile CreateFakeFile(FilePairType type, UPath binaryFileRelativeName, byte[] content)
    {
        if (!binaryFileRelativeName.IsAbsolute)
            throw new ArgumentException("Should start with /");

        if ((attributes & FileAttributes.Hidden) != 0 && !binaryFileRelativeName.GetName().StartsWith("."))
            throw new ArgumentException("Hidden files should start with a dot (.) in their name by Linux convention");

        var actualCreationTime = GetCreationTime();
        var actualLastWriteTime = GetLastWriteTime();

        var binaryFileHash = ComputeSha256Hash(passphrase, content);

        // For FilePairType.None, don't create any actual files, just return a FakeFile with the specified path
        if (type == FilePairType.None)
        {
            var filePair = FilePair.FromBinaryFilePath(fileSystem, binaryFileRelativeName);
            return new FakeFile(filePair, binaryFileHash, content, binaryFileRelativeName, actualCreationTime, actualLastWriteTime);
        }

        var fe = new FileEntry(fileSystem, binaryFileRelativeName);
        fe.Directory.Create();

        var createBinary = type is FilePairType.BinaryFileWithPointerFile or FilePairType.BinaryFileOnly;
        var createPointer = type is FilePairType.BinaryFileWithPointerFile or FilePairType.PointerFileOnly;

        // 1. Create the Binary File if needed
        if (createBinary)
        {
            using (var binaryStream = fileSystem.OpenFile(binaryFileRelativeName, FileMode.Create, FileAccess.Write))
            {
                binaryStream.Write(content);
            }

            fileSystem.SetAttributes(binaryFileRelativeName, attributes);
            fileSystem.SetCreationTimeUtc(binaryFileRelativeName, actualCreationTime);
            fileSystem.SetLastWriteTimeUtc(binaryFileRelativeName, actualLastWriteTime);
        }

        // 2. Create the Pointer File if needed
        if (createPointer)
        {
            var pointerPath = binaryFileRelativeName.GetPointerFilePath();
            using var pointerStream = fileSystem.OpenFile(pointerPath, FileMode.Create, FileAccess.Write);

            var pointerData = Encoding.UTF8.GetBytes($"{{\"BinaryHash\":\"{binaryFileHash}\"}}");
            pointerStream.Write(pointerData);

            fileSystem.SetCreationTimeUtc(pointerPath, actualCreationTime);
            fileSystem.SetLastWriteTimeUtc(pointerPath, actualLastWriteTime);
        }

        // 3. Build the FilePair
        var fp = FilePair.FromBinaryFileFileEntry(new FileEntry(fileSystem, binaryFileRelativeName));

        return new FakeFile(fp, binaryFileHash, content, binaryFileRelativeName, actualCreationTime, actualLastWriteTime);
    }

    private DateTime GetCreationTime()
    {
        return creationTimeUtc ?? DateTime.FromFileTimeUtc(0); // ensure minimum valid Win32 file time (1601-01-01) instead of DateTime.MinValue
    }

    private DateTime GetLastWriteTime()
    {
        return lastWriteTimeUtc ?? DateTime.FromFileTimeUtc(0);
    }

    private static byte[] GenerateRandomContent(int sizeInBytes, int? seed)
    {
        var random = seed is null ? new Random() : new Random(seed.Value);
        var data = new byte[sizeInBytes];
        random.NextBytes(data);
        return data;
    }

    private static Hash ComputeSha256Hash(string passphrase, byte[] data)
    {
        var hasher = new Sha256Hasher(passphrase);
        return hasher.GetHashAsync(data).Result;
    }
}