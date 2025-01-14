namespace FileSystem.Local.Tests;

public class PathSegmentTests
{
    [Theory]
    [InlineData("validPath", "validPath", "validPath")]
    [InlineData("validPath\\", "validPath/", "validPath\\")]
    [InlineData("some\\path", "some/path", "some\\path")]
    [InlineData("some\\path\\", "some/path/", "some\\path\\")]
    [InlineData("C:\\some\\path\\", "C:/some/path/", "C:\\some\\path\\")]
    [InlineData("C:\\some\\path", "C:/some/path", "C:\\some\\path")]
    [InlineData("C:\\some\\path\\file.txt", "C:/some/path/file.txt", "C:\\some\\path\\file.txt")]
    [InlineData("/", "/", "/")]
    [InlineData("\\", "/", "\\")]
    [InlineData("//", "/", "/")]
    [InlineData("\\\\", "/", "\\")]
    [InlineData("folder/\\subfolder", "folder/subfolder", "folder\\subfolder")]
    [InlineData("folder\\sub", "folder/sub", "folder\\sub")]
    [InlineData("folder\\sub\\", "folder/sub/", "folder\\sub\\")]
    [InlineData("folder\\subfolder", "folder/subfolder", "folder\\subfolder")]
    [InlineData("folder/subfolder\\file.txt", "folder/subfolder/file.txt", "folder\\subfolder\\file.txt")]
    [InlineData("/", "/", "/")]
    [InlineData("\\", "/", "\\")]
    [InlineData("C:\\", "C:/", "C:\\")]
    public void PathSegment_ValidInput_CreatesInstance(string validValue, string plaformNeutralExpectedValue, string platformSpecificExpectedValue)
    {
        var segment = (PathSegment)validValue;

        Assert.Equal(plaformNeutralExpectedValue, segment.ToPlatformNeutral());
        Assert.Equal(platformSpecificExpectedValue, segment.ToPlatformSpecific());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PathSegment_InvalidInput_ThrowsException(string invalidValue)
    {
        var exception = Assert.Throws<ArgumentException>(() => (PathSegment)invalidValue);

        Assert.Equal("Path segment cannot be null or empty. (Parameter 'value')", exception.Message);
    }

    [Fact]
    public void PathSegment_Equality_Tests()
    {
        var segment1 = (PathSegment)"some/path";
        var segment2 = (PathSegment)"some\\path";

        Assert.Equal(segment1, segment2);
        Assert.Equal(segment1.GetHashCode(), segment2.GetHashCode());
    }

    [Theory]
    [InlineData("validPath", "bla", "validPath/bla")]
    [InlineData("valid\\Path", "bla/x", "valid/Path/bla/x")]
    [InlineData("folder", "subfolder/file.txt", "folder/subfolder/file.txt")]
    [InlineData("C:\\folder", "subfolder\\file.txt", "C:/folder/subfolder/file.txt")]
    public void PathSegment_OperatorPlus_CombinesSegments(PathSegment segment1, PathSegment segment2, string expected)
    {
        var combined = segment1 + segment2;

        Assert.Equal(expected, combined.ToPlatformNeutral());
    }

    [Fact]
    public void PathSegment_OperatorPlus_ThrowsOnNullSegments()
    {
        PathSegment? segment1 = null;
        var segment2 = (PathSegment)"file.txt";

        Assert.Throws<ArgumentNullException>(() => segment1 + segment2);
        Assert.Throws<ArgumentNullException>(() => segment2 + segment1);
    }

    [Theory]
    [InlineData("some/path")]
    [InlineData("some\\path")]
    public void PathSegment_ToString_ReturnsExpectedValue(string input)
    {
        var segment = (PathSegment)input;

        Assert.Equal(input.ToPlatformNeutralPath(), segment.ToString());
    }
}

public class RootedPathSegmentTests
{
    [Theory]
    [InlineData("/root", "/root", "/root")]
    [InlineData("/root/", "/root/", "\\root\\")]
    [InlineData("C:\\root", "C:/root", "C:\\root")]
    [InlineData("D:/root", "D:/root", "D:/root")]
    public void RootedPathSegment_ValidRoot_CreatesInstance(string validValue, string platformNeutralExpectedValue, string platformSpecificExpectedValue)
    {
        var segment = (RootedPathSegment)validValue;

        Assert.Equal(platformNeutralExpectedValue, segment.ToPlatformNeutral());
        Assert.Equal(platformSpecificExpectedValue, segment.ToPlatformSpecific());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("some/path")]
    [InlineData("some\\path")]
    [InlineData("C:root")]
    public void RootedPathSegment_InvalidRoot_ThrowsException(string invalidValue)
    {
        Assert.Throws<ArgumentException>(() => (RootedPathSegment)invalidValue);
    }

    [Theory]
    [InlineData("/root", "/root", "/root")]
    [InlineData("C:\\root", "C:/root", "C:\\root")]
    public void RootedPathSegment_ToPlatformNeutralAndSpecific_CorrectConversion(string input, string expectedNeutral, string expectedSpecific)
    {
        var segment = (RootedPathSegment)input;

        Assert.Equal(expectedNeutral, segment.ToPlatformNeutral());
        Assert.Equal(expectedSpecific, segment.ToPlatformSpecific());
    }

