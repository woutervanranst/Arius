//using Arius.Core.Extensions;
//using Arius.Core.Models;
//using Arius.Core.Services;
//using Arius.Core.Services.Chunkers;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Logging;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Arius.Core.Commands.DedupEval;

//internal class DedupEvalCommand : ICommand //This class is internal but the interface is public for use in the Facade
//{
//    internal interface IOptions
//    {
//        DirectoryInfo Root { get; }
//    }

//    public DedupEvalCommand(IOptions options,
//        ILogger<DedupEvalCommand> logger,
//        IServiceProvider serviceProvider,
//        IHashValueProvider hvp,
//        ByteBoundaryChunker chunker)
//    {
//        this.options = options;
//        this.logger = logger;
//        services = serviceProvider;
//        this.hvp = hvp;
//        this.chunker = chunker;
//    }

//    private readonly IOptions options;
//    private readonly ILogger<DedupEvalCommand> logger;
//    private readonly IServiceProvider services;
//    private readonly IHashValueProvider hvp;
//    private readonly ByteBoundaryChunker chunker;

//    public async Task<int> Execute()
//    {
//        foreach (var bfi in options.Root.GetBinaryFileInfos())
//        {
//            var binaryHash = hvp.GetBinaryHash(bfi);
//            AddFile(new BinaryFile(options.Root, bfi, binaryHash));
//        }

//        logger.LogInformation($"{fileCount} total files");
//        logger.LogInformation($"{uniqueBinaries.Count} unique files");

//        logger.LogInformation($"{fileSize.GetBytesReadable()} total size");
//        logger.LogInformation($"{uniqueBinaries.Values.Sum().GetBytesReadable()} size with file deduplication");
//        logger.LogInformation($"{uniqueChunks.Values.Sum().GetBytesReadable()} size with chunk deduplication");

//        return 0;
//    }

//    IServiceProvider ICommand.Services => throw new NotImplementedException();

//    private int fileCount;
//    private long fileSize;
//    private readonly Dictionary<BinaryHash, long> uniqueBinaries = new();
//    private readonly Dictionary<ChunkHash, long> uniqueChunks = new();

//    private void AddFile(BinaryFile bf)
//    {
//        throw new NotImplementedException();

//        //fileCount++;
//        //fileSize += bf.Length;

//        //if (!uniqueFiles.ContainsKey(bf.Hash))
//        //{
//        //    uniqueFiles.Add(bf.Hash, bf.Length);

//        //    await foreach (var c in chunker.Chunk(bf))
//        //    {
//        //        if (!uniqueChunks.ContainsKey(c.Hash))
//        //            uniqueChunks.Add(c.Hash, c.Length);

//        //        c.Delete();
//        //    }
//        //}
//    }
//}