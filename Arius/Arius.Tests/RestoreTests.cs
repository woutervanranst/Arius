using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public class RestoreTests
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

        [Test]
        public void Test()
        {

         
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
        //     * */

        [TearDown]
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
