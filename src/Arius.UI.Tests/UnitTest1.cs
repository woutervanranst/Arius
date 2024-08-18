namespace Arius.UI.Tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
        }

    [Test]
    public void GetEntriesAsync_IdenticalPointerFilesAndLocalEntries_Equal()
    {
            //        var x = await Repository
            //            .GetEntriesAsync(SelectedFolder.RelativeDirectoryName)
            //            .ToListAsync();

            //        var y = FileService
            //            .GetEntries(new DirectoryInfo("C:\\Users\\woute\\Documents\\AriusTest"),      //                SelectedFolder.RelativeDirectoryName)
            //            .Where(e => e.Name.EndsWith(".pointer.arius"))
            //            .ToList();

            //        var z  = x.Except(y);
            //        var zz = y.Except(x);
            //        if (z.Any() || zz.Any())
            //        {

            //        }
        }

    [Test]
    public void HydrationStateForChunkedBinaries()
    {

        }

    [Test]
    public void TestThirdLevelNestedGetEntries()
    {
            // eg. [root]/dir1/dir2/dir3 is a crossover point since in the db
            // RelativeParentPath = dir1/dir2 (shenanigans with the /)
            // DirectoryName = dir3
        }
}