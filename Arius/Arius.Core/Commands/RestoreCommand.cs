using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Arius.Core.Commands
{
    internal class RestoreCommand : ICommand //This class is internal but the interface is public for use in the Facade
    {
        internal interface IOptions
        {
            string Path { get; }
            bool Download { get; }
            bool Synchronize { get; }
        }

        public RestoreCommand(IOptions options,
            ILogger<RestoreCommand> logger,
            IServiceProvider serviceProvider)
        {
            this.options = options;
            this.logger = logger;
            this.services = serviceProvider;
        }

        private readonly IOptions options;
        private readonly ILogger<RestoreCommand> logger;
        private readonly IServiceProvider services;

        internal static void AddBlockProviders(IServiceCollection coll)
        {
        }

        IServiceProvider ICommand.Services => services;

        public async Task<int> Execute()
        {
            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var repo = services.GetRequiredService<Repository>();
            var pointerService = services.GetRequiredService<PointerService>();

            var synchronizeBlock = new SynchronizeBlock(
                logger: loggerFactory.CreateLogger<SynchronizeBlock>(),
                root: new DirectoryInfo(options.Path),
                repo: repo,
                pointerService: pointerService,
                pointerToDownload: _ => { },
                done: () => { });
            var synchronizeTask = synchronizeBlock.GetTask;

            await Task.WhenAny(Task.WhenAll(BlockBase.AllTasks), BlockBase.CancellationTask);

            return 0;
        }
    }
}
