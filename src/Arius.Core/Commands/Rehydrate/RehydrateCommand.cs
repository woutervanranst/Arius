using Arius.Core.Facade;
using Arius.Core.Repositories;

namespace Arius.Core.Commands.Rehydrate;

internal record RehydrateCommand : RepositoryOptions
{
    public RehydrateCommand(Repository repo) : base(repo.Options)
    {
    }
}