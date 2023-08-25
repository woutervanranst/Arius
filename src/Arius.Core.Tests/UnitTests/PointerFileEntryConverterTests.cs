using System.IO;
using NUnit.Framework;

namespace Arius.Core.Tests.UnitTests;

class PointerFileEntryConverterTests
{
    [Test]
    public void Deconstruct_VariousStrings_OK()
    {
        var x = PointerFileEntryConverter.Deconstruct("a.txt");
        Assert.AreEqual("a.txt", x.Name);
        Assert.AreEqual("", x.DirectoryName);
        Assert.AreEqual("", x.RelativeParentPath);

        x = PointerFileEntryConverter.Deconstruct(Path.Combine("dir", "a.txt"));
        Assert.AreEqual("a.txt", x.Name);
        Assert.AreEqual("dir", x.DirectoryName);
        Assert.AreEqual("", x.RelativeParentPath);

        x = PointerFileEntryConverter.Deconstruct(Path.Combine("dir ff", "a.txt"));
        Assert.AreEqual("a.txt", x.Name);
        Assert.AreEqual("dir ff", x.DirectoryName);
        Assert.AreEqual("", x.RelativeParentPath);

        x = PointerFileEntryConverter.Deconstruct(Path.Combine("some dir", "dir", "a.txt"));
        Assert.AreEqual("a.txt", x.Name);
        Assert.AreEqual("dir", x.DirectoryName);
        Assert.AreEqual("some dir", x.RelativeParentPath);

        x = PointerFileEntryConverter.Deconstruct(Path.Combine("root dir", "some dir", "dir", "a.txt"));
        Assert.AreEqual("a.txt", x.Name);
        Assert.AreEqual("dir", x.DirectoryName);
        Assert.AreEqual(Path.Combine("root dir", "some dir"), x.RelativeParentPath); // !!!! this one is indeed platform specific

        //x = PointerFileEntryConverter.Deconstruct("dir/a.txt");
        //Assert.AreEqual("a.txt", x.Name);
        //Assert.AreEqual("dir", x.DirectoryName);
        //Assert.AreEqual("", x.RelativeParentPath);
        
        //x = PointerFileEntryConverter.Deconstruct(@"dir ff\a.txt");
        //Assert.AreEqual("a.txt", x.Name);
        //Assert.AreEqual("dir ff", x.DirectoryName);
        //Assert.AreEqual("", x.RelativeParentPath);
        
        //x = PointerFileEntryConverter.Deconstruct(@"some dir/dir/a.txt");
        //Assert.AreEqual("a.txt", x.Name);
        //Assert.AreEqual("dir", x.DirectoryName);
        //Assert.AreEqual("some dir", x.RelativeParentPath);
        
        //x = PointerFileEntryConverter.Deconstruct(@"some dir\dir\a.txt");
        //Assert.AreEqual("a.txt", x.Name);
        //Assert.AreEqual("dir", x.DirectoryName);
        //Assert.AreEqual("some dir", x.RelativeParentPath);
        
        //x = PointerFileEntryConverter.Deconstruct(@"root dir/some dir/dir/a.txt");
        //Assert.AreEqual("a.txt", x.Name);
        //Assert.AreEqual("dir", x.DirectoryName);
        //Assert.AreEqual(@"root dir\some dir", x.RelativeParentPath); //  !!!! this one is indeed platform specific
        
        //x = PointerFileEntryConverter.Deconstruct(@"root dir\some dir\dir\a.txt");
        //Assert.AreEqual("a.txt", x.Name);
        //Assert.AreEqual("dir", x.DirectoryName);
        //Assert.AreEqual(@"root dir\some dir", x.RelativeParentPath);
    }
}