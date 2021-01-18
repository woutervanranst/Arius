using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;
using Arius.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.CommandLine
{
    internal class RestoreCommandExecutor : ICommandExecutor
    {
        public RestoreCommandExecutor(ICommandExecutorOptions options,
                ILogger<RestoreCommandExecutor> logger,
                ILoggerFactory loggerFactory,

                IConfiguration config,
                AzureRepository azureRepository,

                PointerService ps,
                IHashValueProvider h,
                IChunker c,
                IEncrypter e)
        {
            _options = (RestoreOptions)options;
            _root = new DirectoryInfo(_options.Path);
            _logger = logger;
            _loggerFactory = loggerFactory;

            _config = config;
            _azureRepository = azureRepository;

            _ps = ps;
            _hvp = h;
            _chunker = c;
            _encrypter = e;
        }

        private readonly RestoreOptions _options;
        private readonly ILogger<RestoreCommandExecutor> _logger;
        private readonly ILoggerFactory _loggerFactory;

        private readonly IConfiguration _config;
        private readonly AzureRepository _azureRepository;

        private readonly DirectoryInfo _root;
        
        private readonly PointerService _ps;
        private readonly IHashValueProvider _hvp;
        private readonly IChunker _chunker;
        private readonly IEncrypter _encrypter;


        public int Execute()
        {
            if (_root.Exists && _root.EnumerateFiles().Any())
            {
                // use !pf.LocalContentFileInfo.Exists 
                _logger.LogWarning("The folder is not empty. There may be lingering files after the restore.");
                //TODO LOG WARNING if local root directory contains other things than the pointers with their respecitve localcontentfiles --> will not be overwritten but may be backed up
            }


            // Define blocks & intermediate variables
            var blocks = new ServiceCollection()
                .AddSingleton<ILoggerFactory>(_loggerFactory)
                .AddLogging()

                .AddSingleton<RestoreOptions>(_options)

                .AddSingleton<IConfiguration>(_config)
                .AddSingleton<AzureRepository>(_azureRepository)
                .AddSingleton<PointerService>(_ps)
                .AddSingleton<IHashValueProvider>(_hvp)
                .AddSingleton<IChunker>(_chunker)
                .AddSingleton<IEncrypter>(_encrypter)


                .AddSingleton<SynchronizeBlockProvider>()

                .BuildServiceProvider();


            var synchronizeBlock = blocks.GetService<SynchronizeBlockProvider>()!.GetBlock();

            // Set up linking
            var propagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = true };
            var doNotPropagateCompletionOptions = new DataflowLinkOptions() { PropagateCompletion = false };

            // 10
            //indexDirectoryBlock.LinkTo(
            //    addHashBlock,
            //    propagateCompletionOptions);

            //Fill the flow
            synchronizeBlock.Post(_root);
            synchronizeBlock.Complete();


            // Wait for the end
            synchronizeBlock.Completion.Wait();


            return 0;



            //if (_options.Synchronize)
            //    Synchronize();

            //    if (_options.Download)
            //        Download();
            //}
            ////else if (File.Exists(path) && path.EndsWith(".arius"))
            ////{
            ////    // Restore one file

            ////}
            //else
            //{
            //    throw new NotImplementedException();
            //}

            //return 0;
        }
    }

    
}
