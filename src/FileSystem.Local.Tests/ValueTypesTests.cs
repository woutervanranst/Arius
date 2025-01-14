namespace FileSystem.Local.Tests;

public class PlatformNeutralPathSegmentTests
{
    [Theory]
    [InlineData("validPath", "validPath", "validPath")]
    [InlineData("validPath\\", "validPath/", "validPath\\")]
    [InlineData("some\\path", "some/path", "some\\path")]
    [InlineData("some\\path\\", "some/path/", "some\\path\\")]
    [InlineData("C:\\some\\path\\", "C:/some/path/", "C:\\some\\path\\")]
    [InlineData("C:\\some\\path", "C:/some/path", "C:\\some\\path")]
    public void PlatformNeutralPathSegment_ValidInput_CreatesInstance(string validValue, string plaformNeutralExpectedValue, string platformSpecificExpectedValue)
    {
        var segment = (PlatformNeutralPathSegment)validValue;

        Assert.Equal(plaformNeutralExpectedValue, segment.ToPlatformNeutral());
        Assert.Equal(platformSpecificExpectedValue, segment.ToPlatformSpecific());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PlatformNeutralPathSegment_InvalidInput_ThrowsException(string invalidValue)
    {
        var exception = Assert.Throws<ArgumentException>(() => new PlatformNeutralPathSegment(invalidValue));

        Assert.Equal("Path segment cannot be null or empty. (Parameter 'value')", exception.Message);
    }

    [Fact]
    public void PlatformNeutralPathSegment_OperatorPlus_CombinesSegments()
    {
        var segment1 = new PlatformNeutralPathSegment("folder");
        var segment2 = new PlatformNeutralPathSegment("file.txt");

        var combined = segment1 + segment2;

        Assert.Equal(Path.Combine("folder", "file.txt"), combined);
    }

    [Fact]
    public void PlatformNeutralPathSegment_OperatorPlus_ThrowsOnNullSegments()
    {
        PlatformNeutralPathSegment? segment1 = null;
        var segment2 = new PlatformNeutralPathSegment("file.txt");

        Assert.Throws<ArgumentNullException>(() => segment1 + segment2);
        Assert.Throws<ArgumentNullException>(() => segment2 + segment1);
    }
}

public class RootPathSegmentTests
{
    [Theory]
    [InlineData(@"C:\root")]
    [InlineData(@"/root")]
    public void RootPathSegment_ValidRoot_CreatesInstance(string root)
    {
        var segment = new RootPathSegment(root);

        Assert.Equal(root, segment);
    }

    [Theory]
    [InlineData("relativePath")]
    [InlineData(" ")]
    public void RootPathSegment_InvalidRoot_ThrowsException(string invalidRoot)
    {
        var exception = Assert.Throws<ArgumentException>(() => new RootPathSegment(invalidRoot));

        Assert.Equal("Root must be a rooted path. (Parameter 'value')", exception.Message);
    }

    [Fact]
    public void RootPathSegment_OperatorPlus_ReturnsFullNamePathSegment()
    {
        var root = new RootPathSegment(@"C:\root");
        var relative = new RelativePathSegment("subfolder");

        var fullName = root + relative;

        Assert.Equal(@"C:\root\subfolder", fullName);
    }
}

public class RelativePathSegmentTests
{
    [Theory]
    [InlineData("relativePath")]
    [InlineData("folder/file.txt")]
    public void RelativePathSegment_ValidRelativePath_CreatesInstance(string relativePath)
    {
        var segment = new RelativePathSegment(relativePath);

        Assert.Equal(relativePath, segment);
    }

    [Theory]
    [InlineData(@"C:\absolutePath")]
    [InlineData(@"/absolutePath")]
    public void RelativePathSegment_InvalidRelativePath_ThrowsException(string invalidPath)
    {
        var exception = Assert.Throws<ArgumentException>(() => new RelativePathSegment(invalidPath));

        Assert.Equal("Relative name cannot be a rooted value. (Parameter 'value')", exception.Message);
    }
}

public class NamePathSegmentTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("fileName.ext")]
    public void NamePathSegment_ValidName_CreatesInstance(string name)
    {
        var segment = new NamePathSegment(name);

        Assert.Equal(name, segment);
    }

    [Theory]
    [InlineData("file/name")]
    [InlineData("file\\name")]
    public void NamePathSegment_InvalidName_ThrowsException(string invalidName)
    {
        var exception = Assert.Throws<ArgumentException>(() => new NamePathSegment(invalidName));

        Assert.Equal("Name cannot contain path separators. (Parameter 'name')", exception.Message);
    }
}

public class FullNamePathSegmentTests
{
    [Theory]
    [InlineData(@"C:\folder\file.txt")]
    public void FullNamePathSegment_ValidFullName_CreatesInstance(string fullName)
    {
        var segment = (FullNamePathSegment)fullName;

        Assert.Equal(fullName, segment);
    }

    [Fact]
    public void FullNamePathSegment_CreatesFromRootAndRelative()
    {
        var root = new RootPathSegment(@"C:\root");
        var relative = new RelativePathSegment("subfolder/file.txt");

        var fullName = new FullNamePathSegment(root, relative);

        Assert.Equal(@"C:\root\subfolder\file.txt", fullName);
    }
}