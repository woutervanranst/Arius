using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;

namespace Arius.Core.BehaviorTests.StepDefinitions
{
    static class ScenarioContextExtensions
    {
        public static Repository GetRepository(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<Repository>();
        public static PointerService GetPointerService(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<PointerService>();

        private const string REMOTE_REPO_STATS = "RepoStats";
        private const string LOCAL_REPO_STATS = "LocalStats";

        public record RemoteRepoStat(int ChunkCount,
            int ManifestCount,
            PointerFileEntry[] CurrentWithDeletedPfes,
            PointerFileEntry[] CurrentExistingPfes,
            PointerFileEntry[] AllPfes);
        public record LocalRepoStat(FileInfo[] PointerFileInfos);

        public static async Task AddRemoteRepoStatsAsync(this ScenarioContext sc)
        {
            if (!sc.ContainsKey(REMOTE_REPO_STATS))
                sc[REMOTE_REPO_STATS] = new List<RemoteRepoStat>();

            sc.GetRemoteRepoStats().Add(await CreateRemoteRepoStat(GetRepository(sc)));
        }
        public static void AddLocalRepoStats(this ScenarioContext sc)
        {
            if (!sc.ContainsKey(LOCAL_REPO_STATS))
                sc[LOCAL_REPO_STATS] = new List<LocalRepoStat>();

            var di = sc.ScenarioContainer.Resolve<Directories>().ArchiveTestDirectory; // todo when you are here because of Restore not working, probably we can do away with the restore directory entirly?
            sc.GetLocalRepoStats().Add(CreateLocalRepoStat(di));
        }


        public static List<RemoteRepoStat> GetRemoteRepoStats(this ScenarioContext sc) => (List<RemoteRepoStat>)sc[REMOTE_REPO_STATS];
        public static List<LocalRepoStat> GetLocalRepoStats(this ScenarioContext sc) => (List<LocalRepoStat>)sc[LOCAL_REPO_STATS];

        private static async Task<RemoteRepoStat> CreateRemoteRepoStat(Repository repo)
        {
            var chunkCount = await repo.Chunks.GetAllChunkBlobs().CountAsync();
            var manifestCount = await repo.Binaries.CountAsync();

            var currentWithDeletedPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(true)).ToArray();
            var currentExistingPfes = (await repo.PointerFileEntries.GetCurrentEntriesAsync(false)).ToArray();

            var allPfes = (await repo.PointerFileEntries.GetPointerFileEntriesAsync()).ToArray();

            return new RemoteRepoStat(chunkCount, manifestCount, currentWithDeletedPfes, currentExistingPfes, allPfes);
        }
        private static LocalRepoStat CreateLocalRepoStat(DirectoryInfo di)
        {
            var pfis = di.GetPointerFileInfos().ToArray();

            return new LocalRepoStat(pfis);
        }

    }
}
