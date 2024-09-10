using Arius.Core.Domain;
using Arius.Core.Domain.Storage.FileSystem;
using FluentAssertions;
using File = Arius.Core.Domain.Storage.FileSystem.File;

namespace Arius.Core.New.UnitTests;

public class IFileSystemTests
{
    private readonly DirectoryInfo root;

    private readonly string existingFileFullName;
    private readonly string nonExistingFileFullName;

    private readonly string existingRelativeName;
    private readonly string nonExistingRelativeName;

    private readonly string existingPointerFileFullName;
    private readonly string nonExistingPointerFileFullName;

    //private readonly string existingRelativeName;
    //private readonly string nonExistingRelativeName;

    private readonly Hash   hash;


    public IFileSystemTests()
    {
        root = new DirectoryInfo(Path.GetTempPath());

        existingFileFullName = Path.Combine(root.FullName, Path.GetTempFileName());
        System.IO.File.WriteAllText(existingFileFullName, "Test content");
        System.IO.File.Exists(existingFileFullName).Should().BeTrue();

        nonExistingFileFullName = Path.Combine(root.FullName, Path.GetTempFileName());
        System.IO.File.Delete(nonExistingFileFullName);
        System.IO.File.Exists(nonExistingFileFullName).Should().BeFalse();

        existingRelativeName = Path.GetRelativePath(root.FullName, existingFileFullName);

        nonExistingRelativeName = Path.GetRelativePath(root.FullName, nonExistingFileFullName);

        existingPointerFileFullName = Path.Combine(root.FullName, Path.GetTempFileName()) + PointerFile.Extension;
        System.IO.File.WriteAllText(existingPointerFileFullName, "Test content");
        System.IO.File.Exists(existingPointerFileFullName).Should().BeTrue();

        nonExistingPointerFileFullName = Path.Combine(root.FullName, Path.GetTempFileName()) + PointerFile.Extension;
        System.IO.File.Delete(nonExistingPointerFileFullName);
        System.IO.File.Exists(nonExistingPointerFileFullName).Should().BeFalse();

        hash = new Hash("abc".StringToBytes());
    }

    // ======== Tests for File ======== //

    [Fact]
    public void File_FromFullName_WhenNonFullyQualifiedPath_Exception()
    { 
        var act = () => File.FromFullName("abc.txt");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void File_FromFullName_ForExistingFile()
    {
        var f = File.FromFullName(existingFileFullName);

        f.Should().NotBeNull();
        f.FullName.Should().Be(existingFileFullName);
        f.Exists.Should().BeTrue();
    }

    [Fact]
    public void File_FromFullName_ForNonExistingFile_ShouldReturnFile()
    {
        var f = File.FromFullName(nonExistingFileFullName);

        f.Should().NotBeNull();
        f.FullName.Should().Be(nonExistingFileFullName);
        f.Exists.Should().BeFalse();
    }

    [Fact]
    public void File_FromRelativeName_ForExistingFile_ShouldReturnFile()
    {
        var f = File.FromRelativeName(root, existingRelativeName);

        f.Should().NotBeNull();
        f.FullName.Should().Be(existingFileFullName);
        f.Exists.Should().BeTrue();
    }

    [Fact]
    public void File_FromRelativeName_ForNonExistingFile_ShouldReturnFile()
    {
        var f = File.FromRelativeName(root, nonExistingRelativeName);

        f.Should().NotBeNull();
        f.FullName.Should().Be(nonExistingFileFullName);
        f.Exists.Should().BeFalse();
    }

    // ======== Tests for StateDatabaseFile ======== //

    [Fact]
    public void StateDatabaseFile_FromRepositoryVersion_ForExistingFile_ShouldReturnFile()
    {
    }

    [Fact]
    public void StateDatabaseFile_FromRepositoryVersion_ForNonExistingFile_ShouldReturnFile()
    {
    }

    [Fact]
    public void StateDatabaseFile_FromFullName_ForExistingFile_ShouldReturnFile()
    {
    }

    [Fact]
    public void StateDatabaseFile_FromFullName_ForNonExistingFile_ShouldReturnFile()
    {
    }

    [Fact]
    public void StateDatabaseFile_FromRepositoryVersion_WithTempVersion_ForExistingFile_ShouldReturnStateDatabaseFile()
    {
    }

    [Fact]
    public void StateDatabaseFile_FromRepositoryVersion_WithTempVersion_ForNonExistingFile_ShouldReturnStateDatabaseFile()
    {
    }


    // ======== Tests for PointerFile ======== //

    [Fact]
    public void PointerFile_FromFullName_ForExistingFile_ShouldReturnPointerFile()
    {
    }

    [Fact]
    public void PointerFile_FromFullName_ForNonExistingFile_ShouldReturnPointerFile()
    {
    }

    [Fact]
    public void PointerFile_FromRelativeName_ForExistingFile_ShouldReturnPointerFile()
    {
    }

    [Fact]
    public void PointerFile_FromRelativeName_ForNonExistingFile_ShouldReturnPointerFile()
    {
    }


    // ======== Tests for PointerFileWithHash ======== //

    [Fact]
    public void PointerFileWithHash_FromFullName_ForExistingFile_Exception()
    {
        var act = () => PointerFileWithHash.FromFullName(root, existingFileFullName, hash);

        act.Should().Throw<ArgumentException>(); // throws exception as this file doesnt end in .pointer.arius

    }

    [Fact]
    public void PointerFileWithHash_FromFullName_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
        var f = PointerFileWithHash.FromFullName(root, existingPointerFileFullName, hash);

        f.Should().NotBeNull();
        f.FullName.Should().Be(existingPointerFileFullName);
        f.Exists.Should().BeTrue();
        f.Hash.Should().Be(hash);
    }

    [Fact]
    public void PointerFileWithHash_FromFullName_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
        var f = PointerFileWithHash.FromFullName(root, nonExistingPointerFileFullName, hash);

        f.Should().NotBeNull();
        f.FullName.Should().Be(nonExistingPointerFileFullName);
        f.Exists.Should().BeFalse();
        f.Hash.Should().Be(hash);
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromRelativeName_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromRelativeName_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromBinaryFileWithHash_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromBinaryFileWithHash_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromExistingPointerFile_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromExistingPointerFile_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_Create_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_Create_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_Create_WithPointerFileEntry_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_Create_WithPointerFileEntry_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
    }


    // ======== Tests for BinaryFile ======== //

    [Fact(Skip = "NotImplemented")]
    public void BinaryFile_FromFullName_ShouldReturnBinaryFile_ForExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFile_FromFullName_ShouldReturnBinaryFile_ForNonExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFile_FromRelativeName_ShouldReturnBinaryFile_ForExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFile_FromRelativeName_ShouldReturnBinaryFile_ForNonExistingFile()
    {
    }

    // ======== Tests for BinaryFileWithHash ======== //

    [Fact(Skip = "NotImplemented")]
    public void BinaryFileWithHash_FromFullName_ShouldReturnBinaryFileWithHash_ForExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFileWithHash_FromFullName_ShouldReturnBinaryFileWithHash_ForNonExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFileWithHash_FromRelativeName_ShouldReturnBinaryFileWithHash_ForExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFileWithHash_FromRelativeName_ShouldReturnBinaryFileWithHash_ForNonExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFileWithHash_FromBinaryFile_ShouldReturnBinaryFileWithHash_ForExistingFile()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void BinaryFileWithHash_FromBinaryFile_ShouldReturnBinaryFileWithHash_ForNonExistingFile()
    {
    }
}