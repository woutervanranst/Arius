using Arius.Core.Commands.Restore;
using FluentValidation;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Tests.UnitTests;

internal class RestoreCommandOptionsTests : TestBase
{
    //protected override void BeforeTestClass()
    //{
    //    ArchiveTestDirectory.Clear();
    //}

    [Test]
    public void Ctor_ValidDirectory_PathInstanceOfDirectoryInfo()
    {
        //var o = new IRestoreCommandOptions(
        //    accountName: "ha",
        //    accountKey: "he",
        //    container: "hi",
        //    passphrase: "ff",
        //    synchronize: true,
        //    download: true,
        //    keepPointers: true,
        //    path: Directory.GetCurrentDirectory(),
        //    pointInTimeUtc: DateTime.UtcNow);

        //Assert.IsNotNull(o);
        //Assert.IsInstanceOf<DirectoryInfo>(o.Path);
    }

    [Test]
    public void Ctor_ValidFile_PathInstanceOfFileInfo()
    {
        //var fn = Path.Combine(TestBase.RestoreTestDirectory.FullName, "ha.pointer.arius");
        //File.WriteAllText(fn, "");

        //var o = new IRestoreCommandOptions(
        //    accountName: "ha",
        //    accountKey: "he",
        //    container: "hi",
        //    passphrase: "ff",
        //    synchronize: false,
        //    download: true,
        //    keepPointers: true,
        //    path: fn,
        //    pointInTimeUtc: DateTime.UtcNow);

        //Assert.IsNotNull(o);
        //Assert.IsInstanceOf<FileInfo>(o.Path);

        //File.Delete(fn);
    }

    [Test]
    public void Ctor_NonExistingDirectory_FileNotFoundException()
    {
        //Assert.Catch<FileNotFoundException>(() =>
        //    new IRestoreCommandOptions(
        //    accountName: "ha",
        //    accountKey: "he",
        //    container: "hi",
        //    passphrase: "ff",
        //    synchronize: true,
        //    download: true,
        //    keepPointers: true,
        //    path: Path.Combine(Directory.GetCurrentDirectory(), "idonotexist"),
        //    pointInTimeUtc: DateTime.UtcNow));
    }

    [Test]
    public void Ctor_NonExistingFile_FileNotFoundException()
    {
        //Assert.Catch<FileNotFoundException>(() =>
        //    new IRestoreCommandOptions(
        //    accountName: "ha",
        //    accountKey: "he",
        //    container: "hi",
        //    passphrase: "ff",
        //    synchronize: true,
        //    download: true,
        //    keepPointers: true,
        //    path: Path.Combine(Directory.GetCurrentDirectory(), "idonotexist.pointer.arius"),
        //    pointInTimeUtc: DateTime.UtcNow));
    }
}