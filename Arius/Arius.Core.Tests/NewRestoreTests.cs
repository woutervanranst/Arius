using Arius.Core.Extensions;
using Arius.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
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
