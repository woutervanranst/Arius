using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Arius.Core.Configuration;
using Arius.Core.Extensions;
using Arius.Core.Models;
using Arius.Core.Repositories;
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

            return 0;
        }
    }
}
