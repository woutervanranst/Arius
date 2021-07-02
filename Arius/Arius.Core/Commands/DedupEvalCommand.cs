using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Services;
using Arius.Core.Services.Chunkers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal class DedupEvalCommandOptions : Facade.Facade.IOptions,
        DedupEvalCommand.IOptions,

        IHashValueProvider.IOptions
    {
        public DirectoryInfo Root { get; init; }

        public string Passphrase => string.Empty; //No passphrase/hash seed needed
        public bool FastHash => false; //Do not use fasthash
    }

    internal class DedupEvalCommand : ICommand //This class is internal but the interface is public for use in the Facade
    {
        internal interface IOptions
        {
            DirectoryInfo Root { get; }
        }

        public DedupEvalCommand(IOptions options,
            ILogger<DedupEvalCommand> logger,
            IServiceProvider serviceProvider,
            IHashValueProvider hvp,
            ByteBoundaryChunker chunker)
        {
            this.options = options;
            this.logger = logger;
            this.services = serviceProvider;
            this.hvp = hvp;
            this.chunker = chunker;
        }

        private readonly IOptions options;
        private readonly ILogger<DedupEvalCommand> logger;
        private readonly IServiceProvider services;
        private readonly IHashValueProvider hvp;
        private readonly ByteBoundaryChunker chunker;

        public async Task<int> Execute()
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();


            var indexBlock = new IndexBlock(
                logger: loggerFactory.CreateLogger<IndexBlock>(),
                root: options.Root,
                indexedFile: file => AddFile(file),
                done: () => { });
            var indexTask = indexBlock.GetTask;



            await indexTask;

            logger.LogInformation($"{fileCount} total files");
            logger.LogInformation($"{uniqueFiles.Count} unique files");

            logger.LogInformation($"{fileSize.GetBytesReadable()} total size");
            logger.LogInformation($"{uniqueFiles.Values.Sum().GetBytesReadable()} size with file deduplication");
            logger.LogInformation($"{uniqueChunks.Values.Sum().GetBytesReadable()} size with chunk deduplication");

            return 0;
        }

        IServiceProvider ICommand.Services => throw new NotImplementedException();

        private int fileCount;
        private long fileSize;
        private readonly Dictionary<HashValue, long> uniqueFiles = new();
        private readonly Dictionary<HashValue, long> uniqueChunks = new();

        private void AddFile(IFile f)
        {
            if (f is not BinaryFile)
                return;

            fileCount++;
            fileSize += f.Length;


            var bf = (BinaryFile)f;
            bf.Hash = hvp.GetHashValue(bf);
            if (!uniqueFiles.ContainsKey(bf.Hash))
            { 
                uniqueFiles.Add(bf.Hash, f.Length);

                foreach (var c in chunker.Chunk(bf))
                { 
                    if (!uniqueChunks.ContainsKey(c.Hash))
                        uniqueChunks.Add(c.Hash, c.Length);

                    c.Delete();
                }
            }
        }
    }
}