using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace Arius.Core.Commands
{
    internal class WfCoreArchiveCommand : ICommand
    {
        internal interface IOptions
        {
            string Path { get; }
        }

        public WfCoreArchiveCommand(IOptions options,
            ILogger<ArchiveCommand> logger)
        {
            root = new DirectoryInfo(options.Path);

            IServiceProvider serviceProvider = ConfigureServices();

            //start the workflow host
            host = serviceProvider.GetService<IWorkflowHost>();
            host.RegisterWorkflow<HelloWorldWorkflow>();
            host.Start();
        }

        private readonly DirectoryInfo root;
        private readonly IWorkflowHost host;

        IServiceProvider ICommand.Services => throw new NotImplementedException();

        private static IServiceProvider ConfigureServices()
        {
            //setup dependency injection
            IServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddWorkflow();
            //services.AddWorkflow(x => x.UseMongoDB(@"mongodb://localhost:27017", "workflow"));
            services.AddTransient<GoodbyeWorld>();

            var serviceProvider = services.BuildServiceProvider();

            return serviceProvider;
        }


        public Task<int> Execute()
        {
            //    host.StartWorkflow("Archive");

            //    Console.ReadLine();

            //    host.Stop();


            host.StartWorkflow("HelloWorld");

            // https://github.com/danielgerlag/workflow-core/issues/162#issuecomment-450663329
            //https://gist.github.com/kpko/f4c10ae7646d58038e0137278e6f49f9

            Console.ReadLine();
            host.Stop();

            return Task.FromResult(0);
        }

        public class HelloWorldWorkflow : IWorkflow
        {
            public void Build(IWorkflowBuilder<object> builder)
            {
                builder
                    .StartWith<HelloWorld>()
                    .Then<GoodbyeWorld>()
                    ;
            }

            public string Id => "HelloWorld";

            public int Version => 1;

        }


        public class HelloWorld : StepBody
        {
            public override ExecutionResult Run(IStepExecutionContext context)
            {
                Console.WriteLine("Hello world");
                return ExecutionResult.Next();
            }
        }


        public class GoodbyeWorld : StepBody
        {

            private ILogger _logger;

            public GoodbyeWorld(ILoggerFactory loggerFactory)
            {
                _logger = loggerFactory.CreateLogger<GoodbyeWorld>();
            }

            public override ExecutionResult Run(IStepExecutionContext context)
            {
                Console.WriteLine("Goodbye world");
                _logger.LogInformation("Hi there!");
                return ExecutionResult.Next();
            }
        }

        //internal class ArchiveWorkflow : IWorkflow
        //{
        //    public void Build(IWorkflowBuilder<object> builder)
        //    {
        //        builder
        //            .StartWith<IndexDirectoryStep>()
        //                //.Input(step => step.Root = root)
        //            .Then<GoodbyeWorld>()
        //            ;
        //    }

        //    public string Id => "Archive";

        //    public int Version => 1;
        //}

        internal class IndexDirectoryStep : StepBody
        {
            private readonly ILogger<IndexDirectoryStep> _logger;

            public IndexDirectoryStep(ILogger<IndexDirectoryStep> logger)
            {
                this._logger = logger;
            }

            public DirectoryInfo Root { get; set; }

            public override ExecutionResult Run(IStepExecutionContext context)
            {
                //_logger.LogInformation($"Indexing {di.FullName}");

                //return IndexDirectory(di, di);

                return ExecutionResult.Next();
            }

            /// <summary>
            /// (new implemenation that excludes system/hidden files (eg .git / @eaDir)
            /// </summary>
            /// <param name="directory"></param>
            /// <returns></returns>
            private IEnumerable<IAriusEntry> IndexDirectory(DirectoryInfo root, DirectoryInfo directory)
            {
                foreach (var file in directory.GetFiles())
                {
                    if (IsHiddenOrSystem(file))
                    {
                        _logger.LogDebug($"Skipping file {file.FullName} as it is SYSTEM or HIDDEN");
                        continue;
                    }
                    else if (IsIgnoreFile(file))
                    {
                        _logger.LogDebug($"Ignoring file {file.FullName}");
                        continue;
                    }
                    else
                    {
                        yield return GetAriusEntry(root, file);
                    }
                }

                foreach (var dir in directory.GetDirectories())
                {
                    if (IsHiddenOrSystem(dir))
                    {
                        _logger.LogDebug($"Skipping directory {dir.FullName} as it is SYSTEM or HIDDEN");
                        continue;
                    }

                    foreach (var f in IndexDirectory(root, dir))
                        yield return f;
                }
            }

            private bool IsHiddenOrSystem(DirectoryInfo d)
            {
                if (d.Name == "@eaDir") //synology internals -- ignore
                    return true;

                return IsHiddenOrSystem(d.Attributes);

            }
            private bool IsHiddenOrSystem(FileInfo fi)
            {
                if (fi.FullName.Contains("eaDir") ||
                    fi.FullName.Contains("SynoResource"))
                    //fi.FullName.Contains("@")) // commenting out -- email adresses are not weird
                    _logger.LogWarning("WEIRD FILE: " + fi.FullName);

                return IsHiddenOrSystem(fi.Attributes);
            }
            private static bool IsHiddenOrSystem(FileAttributes attr)
            {
                return (attr & FileAttributes.System) != 0 || (attr & FileAttributes.Hidden) != 0;
            }

            private static bool IsIgnoreFile(FileInfo fi)
            {
                var lowercaseFilename = fi.Name.ToLower();

                return lowercaseFilename.Equals("autorun.ini") ||
                    lowercaseFilename.Equals("thumbs.db") ||
                    lowercaseFilename.Equals(".ds_store");
            }

            private IAriusEntry GetAriusEntry(DirectoryInfo root, FileInfo fi)
            {
                if (fi.IsPointerFile())
                {
                    _logger.LogInformation($"Found PointerFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                    return new PointerFile(root, fi);
                }
                else
                {
                    _logger.LogInformation($"Found BinaryFile {Path.GetRelativePath(root.FullName, fi.FullName)}");

                    return new BinaryFile(root, fi);
                }
            }
        }

        //public class GoodbyeWorld : StepBody
        //{

        //    private ILogger _logger;

        //    public GoodbyeWorld(ILoggerFactory loggerFactory)
        //    {
        //        _logger = loggerFactory.CreateLogger<GoodbyeWorld>();
        //    }

        //    public override ExecutionResult Run(IStepExecutionContext context)
        //    {
        //        Console.WriteLine("Goodbye world");
        //        _logger.LogInformation("Hi there!");
        //        return ExecutionResult.Next();
        //    }
        //}
    }
}
