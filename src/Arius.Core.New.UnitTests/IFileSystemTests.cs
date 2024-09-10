using Arius.Core.Domain;
using Arius.Core.Domain.Storage;
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

    private readonly Hash hash;


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
        var version = new RepositoryVersion { Name = "2023-01-01" };
        var sdbFile = StateDatabaseFile.FromRepositoryVersion(root, version, false);

        sdbFile.Should().NotBeNull();
        sdbFile.Version.Should().Be(version);
        sdbFile.IsTemp.Should().BeFalse();
    }

    [Fact]
    public void StateDatabaseFile_FromRepositoryVersion_ForNonExistingFile_ShouldReturnFile()
    {
        var version = new RepositoryVersion { Name = "2023-01-01" };
        var sdbFile = StateDatabaseFile.FromRepositoryVersion(root, version, false);

        sdbFile.Should().NotBeNull();
        sdbFile.Version.Should().Be(version);
        sdbFile.IsTemp.Should().BeFalse();
    }

    [Fact(Skip = "NotImplemented")]
    public void StateDatabaseFile_FromFullName_ForExistingFile_Exception()
    {
    }

    [Fact(Skip = "NotImplemented")]
    public void StateDatabaseFile_FromFullName_ForExistingFile_ShouldReturnFile()
    {
        //var sdbFile = StateDatabaseFile.FromFullName(root, existingFileFullName);

        //sdbFile.Should().NotBeNull();
        //sdbFile.FullName.Should().Be(existingFileFullName);
        //sdbFile.Exists.Should().BeTrue();
    }

    [Fact(Skip = "NotImplemented")]
    public void StateDatabaseFile_FromFullName_ForNonExistingFile_ShouldReturnFile()
    {
        //var sdbFile = StateDatabaseFile.FromFullName(root, nonExistingFileFullName);

        //sdbFile.Should().NotBeNull();
        //sdbFile.FullName.Should().Be(nonExistingFileFullName);
        //sdbFile.Exists.Should().BeFalse();
    }

    [Fact]
    public void StateDatabaseFile_FromRepositoryVersion_WithTempVersion_ForExistingFile_ShouldReturnStateDatabaseFile()
    {
        var version = new RepositoryVersion { Name = "2023-01-01" };
        var sdbFile = StateDatabaseFile.FromRepositoryVersion(root, version, true);

        sdbFile.Should().NotBeNull();
        sdbFile.Version.Should().Be(version);
        sdbFile.IsTemp.Should().BeTrue();
    }

    [Fact]
    public void StateDatabaseFile_FromRepositoryVersion_WithTempVersion_ForNonExistingFile_ShouldReturnStateDatabaseFile()
    {
        var version = new RepositoryVersion { Name = "2023-01-01" };
        var sdbFile = StateDatabaseFile.FromRepositoryVersion(root, version, true);

        sdbFile.Should().NotBeNull();
        sdbFile.Version.Should().Be(version);
        sdbFile.IsTemp.Should().BeTrue();
    }


    // ======== Tests for PointerFile ======== //

    [Fact]
    public void PointerFile_FromFullName_ForExistingFile_ShouldReturnPointerFile()
    {
        var pf = PointerFile.FromFullName(root, existingPointerFileFullName);

        pf.Should().NotBeNull();
        pf.FullName.Should().Be(existingPointerFileFullName);
        pf.Exists.Should().BeTrue();
    }

    [Fact]
    public void PointerFile_FromFullName_ForNonExistingFile_ShouldReturnPointerFile()
    {
        var pf = PointerFile.FromFullName(root, nonExistingPointerFileFullName);

        pf.Should().NotBeNull();
        pf.FullName.Should().Be(nonExistingPointerFileFullName);
        pf.Exists.Should().BeFalse();
    }

    [Fact]
    public void PointerFile_FromRelativeName_ForExistingFile_ShouldReturnPointerFile()
    {
        var relativeName = Path.GetRelativePath(root.FullName, existingPointerFileFullName);
        var pf           = PointerFile.FromRelativeName(root, relativeName);

        pf.Should().NotBeNull();
        pf.FullName.Should().Be(existingPointerFileFullName);
        pf.Exists.Should().BeTrue();
    }

    [Fact]
    public void PointerFile_FromRelativeName_ForNonExistingFile_ShouldReturnPointerFile()
    {
        var relativeName = Path.GetRelativePath(root.FullName, nonExistingPointerFileFullName);
        var pf           = PointerFile.FromRelativeName(root, relativeName);

        pf.Should().NotBeNull();
        pf.FullName.Should().Be(nonExistingPointerFileFullName);
        pf.Exists.Should().BeFalse();
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

    [Fact]
    public void PointerFileWithHash_FromRelativeName_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
        var relativeName = Path.GetRelativePath(root.FullName, existingPointerFileFullName);
        var pfwh         = PointerFileWithHash.FromRelativeName(root, relativeName, hash);

        pfwh.Should().NotBeNull();
        pfwh.FullName.Should().Be(existingPointerFileFullName);
        pfwh.Exists.Should().BeTrue();
        pfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void PointerFileWithHash_FromRelativeName_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
        var relativeName = Path.GetRelativePath(root.FullName, nonExistingPointerFileFullName);
        var pfwh         = PointerFileWithHash.FromRelativeName(root, relativeName, hash);

        pfwh.Should().NotBeNull();
        pfwh.FullName.Should().Be(nonExistingPointerFileFullName);
        pfwh.Exists.Should().BeFalse();
        pfwh.Hash.Should().Be(hash);
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromBinaryFileWithHash_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
        //var bfwh = BinaryFileWithHash.FromFullName(root, existingFileFullName, hash);
        //var pfwh = PointerFileWithHash.FromBinaryFileWithHash(bfwh);

        //pfwh.Should().NotBeNull();
        //pfwh.FullName.Should().Be(existingFileFullName + PointerFile.Extension);
        //pfwh.Exists.Should().BeFalse();
        //pfwh.Hash.Should().Be(hash);
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromBinaryFileWithHash_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
        //var bfwh = BinaryFileWithHash.FromFullName(root, nonExistingFileFullName, hash);
        //var pfwh = PointerFileWithHash.FromBinaryFileWithHash(bfwh);

        //pfwh.Should().NotBeNull();
        //pfwh.FullName.Should().Be(nonExistingFileFullName + PointerFile.Extension);
        //pfwh.Exists.Should().BeFalse();
        //pfwh.Hash.Should().Be(hash);
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_FromExistingPointerFile_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
        //var pf = PointerFile.FromFullName(root, existingPointerFileFullName);
        //var pfwh = PointerFileWithHash.FromExistingPointerFile(pf);

        //pfwh.Should().NotBeNull();
        //pfwh.FullName.Should().Be(existingPointerFileFullName);
        //pfwh.Exists.Should().BeTrue();
        //pfwh.Hash.Value.Should().NotBeEmpty(); //Hash value read from existing file
    }

    [Fact]
    public void PointerFileWithHash_FromExistingPointerFile_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
        var pf  = PointerFile.FromFullName(root, nonExistingPointerFileFullName);
        var act = () => PointerFileWithHash.FromExistingPointerFile(pf);

        act.Should().Throw<ArgumentException>(); //Throws as file does not exist
    }

    [Fact]
    public void PointerFileWithHash_Create_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
        var bfwh = BinaryFileWithHash.FromFullName(root, existingFileFullName, hash);
        var pfwh = PointerFileWithHash.Create(bfwh);

        pfwh.Should().NotBeNull();
        pfwh.FullName.Should().Be(existingFileFullName + PointerFile.Extension);
        pfwh.Exists.Should().BeTrue();
        pfwh.Hash.Should().Be(hash);
    }

    [Fact(Skip = "NotImplemented")]
    public void PointerFileWithHash_Create_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
        //var bfwh = BinaryFileWithHash.FromFullName(root, nonExistingFileFullName, hash);
        //var pfwh = PointerFileWithHash.Create(bfwh);

        //pfwh.Should().NotBeNull();
        //pfwh.FullName.Should().Be(nonExistingFileFullName + PointerFile.Extension);
        //pfwh.Exists.Should().BeTrue();
        //pfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void PointerFileWithHash_Create_WithPointerFileEntry_ForExistingFile_ShouldReturnPointerFileWithHash()
    {
        //var pfe = new PointerFileEntry(existingRelativeName, hash, DateTime.UtcNow, DateTime.UtcNow);
        //var pfwh = PointerFileWithHash.Create(root, pfe);

        //pfwh.Should().NotBeNull();
        //pfwh.FullName.Should().Be(existingFileFullName + PointerFile.Extension);
        //pfwh.Exists.Should().BeTrue();
        //pfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void PointerFileWithHash_Create_WithPointerFileEntry_ForNonExistingFile_ShouldReturnPointerFileWithHash()
    {
        //var pfe = new PointerFileEntry(nonExistingRelativeName, hash, DateTime.UtcNow, DateTime.UtcNow);
        //var pfwh = PointerFileWithHash.Create(root, pfe);

        //pfwh.Should().NotBeNull();
        //pfwh.FullName.Should().Be(nonExistingFileFullName + PointerFile.Extension);
        //pfwh.Exists.Should().BeTrue();
        //pfwh.Hash.Should().Be(hash);
    }


    // ======== Tests for BinaryFile ======== //

    [Fact]
    public void BinaryFile_FromFullName_ShouldReturnBinaryFile_ForExistingFile()
    {
        var bf = BinaryFile.FromFullName(root, existingFileFullName);

        bf.Should().NotBeNull();
        bf.FullName.Should().Be(existingFileFullName);
        bf.Exists.Should().BeTrue();
    }

    [Fact]
    public void BinaryFile_FromFullName_ShouldReturnBinaryFile_ForNonExistingFile()
    {
        var bf = BinaryFile.FromFullName(root, nonExistingFileFullName);

        bf.Should().NotBeNull();
        bf.FullName.Should().Be(nonExistingFileFullName);
        bf.Exists.Should().BeFalse();
    }

    [Fact]
    public void BinaryFile_FromRelativeName_ShouldReturnBinaryFile_ForExistingFile()
    {
        var bf = BinaryFile.FromRelativeName(root, existingRelativeName);

        bf.Should().NotBeNull();
        bf.FullName.Should().Be(existingFileFullName);
        bf.Exists.Should().BeTrue();
    }

    [Fact]
    public void BinaryFile_FromRelativeName_ShouldReturnBinaryFile_ForNonExistingFile()
    {
        var bf = BinaryFile.FromRelativeName(root, nonExistingRelativeName);

        bf.Should().NotBeNull();
        bf.FullName.Should().Be(nonExistingFileFullName);
        bf.Exists.Should().BeFalse();
    }

    // ======== Tests for BinaryFileWithHash ======== //

    [Fact]
    public void BinaryFileWithHash_FromFullName_ShouldReturnBinaryFileWithHash_ForExistingFile()
    {
        var bfwh = BinaryFileWithHash.FromFullName(root, existingFileFullName, hash);

        bfwh.Should().NotBeNull();
        bfwh.FullName.Should().Be(existingFileFullName);
        bfwh.Exists.Should().BeTrue();
        bfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void BinaryFileWithHash_FromFullName_ShouldReturnBinaryFileWithHash_ForNonExistingFile()
    {
        var bfwh = BinaryFileWithHash.FromFullName(root, nonExistingFileFullName, hash);

        bfwh.Should().NotBeNull();
        bfwh.FullName.Should().Be(nonExistingFileFullName);
        bfwh.Exists.Should().BeFalse();
        bfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void BinaryFileWithHash_FromRelativeName_ShouldReturnBinaryFileWithHash_ForExistingFile()
    {
        var bfwh = BinaryFileWithHash.FromRelativeName(root, existingRelativeName, hash);

        bfwh.Should().NotBeNull();
        bfwh.FullName.Should().Be(existingFileFullName);
        bfwh.Exists.Should().BeTrue();
        bfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void BinaryFileWithHash_FromRelativeName_ShouldReturnBinaryFileWithHash_ForNonExistingFile()
    {
        var bfwh = BinaryFileWithHash.FromRelativeName(root, nonExistingRelativeName, hash);

        bfwh.Should().NotBeNull();
        bfwh.FullName.Should().Be(nonExistingFileFullName);
        bfwh.Exists.Should().BeFalse();
        bfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void BinaryFileWithHash_FromBinaryFile_ShouldReturnBinaryFileWithHash_ForExistingFile()
    {
        var bf   = BinaryFile.FromFullName(root, existingFileFullName);
        var bfwh = BinaryFileWithHash.FromBinaryFile(bf, hash);

        bfwh.Should().NotBeNull();
        bfwh.FullName.Should().Be(existingFileFullName);
        bfwh.Exists.Should().BeTrue();
        bfwh.Hash.Should().Be(hash);
    }

    [Fact]
    public void BinaryFileWithHash_FromBinaryFile_ShouldReturnBinaryFileWithHash_ForNonExistingFile()
    {
        var bf   = BinaryFile.FromFullName(root, nonExistingFileFullName);
        var bfwh = BinaryFileWithHash.FromBinaryFile(bf, hash);

        bfwh.Should().NotBeNull();
        bfwh.FullName.Should().Be(nonExistingFileFullName);
        bfwh.Exists.Should().BeFalse();
        bfwh.Hash.Should().Be(hash);
    }
}