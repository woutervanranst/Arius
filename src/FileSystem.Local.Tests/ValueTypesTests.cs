namespace FileSystem.Local.Tests;

public class ValueTypesTests
{
    [Fact]
    public void Test1()
    {

    }
}


public class PathTests
{
    [Fact]
    public void Root_ValidPath_ShouldCreateRoot()
    {
        var root = (RootPathSegment)"C:\\Users\\Test";
        Assert.Equal("C:\\Users\\Test", root);
    }

}


public class PathSegmentTests
{
    [Theory]
    [InlineData("validPath")]
    [InlineData("anotherPath")]
    public void PathSegment_ValidInput_CreatesInstance(string validValue)
    {
        var segment = new PathSegment(validValue);

        Assert.Equal(validValue, segment.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PathSegment_InvalidInput_ThrowsException(string invalidValue)
    {
        var exception = Assert.Throws<ArgumentException>(() => new PathSegment(invalidValue));

        Assert.Equal("Path segment cannot be null or empty. (Parameter 'value')", exception.Message);
    }

    [Fact]
    public void PathSegment_OperatorPlus_CombinesSegments()
    {
        var segment1 = new PathSegment("folder");
        var segment2 = new PathSegment("file.txt");

        var combined = segment1 + segment2;

        Assert.Equal(Path.Combine("folder", "file.txt"), combined.Value);
    }

    [Fact]
    public void PathSegment_OperatorPlus_ThrowsOnNullSegments()
    {
        PathSegment? segment1 = null;
        var segment2 = new PathSegment("file.txt");

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

        Assert.Equal(root, segment.Value);
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

        Assert.Equal(@"C:\root\subfolder", fullName.Value);
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

        Assert.Equal(relativePath, segment.Value);
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

        Assert.Equal(name, segment.Value);
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

        Assert.Equal(fullName, segment.Value);
    }

    [Fact]
    public void FullNamePathSegment_CreatesFromRootAndRelative()
    {
        var root = new RootPathSegment(@"C:\root");
        var relative = new RelativePathSegment("subfolder/file.txt");

        var fullName = new FullNamePathSegment(root, relative);

        Assert.Equal(@"C:\root\subfolder\file.txt", fullName.Value);
    }
}