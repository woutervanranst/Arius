using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
            IServiceProvider serviceProvider)
        {
            root = new DirectoryInfo(options.Path);
            this.logger = logger;
            this.services = serviceProvider;
        }

        private readonly DirectoryInfo root;
        private readonly ILogger<ArchiveCommand> logger;
        private readonly IServiceProvider services;

        internal static void AddBlockProviders(IServiceCollection coll/*, Facade.Facade.ArchiveCommandOptions options*/)
        {
            coll
                .AddWorkflow()

                .AddTransient<IndexDirectoryStep>()
                .AddTransient<AddHashStep>()

                //services.AddWorkflow(x => x.UseMongoDB(@"mongodb://localhost:27017", "workflow"));
                ;
        }

        IServiceProvider ICommand.Services => throw new NotImplementedException();


        public Task<int> Execute()
        {
            //start the workflow host
            var host = services.GetService<IWorkflowHost>();
            host.RegisterWorkflow<ArchiveWorkflow, ArchiveWorkflowState>();
            host.Start();

            host.StartWorkflow("ArchiveWorkflow", new ArchiveWorkflowState { Root = root } );

            // https://github.com/danielgerlag/workflow-core/issues/162#issuecomment-450663329
            //https://gist.github.com/kpko/f4c10ae7646d58038e0137278e6f49f9

            Console.ReadLine();
            host.Stop();

            return Task.FromResult(0);
        }

        internal class ArchiveWorkflowState
        {
            public DirectoryInfo Root { get; set; }
            public BlockingCollection<IFile> IndexedFileQueue { get; set; } = new();
            public BlockingCollection<FileBase> HashedFiles { get; set; } = new();
        }

        public class ArchiveWorkflow : IWorkflow<ArchiveWorkflowState>
        {
            public void Build(IWorkflowBuilder<ArchiveWorkflowState> builder)
            {
                builder
                    .StartWith<IndexDirectoryStep>()
                        //.Output(state => state.IndexedFileQueue, step => step.Files)
                        .Then<AddHashStep>()

                    //.ForEach(state => state.IndexedFileQueue.AsEnumerable()/*, _ => true*/)
                    //    .Do(x => x
                    //        .StartWith<AddHashStep>())


                        //.Output(state => state.ha, step => step.Files)
                    //    .Output((step, state) =>
                    //    {
                    //        var k = state as STATE;

                    //        foreach (var item in step.Files)
                    //            k.IndexedFileQueue.Enqueue(item);
                    //    })
                    //.Then<AddHashStep>()
                    //    .Input(step => step.)
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

            public override ExecutionResult Run(IStepExecutionContext context)
            {
                var state = context.Workflow.Data as ArchiveWorkflowState;
                var root = state.Root;

                _logger.LogInformation($"Indexing {root.FullName}");

                //Files = IndexDirectory(root);

                Task.Run(async () => 
                {
                    foreach (var item in IndexDirectory(root))
                    {
                        //await Task.Yield();
                        //await Task.Delay(1000);
                        //Thread.Sleep(4000);

                        state.IndexedFileQueue.Add(item);
                        //Files.Enqueue(item);
                    }
                });

                

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
            public AddHashStep(ILogger<AddHashBlockProvider> logger, IHashValueProvider hashValueProvider)
            {
                this.logger = logger;
                this.hashValueProvider = hashValueProvider;
            }

            private readonly ILogger<AddHashBlockProvider> logger;
            private readonly IHashValueProvider hashValueProvider;

            public override ExecutionResult Run(IStepExecutionContext context)
            {
                var wf = context.Workflow;
                var state = wf.Data as ArchiveWorkflowState;
                var queue = state.IndexedFileQueue;

                while (!queue.IsCompleted)
                {
                    Parallel.ForEach(
                        queue.GetConsumingPartitioner(), 
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        (item) =>
                        {
                            if (item is PointerFile pf)
                                state.HashedFiles.Add(pf);
                            else if (item is BinaryFile bf)
                            {
                                logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId}] Hashing BinaryFile {bf.RelativeName}");

                                bf.Hash = hashValueProvider.GetHashValue(bf);

                                logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId}] Hashing BinaryFile {bf.RelativeName} done");

                                state.HashedFiles.Add(bf);
                            }
                            else
                                throw new ArgumentException($"Cannot add hash to item of type {item.GetType().Name}");


                            //Thread.Sleep(1000);
                            //await Task.Delay(4000);

                            ////Console.WriteLine("Goodbye world");
                            //_logger.LogInformation(item.FullName);
                        });

                }

                return ExecutionResult.Next();
            }
        }
    }
}
