using NUnit.Framework;

namespace Arius.Tests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            Assert.Pass();
        }

        /*
         * Test cases
         * Create File
         * Duplicate file
         * Rename file
         * Delete file
         * Add file again that was previously deleted
         * Rename content file
         * rename .arius file
         * Modify the binary
            * azcopy fails
         * add binary > get .arius file > delete .arius file > archive again > arius file will reappear but cannot appear twice in the manifest
         *
         *
         *
         * add binary
         * add another binary
         * add the same binary
         *
         *
            //TODO test File X is al geupload ik kopieer 'X - Copy' erbij> expectation gewoon pointer erbij binary weg
         *
         *
         * geen lingering files
         *  localcontentfile > blijft staan
         * .7z.arius weg
         *
         * dedup > chunks weg
         * .7z.arius weg
         *
         * #2
         * change a manifest without the binary present
         *
         */
    }
}
