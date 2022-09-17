using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;

namespace Arius.Core.BehaviorTests2.StepDefinitions
{
    [Binding]
    static class ScenarioContextExtensions
    {
        public static Repository GetRepository(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<Repository>();
        public static PointerService GetPointerService(this ScenarioContext sc) => sc.ScenarioContainer.Resolve<PointerService>();

        private const string BINARY_FILES = "BINARIES";
        private const string POINTER_FILES = "POINTERS";
        public record BINARYFILE();
        public record POINTERFILE();

        public static BINARYFILE GetBinaryFile(this ScenarioContext sc, string id) => ((Dictionary<string, BINARYFILE>)sc[BINARY_FILES])[id];
        public static POINTERFILE GetPointerFile(this ScenarioContext sc, string id) => ((Dictionary<string, POINTERFILE>)sc[POINTER_FILES])[id];



        //[BeforeTestRun]
        //private static void Init(context sc)
        //{
        //    sc[BINARY_FILES] = new Dictionary<string, BINARYFILE>();
        //    sc[POINTER_FILES] = new Dictionary<string, POINTERFILE>();

        //}

    }

    class FileSystemContext
    {
        public string name { get; set; }
        
    }


}
