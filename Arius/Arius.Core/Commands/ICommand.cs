using System;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    public interface ICommand : IObservable<SomeEvent>
    {
        internal IServiceProvider Services { get; }
        public Task<int> Execute();
    }
}