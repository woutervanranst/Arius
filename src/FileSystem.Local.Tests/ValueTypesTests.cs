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
    [InlineData("/", "/", "\\")]
    [InlineData("\\", "/", "\\")]
    [InlineData("//", "/", "\\")]
    [InlineData("\\\\", "/", "\\")]
    [InlineData("folder/\\subfolder", "folder/subfolder", "folder\\subfolder")]
    [InlineData("folder\\sub", "folder/sub", "folder\\sub")]
    [InlineData("folder\\sub\\", "folder/sub/", "folder\\sub\\")]
    [InlineData("folder\\subfolder", "folder/subfolder", "folder\\subfolder")]
    [InlineData("folder/subfolder\\file.txt", "folder/subfolder/file.txt", "folder\\subfolder\\file.txt")]
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

        Assert.NotEqual(segment1, segment2);
        Assert.False(segment1.Equals(segment2));
        Assert.NotEqual(segment1.GetHashCode(), segment2.GetHashCode());
        
        Assert.False(segment1.Equals(segment2, PathSegmentComparison.LiteralValue));
        Assert.True(segment1.Equals(segment2, PathSegmentComparison.PlatformInvariant));

        var segment3 = (PathSegment)"some/path";

        Assert.Equal(segment1, segment3);
        Assert.True(segment1.Equals(segment3));
        Assert.Equal(segment1.GetHashCode(), segment3.GetHashCode());

        Assert.True(segment1.Equals(segment3, PathSegmentComparison.LiteralValue));
        Assert.True(segment1.Equals(segment3, PathSegmentComparison.PlatformInvariant));

        //Assert.Equal(segment1.GetHashCode(), segment2.GetHashCode());
    }

    [Theory]
    [InlineData("validPath", "bla", "validPath/bla")]
    [InlineData("valid\\Path", "bla/x", "valid/Path/bla/x")]
    [InlineData("folder", "subfolder/file.txt", "folder/subfolder/file.txt")]
    [InlineData("C:\\folder", "subfolder\\file.txt", "C:/folder/subfolder/file.txt")]
    public void PathSegment_OperatorPlus_CombinesSegments(PathSegment segment1, PathSegment segment2, string expected)
    {
        var combined = segment1 + segment2;

        Assert.IsType<PathSegment>(combined);

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

        Assert.Equal(input, segment.ToString());
    }
}

public class RootedPathSegmentTests
{
    [Theory]
    [InlineData("/root", "/root", "\\root")]
    [InlineData("/root/", "/root/", "\\root\\")]
    [InlineData("C:\\root", "C:/root", "C:\\root")]
    [InlineData("D:/root", "D:/root", "D:\\root")]
    [InlineData("C:root/x", "C:root/x", "C:root\\x")]
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
    
    public void RootedPathSegment_InvalidRoot_ThrowsException(string invalidValue)
    {
        Assert.Throws<ArgumentException>(() => (RootedPathSegment)invalidValue);
    }

    [Theory]
    [InlineData("/root", "/root", "\\root")]
    [InlineData("C:\\root", "C:/root", "C:\\root")]
    public void RootedPathSegment_ToPlatformNeutralAndSpecific_CorrectConversion(string input, string expectedNeutral, string expectedSpecific)
    {
        var segment = (RootedPathSegment)input;

        Assert.Equal(expectedNeutral, segment.ToPlatformNeutral());
        Assert.Equal(expectedSpecific, segment.ToPlatformSpecific());
    }

