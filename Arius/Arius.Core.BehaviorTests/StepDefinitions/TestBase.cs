using Arius.Core.Models;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    [Binding]
    class TestBase
    {
        public TestBase(ScenarioContext sc)
        {
            scenarioContext = sc;
            directories = sc.ScenarioContainer.Resolve<Directories>();
        }

        protected readonly Directories directories;
        protected readonly ScenarioContext scenarioContext;

        protected async Task<(PointerFile pf, PointerFileEntry? pfe)> GetPointerInfoAsync(FileInfo fi)
        {
            var pf = scenarioContext.GetPointerService().GetPointerFile(directories.ArchiveTestDirectory, fi); //TODO this may not work in case of Restores

            var a_rn = Path.GetRelativePath(directories.ArchiveTestDirectory.FullName, fi.FullName);
            var pfes = await scenarioContext.GetRepository().PointerFileEntries.GetCurrentEntriesAsync(includeDeleted: true);
            var pfe = pfes.SingleOrDefault(r => r.RelativeName.StartsWith(a_rn));

            return (pf, pfe);
        }
    }
}