    [Fact]
    public void RootedPathSegment_OperatorPlus_CombinesWithRelativeSegment()
    {
        var rootSegment = (RootedPathSegment)"C:/root";
        var relativeSegment = (RelativePathSegment)"subfolder";

        var combined = rootSegment + relativeSegment;

        Assert.Equal("C:/root/subfolder", combined.ToPlatformNeutral());
        Assert.Equal("C:\\root\\subfolder", combined.ToPlatformSpecific());
    }

    [Fact]
    public void RootedPathSegment_OperatorPlus_ThrowsOnNullSegment()
    {
        RootedPathSegment? nullSegment = null;
        var relativeSegment = (RelativePathSegment)"subfolder";

        Assert.Throws<ArgumentNullException>(() => nullSegment + relativeSegment);
        Assert.Throws<ArgumentNullException>(() => relativeSegment + nullSegment);
    }

    [Theory]
    [InlineData("/root", "/root", "/root")]
    [InlineData("C:\\root", "C:/root", "C:\\root")]
    public void RootedPathSegment_OperatorPlus_HandlesMixedSeparators(string rootPath, string expectedNeutral, string expectedSpecific)
    {
        var rootSegment = (RootedPathSegment)rootPath;
        var relativeSegment = (RelativePathSegment)"subfolder";

        var combined = rootSegment + relativeSegment;

        Assert.Equal(expectedNeutral + "/subfolder", combined.ToPlatformNeutral());
        Assert.Equal(expectedSpecific + "\\subfolder", combined.ToPlatformSpecific());
    }
}

//public class RootPathSegmentTests
//{
//    [Theory]
//    [InlineData(@"C:\root")]
//    [InlineData(@"/root")]
//    public void RootPathSegment_ValidRoot_CreatesInstance(string root)
//    {
//        var segment = new RootPathSegment(root);

//        Assert.Equal(root, segment);
//    }

//    [Theory]
//    [InlineData("relativePath")]
//    [InlineData(" ")]
//    public void RootPathSegment_InvalidRoot_ThrowsException(string invalidRoot)
//    {
//        var exception = Assert.Throws<ArgumentException>(() => new RootPathSegment(invalidRoot));

//        Assert.Equal("Root must be a root path. (Parameter 'value')", exception.Message);
//    }

//    [Fact]
//    public void RootPathSegment_OperatorPlus_ReturnsFullNamePathSegment()
//    {
//        var root = new RootPathSegment(@"C:\root");
//        var relative = new RelativePathSegment("subfolder");

//        var fullName = root + relative;

//        Assert.Equal(@"C:\root\subfolder", fullName);
//    }
//}

//public class RelativePathSegmentTests
//{
//    [Theory]
//    [InlineData("relativePath")]
//    [InlineData("folder/file.txt")]
//    public void RelativePathSegment_ValidRelativePath_CreatesInstance(string relativePath)
//    {
//        var segment = new RelativePathSegment(relativePath);

//        Assert.Equal(relativePath, segment);
//    }

//    [Theory]
//    [InlineData(@"C:\absolutePath")]
//    [InlineData(@"/absolutePath")]
//    public void RelativePathSegment_InvalidRelativePath_ThrowsException(string invalidPath)
//    {
//        var exception = Assert.Throws<ArgumentException>(() => new RelativePathSegment(invalidPath));

//        Assert.Equal("Relative name cannot be a root value. (Parameter 'value')", exception.Message);
//    }
//}

//public class NamePathSegmentTests
//{
//    [Theory]
//    [InlineData("file")]
//    [InlineData("fileName.ext")]
//    public void NamePathSegment_ValidName_CreatesInstance(string name)
//    {
//        var segment = new NamePathSegment(name);

//        Assert.Equal(name, segment);
//    }

//    [Theory]
//    [InlineData("file/name")]
//    [InlineData("file\\name")]
//    public void NamePathSegment_InvalidName_ThrowsException(string invalidName)
//    {
//        var exception = Assert.Throws<ArgumentException>(() => new NamePathSegment(invalidName));

//        Assert.Equal("Name cannot contain path separators. (Parameter 'name')", exception.Message);
//    }
//}

//public class FullNamePathSegmentTests
//{
//    [Theory]
//    [InlineData(@"C:\folder\file.txt")]
//    public void FullNamePathSegment_ValidFullName_CreatesInstance(string fullName)
//    {
//        var segment = (FullNamePathSegment)fullName;

//        Assert.Equal(fullName, segment);
//    }

//    [Fact]
//    public void FullNamePathSegment_CreatesFromRootAndRelative()
//    {
//        var root = new RootPathSegment(@"C:\root");
//        var relative = new RelativePathSegment("subfolder/file.txt");

//        var fullName = new FullNamePathSegment(root, relative);

//        Assert.Equal(@"C:\root\subfolder\file.txt", fullName);
//    }
//}