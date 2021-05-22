using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    public interface ICommandExecutor
    {
        internal IServiceProvider Services { get; }
        public Task<int> Execute();
    }
}