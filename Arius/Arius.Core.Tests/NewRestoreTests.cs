using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Arius.Core.Tests
{
    class NewRestoreTests
    {
        [OneTimeSetUp]
        public void ClassInit()
        {
            // Executes once for the test class. (Optional)
        }

        [SetUp]
        public void TestInit()
        {
            // Runs before each test. (Optional)
            TestSetup.restoreTestDirectory.Clear();
        }


        /// <summary>
        /// Test the --synchronize flag
        /// </summary>
        /// <returns></returns>
        [Test] //Deze hoort bij Order(1001) maar gemaakt om apart te draaien
        public async Task Restore_Synchronize_CreatedIfNotExistAndDeletedIfExist()
        {
            //Archive the full directory so that only pointers remain
            await ArchiveRestoreTests.EnsureFullDirectoryArchived(removeLocal: true);

            var pf1 = TestSetup.archiveTestDirectory.GetPointerFileInfos().First();
            var pf2 = TestSetup.archiveTestDirectory.GetPointerFileInfos().Skip(1).First();
            var f3 = new FileInfo(Path.Combine(TestSetup.archiveTestDirectory.FullName, "randomotherfile.doc"));
            File.WriteAllText(f3.FullName, "stuff");

            //They do not refer to the same pointerfile
            Assert.IsTrue(pf1.FullName != pf2.FullName);

            pf1.Delete(); //we delete this one, it will be recreated
            pf2.Rename("bla.pointer.arius"); //we rename this one, it will be deleted


            //run archive
            await ArchiveRestoreTests.RestoreCommand(synchronize: true, download: false, keepPointers: true, path: TestSetup.archiveTestDirectory.FullName);

            Assert.IsTrue(pf1.Exists);
            Assert.IsFalse(pf2.Exists);
            Assert.IsTrue(f3.Exists); //non-pointer files remain intact
        }



        [Test]
        public async Task ha()
        {
            //Archive the full directory so that only pointers remain
            await ArchiveRestoreTests.EnsureFullDirectoryArchived(removeLocal: true);

            // synchronize and download

            // synchronize and do not download

            // download a file

            // download a directory


            // synchronize + file
            var pfi = TestSetup.archiveTestDirectory.GetPointerFileInfos().First();
            var pfi2 = pfi.CopyTo(TestSetup.restoreTestDirectory);
            Assert.CatchAsync<InvalidOperationException>(async () =>
            { 
                try
                { 
                    await ArchiveRestoreTests.RestoreCommand(
                        synchronize: true, 
                        path: pfi2.FullName);
                }
                catch (AggregateException e)
                {
                    throw e.InnerException.InnerException;
                }
            });
    }


        [Test]
        public async Task Restore_FileDoesNotExist_ValidationException()
        {
            //Archive the full directory so that only pointers remain
            await ArchiveRestoreTests.EnsureFullDirectoryArchived(removeLocal: true);

            var pfi = TestSetup.archiveTestDirectory.GetPointerFileInfos().First();
            var pfi2 = pfi.CopyTo(TestSetup.restoreTestDirectory);

            // Delete the pointerfile
            pfi2.Delete();

            // Restore a file that does not exist
            Assert.CatchAsync<FluentValidation.ValidationException>(async () => 
                await ArchiveRestoreTests.RestoreCommand(
                    path: pfi2.FullName));
        }

        [Test]
        public async Task Restore_FolderDoesNotExist_ValidationException()
        { 
        }





        public void TestCleanup()
        {
            // Runs after each test. (Optional)
        }
        [OneTimeTearDown]
        public void ClassCleanup()
        {
            // Runs once after all tests in this class are executed. (Optional)
            // Not guaranteed that it executes instantly after all tests from the class.
        }
    }
}
