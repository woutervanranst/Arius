using Arius.Core.Models;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    [Binding]
    class TestBase
    {
        public TestBase(ScenarioContext sc)
        {
            scenarioContext = sc;
        }

        protected readonly ScenarioContext scenarioContext;
    }

    class LocalTestBase : TestBase
    {
        public LocalTestBase(ScenarioContext sc, Directories dirs) : base(sc)
        {
            directories = dirs;
        }

        protected readonly Directories directories;

        protected (PointerFile pf, PointerFileEntry? pfe) GetPointerInfo(FileInfo fi)
        {
            var pf = scenarioContext.GetPointerService().GetPointerFile(fi);

            var a_rn = Path.GetRelativePath(directories.ArchiveTestDirectory.FullName, fi.FullName);
            var pfe = scenarioContext.GetRepository().PointerFileEntries.GetCurrentEntriesAsync(includeDeleted: true).Result.SingleOrDefault(r => r.RelativeName.StartsWith(a_rn));

            return (pf, pfe);

        }
    }
}
