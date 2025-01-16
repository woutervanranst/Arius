//using ZioFileSystem;

//namespace FileSystem.Local.Tests;

//public class ZioTests
//{
//    [Theory]
//    [InlineData("c:\\temp\\ble.txt")]
//    [InlineData("//mnt/c/temp/ble.txt")]
//    public void X(ZioFileSystem.PathSegment segment)
//    {
//        Class1.ha();

//        //var x = x;
//    }

//    [Theory]
//    [InlineData("", "dir", "dir")]
//    [InlineData("dir", "", "dir")]
//    public void RelativeNamePathSegment_OperatorPlus_CombinesSegments(ZioFileSystem.PathSegment segment1, ZioFileSystem.PathSegment segment2, ZioFileSystem.PathSegment expected)
//    {
//        var actual = segment1 + segment2;

//        Assert.Equal(expected, actual);
//    }
//}