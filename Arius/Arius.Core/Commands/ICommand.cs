using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    public interface ICommand
    {
        internal IServiceProvider Services { get; }
        public Task<int> Execute();
    }
}