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
            this._logger = logger;
            this.services = serviceProvider;
        }

        private readonly DirectoryInfo root;
        private readonly ILogger<ArchiveCommand> _logger;
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


        public async Task<int> Execute()
        {
            var indexedFiles = new BlockingCollection<IFile>();
            var hashedFiles = new BlockingCollection<FileBase>();


            var t1 = Task.Run(() =>
            {
                foreach (var file in IndexDirectory(root))
                    indexedFiles.Add(file);

                indexedFiles.CompleteAdding();
            });

            var t2 = Task.Run(() =>
            {
                var hvp = services.GetRequiredService<IHashValueProvider>();

                while (!indexedFiles.IsCompleted)
                {
                    Parallel.ForEach(
                        indexedFiles.GetConsumingPartitioner(),
                        new ParallelOptions { MaxDegreeOfParallelism = 2 /*Environment.ProcessorCount */},
                        (file) =>
                        {
                            if (file is PointerFile pf)
                                hashedFiles.Add(pf);
                            else if (file is BinaryFile bf)
                            {
                                _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId}] Hashing BinaryFile {bf.RelativeName}");

                                bf.Hash = hvp.GetHashValue(bf);

                                _logger.LogInformation($"[{Thread.CurrentThread.ManagedThreadId}] Hashing BinaryFile {bf.RelativeName} done");

                                hashedFiles.Add(bf);
                            }
                            else
                                throw new ArgumentException($"Cannot add hash to item of type {file.GetType().Name}");


                            //Thread.Sleep(1000);
                            //await Task.Delay(4000);
                        });
                }
            });


            await Task.WhenAll(t1, t2);

            return 0;
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
}
