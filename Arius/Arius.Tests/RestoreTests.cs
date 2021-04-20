using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Arius.CommandLine;
using Arius.Extensions;
using Arius.Models;
using Arius.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using NUnit.Framework;
using NUnit.Framework.Internal;

// https://www.automatetheplanet.com/nunit-cheat-sheet/

namespace Arius.Tests
{
    public partial class ArchiveRestoreTests
    {
        [OneTimeSetUp]
        public void ClassInit_Restore()
        {
            // Executes once for the test class. (Optional)

            //if (TestSetup.restoreTestDirectory.Exists) TestSetup.restoreTestDirectory.Delete(true);
            //TestSetup.restoreTestDirectory.Create();
        }

        [SetUp]
        public void TestInit_Restore()
        {
            // Runs before each test. (Optional)

            if (TestSetup.restoreTestDirectory.Exists) TestSetup.restoreTestDirectory.Delete(true);
            TestSetup.restoreTestDirectory.Create();
        }

        private static readonly FileCompare comparer = new();

        [Test, Order(110)]
        public async Task Restore_OneFileFromCold()
        {
            Assert.IsTrue(TestSetup.restoreTestDirectory.GetFileSystemInfos().Length == 0);

            await RestoreCommand(synchronize: true, download: true, keepPointers: true);

            IEnumerable<FileInfo> archiveFiles = TestSetup.archiveTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);
            IEnumerable<FileInfo> restoredFiles = TestSetup.restoreTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);


            bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);

            Assert.IsTrue(areIdentical);
        }


        [Test, Order(1001)]
        public async Task Restore_FullSourceDirectory_NoPointers()
        {
            Assert.IsTrue(TestSetup.restoreTestDirectory.GetFileSystemInfos().Length == 0);

            await RestoreCommand(synchronize: true, download: true, keepPointers: false);

            IEnumerable<FileInfo> archiveFiles = TestSetup.archiveTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);
            IEnumerable<FileInfo> restoredFiles = TestSetup.restoreTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);


            bool allNonPointerFilesAreRestored = !restoredFiles.Except(archiveFiles, comparer).Any();

            // all non pointer files are restored
            Assert.IsTrue(allNonPointerFilesAreRestored);

            // Does not contain pointer files
            var noPointerFiles = !restoredFiles.Any(fi => fi.IsPointerFile());
            Assert.IsTrue(noPointerFiles);
        }

        
        [Test, Order(1002)]
        public async Task Restore_FullSourceDirectory_OnlyPointers()
        {
            Assert.IsTrue(TestSetup.restoreTestDirectory.GetFileSystemInfos().Length == 0);

            await RestoreCommand(synchronize: true, download: false, keepPointers: true);

            IEnumerable<FileInfo> archiveFiles = TestSetup.archiveTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);
            IEnumerable<FileInfo> restoredFiles = TestSetup.restoreTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);


            archiveFiles = archiveFiles.Where(fi => fi.IsPointerFile());

            bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);

            Assert.IsTrue(areIdentical);
        }

        [Test, Order(1003)]
        public async Task Restore_FullSourceDirectory_Selectively()
        {
            Assert.IsTrue(TestSetup.restoreTestDirectory.GetFileSystemInfos().Length == 0);


            var services = await RestoreCommand(synchronize: true, download: false, keepPointers: true);



            IEnumerable<FileInfo> archiveFiles = TestSetup.archiveTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);
            IEnumerable<FileInfo> restoredFiles = TestSetup.restoreTestDirectory.GetFiles("*.*", SearchOption.AllDirectories);


            archiveFiles = archiveFiles.Where(fi => fi.IsPointerFile());

            bool areIdentical = archiveFiles.SequenceEqual(restoredFiles, comparer);

            Assert.IsTrue(areIdentical);
        }







        private async Task<IServiceProvider> RestoreCommand(bool synchronize, bool download, bool keepPointers)
        {
            var cmd = "restore " +
                $"-n {TestSetup.accountName} " +
                $"-k {TestSetup.accountKey} " +
                $"-p {TestSetup.passphrase} " +
                $"-c {TestSetup.container.Name} " +
                $"{(synchronize ? "--synchronize " : "")}" +
                $"{(download ? "--download " : "")}" +
                $"{(keepPointers ? "--keep-pointers " : "")}" +
                $"{TestSetup.restoreTestDirectory.FullName}";

            return await ExecuteCommand(cmd);
        }

        //private static bool Equal(DirectoryInfo dir1, DirectoryInfo dir2)
        //{
        //    // https://docs.microsoft.com/en-us/dotnet/csharp/programming-guide/concepts/linq/how-to-compare-the-contents-of-two-folders-linq

        //    // Take a snapshot of the file system.  
        //    IEnumerable<FileInfo> list1 = dir1.GetFiles("*.*", SearchOption.AllDirectories);
        //    IEnumerable<FileInfo> list2 = dir2.GetFiles("*.*", SearchOption.AllDirectories);

        //    //A custom file comparer defined below  
        //    FileCompare comparer = new();

        //    // This query determines whether the two folders contain  
        //    // identical file lists, based on the custom file comparer  
        //    // that is defined in the FileCompare class.  
        //    // The query executes immediately because it returns a bool.  
        //    bool areIdentical = list1.SequenceEqual(list2, comparer);

        //    if (!areIdentical)
        //        return false;

        //    return true;

        //    //// Find the common files. It produces a sequence and doesn't
        //    //// execute until the foreach statement.  
        //    //var queryCommonFiles = list1.Intersect(list2, myFileCompare);

        //    //if (queryCommonFiles.Any())
        //    //{
        //    //    Console.WriteLine("The following files are in both folders:");
        //    //    foreach (var v in queryCommonFiles)
        //    //    {
        //    //        Console.WriteLine(v.FullName); //shows which items end up in result list  
        //    //    }
        //    //}
        //    //else
        //    //{
        //    //    Console.WriteLine("There are no common files in the two folders.");
        //    //}

        //    //// Find the set difference between the two folders.  
        //    //// For this example we only check one way.  
        //    //var queryList1Only = (from file in list1
        //    //                      select file).Except(list2, myFileCompare);

        //    //Console.WriteLine("The following files are in list1 but not list2:");
        //    //foreach (var v in queryList1Only)
        //    //{
        //    //    Console.WriteLine(v.FullName);
        //    //}

        //    //// Keep the console window open in debug mode.  
        //    //Console.WriteLine("Press any key to exit.");
        //    //Console.ReadKey();
        //}

        private class FileCompare : IEqualityComparer<FileInfo>
        {
            public FileCompare() { }

            public bool Equals(FileInfo x, FileInfo y)
            {
                return x.Name == y.Name &&
                    x.Length == y.Length &&
                    x.LastWriteTimeUtc == y.LastWriteTimeUtc &&
                    SHA256Hasher.GetHashValue(x.FullName, "").Equals(SHA256Hasher.GetHashValue(y.FullName, ""));
            }

            // Return a hash that reflects the comparison criteria. According to the
            // rules for IEqualityComparer<T>, if Equals is true, then the hash codes must  
            // also be equal. Because equality as defined here is a simple value equality, not  
            // reference identity, it is possible that two or more objects will produce the same  
            // hash code.  
            public int GetHashCode(FileInfo fi)
            {
                return HashCode.Combine(fi.Name, fi.Length, fi.LastWriteTimeUtc, SHA256Hasher.GetHashValue(fi.FullName, ""));
            }
        }
    }

    


    //    /*
    //     * Test cases
    //     *      empty dir
    //     *      dir with files > not to be touched?
    //     *      dir with pointers - too many pointers > to be deleted
    //     *      dir with pointers > not enough pointers > to be synchronzed
    //     *      remote with isdeleted and local present > should be deleted
    //     *      remote with !isdeleted and local not present > should be created
    //     *      also in subdirectories
    //     *      in ariusfile : de verschillende extensions
    //     *      files met duplicates enz upload download
    //     *      al 1 file lokaal > kopieert de rest
    //     *      restore > normal binary file remains untouched
    //     * directory more than 2 deep without other files
    //     *  download > local files exist s> don't download all
    // * restore naar directory waar al andere bestanden (binaries) instaan -< are not touched (dan moet ge maa rnaar ne lege restoren)

    // restore a seoncd time without any changes
    //     * */

    //[TearDown]
    //public void TestCleanup()
    //{
    //    // Runs after each test. (Optional)
    //}
    //[OneTimeTearDown]
    //public void ClassCleanup()
    //{
    //    // Runs once after all tests in this class are executed. (Optional)
    //    // Not guaranteed that it executes instantly after all tests from the class.
    //}
}
