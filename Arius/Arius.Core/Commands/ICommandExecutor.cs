using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal interface ICommandExecutor
    {
        public Task<int> Execute();
    }
}