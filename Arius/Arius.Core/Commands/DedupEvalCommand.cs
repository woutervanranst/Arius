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
        DedupEvalCommand.IOptions
    {
        public DirectoryInfo Root { get; init; }
    }

    internal class DedupEvalCommand : ICommand //This class is internal but the interface is public for use in the Facade
    {
        internal interface IOptions
        {
            DirectoryInfo Root { get; }
        }

        public DedupEvalCommand(IOptions options,
            ILogger<DedupEvalCommand> logger,
            IServiceProvider serviceProvider)
        {
            this.options = options;
            this.logger = logger;
            this.services = serviceProvider;
        }

        private readonly IOptions options;
        private readonly ILogger<DedupEvalCommand> logger;
        private readonly IServiceProvider services;

        public Task<int> Execute()
        {
            throw new NotImplementedException();
        }

        IServiceProvider ICommand.Services => throw new NotImplementedException();
    }
}