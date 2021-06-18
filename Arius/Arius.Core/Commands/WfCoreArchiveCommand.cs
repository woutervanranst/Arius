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
            //coll
                //.AddWorkflow()

                //.AddTransient<IndexDirectoryStep>()
                //.AddTransient<AddHashStep>()

                //services.AddWorkflow(x => x.UseMongoDB(@"mongodb://localhost:27017", "workflow"));
                ;
        }

        IServiceProvider ICommand.Services => throw new NotImplementedException();


        public Task<int> Execute()
        {
            var pipeline = new GenericBCPipelineAwait<DirectoryInfo, int>((di, builder) => di.Step2<>
                )
            return Task.FromResult(0);
        }

        //internal class ArchiveWorkflowState
        //{
        //    public DirectoryInfo Root { get; set; }
        //    public BlockingCollection<IFile> IndexedFileQueue { get; set; } = new();
        //    public BlockingCollection<FileBase> HashedFiles { get; set; } = new();
        //}



        internal static class IndexDirectoryStep
        {
            public static int Ka(this DirectoryInfo di)
            {
                return 5;
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





    public static class GenericBCPipelineAwaitExtensions
    {
        public static TOutput Step2<TInput, TOutput, TInputOuter, TOutputOuter>(this TInput inputType,
            GenericBCPipelineAwait<TInputOuter, TOutputOuter> pipelineBuilder,
            Func<TInput, TOutput> step)
        {
            var pipelineStep = pipelineBuilder.GenerateStep<TInput, TOutput>();
            pipelineStep.StepAction = step;
            return default(TOutput);
        }
    }

    public class GenericBCPipelineAwait<TPipeIn, TPipeOut>
    {
        public interface IPipelineAwaitStep<TStepIn>
        {
            BlockingCollection<Item<TStepIn>> Buffer { get; set; }
        }

        public class GenericBCPipelineAwaitStep<TStepIn, TStepOut> : IPipelineAwaitStep<TStepIn>
        {
            public BlockingCollection<Item<TStepIn>> Buffer { get; set; } = new BlockingCollection<Item<TStepIn>>();
            public Func<TStepIn, TStepOut> StepAction { get; set; }
        }

        public class Item<T>
        {
            public T Input { get; set; }
            public TaskCompletionSource<TPipeOut> TaskCompletionSource { get; set; }
        }

        List<object> _pipelineSteps = new List<object>();


        public GenericBCPipelineAwait(Func<TPipeIn, GenericBCPipelineAwait<TPipeIn, TPipeOut>, TPipeOut> steps)
        {
            steps.Invoke(default(TPipeIn), this);//Invoke just once to build blocking collections
        }

        public Task<TPipeOut> Execute(TPipeIn input)
        {
            var first = _pipelineSteps[0] as IPipelineAwaitStep<TPipeIn>;
            TaskCompletionSource<TPipeOut> tsk = new TaskCompletionSource<TPipeOut>();
            first.Buffer.Add(/*input*/new Item<TPipeIn>()
            {
                Input = input,
                TaskCompletionSource = tsk
            });
            return tsk.Task;
        }

        public GenericBCPipelineAwaitStep<TStepIn, TStepOut> GenerateStep<TStepIn, TStepOut>()
        {
            var pipelineStep = new GenericBCPipelineAwaitStep<TStepIn, TStepOut>();
            var stepIndex = _pipelineSteps.Count;

            Task.Run(() =>
            {
                IPipelineAwaitStep<TStepOut> nextPipelineStep = null;

                foreach (var input in pipelineStep.Buffer.GetConsumingEnumerable())
                {
                    bool isLastStep = stepIndex == _pipelineSteps.Count - 1;
                    TStepOut output;
                    try
                    {
                        output = pipelineStep.StepAction(input.Input);
                    }
                    catch (Exception e)
                    {
                        input.TaskCompletionSource.SetException(e);
                        continue;
                    }
                    if (isLastStep)
                    {
                        input.TaskCompletionSource.SetResult((TPipeOut)(object)output);
                    }
                    else
                    {
                        nextPipelineStep = nextPipelineStep ?? (isLastStep ? null : _pipelineSteps[stepIndex + 1] as IPipelineAwaitStep<TStepOut>);
                        nextPipelineStep.Buffer.Add(new Item<TStepOut>() { Input = output, TaskCompletionSource = input.TaskCompletionSource });
                    }
                }
            });

            _pipelineSteps.Add(pipelineStep);
            return pipelineStep;

        }



        //public interface IAwaitablePipeline<TOutput>
        //{
        //    Task<TOutput> Execute(object input);
        //}

        //public class CastingPipelineWithAwait<TOutput> : IAwaitablePipeline<TOutput>
        //{
        //    class Step
        //    {
        //        public Func<object, object> Func { get; set; }
        //        public int DegreeOfParallelism { get; set; }
        //        public int MaxCapacity { get; set; }
        //    }

        //    class Item
        //    {
        //        public object Input { get; set; }
        //        public TaskCompletionSource<TOutput> TaskCompletionSource { get; set; }
        //    }

        //    List<Step> _pipelineSteps = new List<Step>();
        //    BlockingCollection<Item>[] _buffers;

        //    public event Action<TOutput> Finished;

        //    public void AddStep(Func<object, object> stepFunc, int degreeOfParallelism, int maxCapacity)
        //    {
        //        _pipelineSteps.Add(new Step()
        //        {
        //            Func = stepFunc,
        //            DegreeOfParallelism = degreeOfParallelism,
        //            MaxCapacity = maxCapacity,
        //        });
        //    }

        //    public Task<TOutput> Execute(object input)
        //    {
        //        var first = _buffers[0];
        //        var item = new Item()
        //        {
        //            Input = input,
        //            TaskCompletionSource = new TaskCompletionSource<TOutput>()
        //        };
        //        first.Add(item);
        //        return item.TaskCompletionSource.Task;
        //    }

        //    public IAwaitablePipeline<TOutput> GetPipeline()
        //    {
        //        _buffers = _pipelineSteps.Select(step => new BlockingCollection<Item>()).ToArray();

        //        int bufferIndex = 0;
        //        foreach (var pipelineStep in _pipelineSteps)
        //        {
        //            var bufferIndexLocal = bufferIndex;

        //            for (int i = 0; i < pipelineStep.DegreeOfParallelism; i++)
        //            {
        //                Task.Run(() => { StartStep(bufferIndexLocal, pipelineStep); });
        //            }

        //            bufferIndex++;
        //        }
        //        return this;
        //    }

        //    private void StartStep(int bufferIndexLocal, Step pipelineStep)
        //    {
        //        foreach (var input in _buffers[bufferIndexLocal].GetConsumingEnumerable())
        //        {
        //            object output;
        //            try
        //            {
        //                output = pipelineStep.Func.Invoke(input.Input);
        //            }
        //            catch (Exception e)
        //            {
        //                input.TaskCompletionSource.SetException(e);
        //                continue;
        //            }

        //            bool isLastStep = bufferIndexLocal == _pipelineSteps.Count - 1;
        //            if (isLastStep)
        //            {
        //                input.TaskCompletionSource.SetResult((TOutput)(object)output);
        //            }
        //            else
        //            {
        //                var next = _buffers[bufferIndexLocal + 1];
        //                next.Add(new Item() { Input = output, TaskCompletionSource = input.TaskCompletionSource });
        //            }
        //        }
        //    }
        //}
    }
