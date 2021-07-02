using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal class DedupEvalCommand : ICommand //This class is internal but the interface is public for use in the Facade
    {
        internal class Options : Facade.Facade.IOptions
        {
            public DirectoryInfo Root { get; init; }
        }

        public DedupEvalCommand()
        {

        }

        public Task<int> Execute()
        {
            throw new NotImplementedException();
        }

        IServiceProvider ICommand.Services => throw new NotImplementedException();
    }
}