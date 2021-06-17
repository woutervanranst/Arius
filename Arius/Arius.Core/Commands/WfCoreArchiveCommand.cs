using Arius.Core.Extensions;
using Arius.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
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
            ILogger<ArchiveCommand> logger,
            ILoggerFactory loggerFactory)
        {
            root = new DirectoryInfo(options.Path);
            this.loggingBuilder = loggerFactory;

            IServiceProvider serviceProvider = ConfigureServices();

            //start the workflow host
            host = serviceProvider.GetService<IWorkflowHost>();
            host.RegisterWorkflow<ArchiveWorkflow, STATE>();
            host.Start();
        }

        private readonly DirectoryInfo root;
        private readonly IWorkflowHost host;
        private readonly ILoggerFactory loggingBuilder;

        IServiceProvider ICommand.Services => throw new NotImplementedException();

        private IServiceProvider ConfigureServices()
        {
            //setup dependency injection
            IServiceCollection services = new ServiceCollection();
            
            services.AddSingleton(loggingBuilder);
            services.AddLogging();

            services.AddWorkflow();
            //services.AddWorkflow(x => x.UseMongoDB(@"mongodb://localhost:27017", "workflow"));
            services.AddTransient<IndexDirectoryStep>();
            services.AddTransient<AddHashStep>();

            var serviceProvider = services.BuildServiceProvider();

            return serviceProvider;
        }


        public Task<int> Execute()
        {
            host.StartWorkflow("ArchiveWorkflow", root);

            // https://github.com/danielgerlag/workflow-core/issues/162#issuecomment-450663329
            //https://gist.github.com/kpko/f4c10ae7646d58038e0137278e6f49f9

            Console.ReadLine();
            host.Stop();

            return Task.FromResult(0);
        }

        internal class STATE
        {
            public ConcurrentQueue<IFile> ha { get; set; }
        }

        public class ArchiveWorkflow : IWorkflow<STATE>
        {
            public void Build(IWorkflowBuilder<STATE> builder)
            {
                builder
                    .StartWith<IndexDirectoryStep>()
                        .Output(state => state.ha, step => step.Files)
                    .Then<AddHashStep>()
                        //.Input()
                    ;
            }

            public string Id => "ArchiveWorkflow";

            public int Version => 1;

        }



        internal class IndexDirectoryStep : StepBody
        {
            private readonly ILogger<IndexDirectoryStep> _logger;

            public IndexDirectoryStep(ILogger<IndexDirectoryStep> logger)
            {
                this._logger = logger;
            }

            public IEnumerable<IFile> Files { get; set; }

            public override ExecutionResult Run(IStepExecutionContext context)
            {
                var root = context.Workflow.Data as DirectoryInfo;

                _logger.LogInformation($"Indexing {root.FullName}");

                Files = IndexDirectory(root);

                //foreach (var item in IndexDirectory(root))
                //{
                //}

                return ExecutionResult.Next();
            }

            private IEnumerable<IFile> IndexDirectory(DirectoryInfo directory) => IndexDirectory(directory, directory);

            private IEnumerable<IFile> IndexDirectory(DirectoryInfo root, DirectoryInfo directory)
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

            private IFile GetAriusEntry(DirectoryInfo root, FileInfo fi)
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

        public class AddHashStep : StepBody
        {
            public AddHashStep(ILoggerFactory loggerFactory)
            {
                _logger = loggerFactory.CreateLogger<AddHashStep>();
            }
            private ILogger _logger;
            public IEnumerable<IFile> Files { get; set; }

            public override ExecutionResult Run(IStepExecutionContext context)
            {
                Console.WriteLine("Goodbye world");
                _logger.LogInformation("Hi there!");
                return ExecutionResult.Next();
            }
        }
    }
}