    [Theory]
    [InlineData("C:\\root", "file.txt", "C:/root/file.txt", "C:\\root\\file.txt")]
    public void RootedPathSegment_OperatorPlus_CombinesWithNamePathSegment(RootedPathSegment rootSegment, NamePathSegment relativeSegment, string expectedNeutral, string expectedSpecific)
    {
        var combined = rootSegment + relativeSegment;

        Assert.IsType<FullNamePathSegment>(combined);

        Assert.Equal(expectedNeutral, combined.ToPlatformNeutral());
        Assert.Equal(expectedSpecific, combined.ToPlatformSpecific());
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
    [InlineData("/root", "subfolder", "/root/subfolder", "\\root\\subfolder")]
    [InlineData("C:\\root", "subfolder", "C:/root/subfolder", "C:\\root\\subfolder")]
    public void RootedPathSegment_OperatorPlus_HandlesMixedSeparators(RootedPathSegment rootSegment, RelativePathSegment relativeSegment, string expectedNeutral, string expectedSpecific)
    {
        var combined = rootSegment + relativeSegment;

        Assert.Equal(expectedNeutral, combined.ToPlatformNeutral());
        Assert.Equal(expectedSpecific, combined.ToPlatformSpecific());
    }
}

public class RelativeNamePathSegmentTests
{
    [Theory]
    [InlineData("relativePath", "relativePath", "relativePath")]
    [InlineData("folder\\subfolder", "folder/subfolder", "folder\\subfolder")]
    [InlineData("folder/subfolder", "folder/subfolder", "folder\\subfolder")]
    [InlineData("folder\\subfolder\\file.txt", "folder/subfolder/file.txt", "folder\\subfolder\\file.txt")]
    public void RelativeNamePathSegment_ValidInput_CreatesInstance(RelativeNamePathSegment segment, string platformNeutralExpectedValue, string platformSpecificExpectedValue)
    {
        Assert.Equal(platformNeutralExpectedValue, segment.ToPlatformNeutral());
        Assert.Equal(platformSpecificExpectedValue, segment.ToPlatformSpecific());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\root")]
    [InlineData("/root")]
    public void RelativeNamePathSegment_InvalidInput_ThrowsException(string invalidValue)
    {
        Assert.Throws<ArgumentException>(() => (RelativeNamePathSegment)invalidValue);
    }

    [Theory]
    [InlineData("folder/subfolder", "folder/subfolder")]
    [InlineData("folder\\subfolder", "folder\\subfolder")]
    public void RelativeNamePathSegment_Equality_Tests(RelativeNamePathSegment segment1, RelativeNamePathSegment segment2)
    {
        Assert.Equal(segment1, segment2);
        Assert.Equal(segment1.GetHashCode(), segment2.GetHashCode());

    }



    [Theory]
    [InlineData(null, "file.txt")]
    [InlineData("folder", null)]
    public void RelativeNamePathSegment_OperatorPlus_ThrowsOnNullSegments(string value1, string value2)
    {
        RelativeNamePathSegment? segment1 = value1 != null ? (RelativeNamePathSegment)value1 : null;
        NamePathSegment? segment2 = value2 != null ? (NamePathSegment)value2 : null;

        if (segment1 == null)
        {
            Assert.Throws<ArgumentNullException>(() => segment1 + segment2);
        }
        if (segment2 == null)
        {
            Assert.Throws<ArgumentNullException>(() => segment1 + segment2);
        }
    }

    [Theory]
    [InlineData("folder/subfolder")]
    [InlineData("folder\\subfolder")]
    public void RelativeNamePathSegment_ToString_ReturnsExpectedValue(string input)
    {
        var segment = (RelativeNamePathSegment)input;

        Assert.Equal(input, segment.ToString());
    }
}

public class RelativePathSegmentTest
{
    [Theory]
    [InlineData("folder", "filewithoutextension", "folder/filewithoutextension")]
    [InlineData("folder\\sub", "file.txt", "folder/sub/file.txt")]
    public void RelativeNamePathSegment_OperatorPlus_CombinesSegments(RelativePathSegment segment1, NamePathSegment segment2, RelativeNamePathSegment expected)
    {
        var combined = segment1 + segment2;

        Assert.IsType<RelativeNamePathSegment>(combined);

        Assert.Equal(expected, combined.ToPlatformNeutral());
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