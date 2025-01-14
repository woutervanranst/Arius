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
}