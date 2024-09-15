using Arius.Core.Domain.Storage.FileSystem;
using Arius.Core.Infrastructure.Storage.LocalFileSystem;
using FluentAssertions;

namespace Arius.Core.New.UnitTests;

public class FileTests
{
    [Fact]
    public void Equals_TwoBinaryFilesWithSameFileInfo_ShouldBeEqual()
    {
        // do not remove the cast
        var bf1 = (IBinaryFile)BinaryFile.FromFullName(null, @"C:\AriusTest\Source\Marketing Campaign - Technical Assessments.docx");
        var bf2 = (IBinaryFile)BinaryFile.FromFullName(null, @"C:\AriusTest\Source\Marketing Campaign - Technical Assessments.docx");

        //(bf1 == bf2).Should().BeTrue(); // this is FALSE: see https://stackoverflow.com/questions/73962920/equality-of-interface-types-implemented-by-records
        bf1.Equals(bf2).Should().BeTrue();
        bf1.GetHashCode().Should().Be(bf2.GetHashCode());
    }

    [Fact]
    public void Equals_TwoFilePairsWithSameFileInfo_ShouldBeEqual()
    {
        // do not remove the cast
        var bf1 = (IBinaryFile)BinaryFile.FromFullName(new DirectoryInfo(@"C:\AriusTest\"), @"C:\AriusTest\Source\Marketing Campaign - Technical Assessments.docx");
        var bf2 = (IBinaryFile)BinaryFile.FromFullName(new DirectoryInfo(@"C:\AriusTest\"), @"C:\AriusTest\Source\Marketing Campaign - Technical Assessments.docx");

        var p1 = new FilePair(null, bf1);
        var p2 = new FilePair(null, bf2);

        p1.Equals(p2).Should().BeTrue();
        p1.GetHashCode().Should().Be(p2.GetHashCode());
    }

    [Fact]
    public void Equals_TwoFilePairsWithDifferentFileInfo_ShouldNotBeEqual()
    {
        // do not remove the cast
        var bf1 = (IBinaryFile)BinaryFile.FromFullName(new DirectoryInfo(@"C:\AriusTest\"), @"C:\AriusTest\Source\Marketing Campaign - Technical Assessments.docx");
        var bf2 = (IBinaryFile)BinaryFile.FromFullName(new DirectoryInfo(@"C:\AriusTest\"), @"C:\AriusTest\Source\Marketing Campaign - Technical Assessments.docx");

        var dict = new Dictionary<IFile, bool>();

        dict.Add(bf1, true);
        Assert.Throws<ArgumentException>(() => dict.Add(bf2, true));
    }
}