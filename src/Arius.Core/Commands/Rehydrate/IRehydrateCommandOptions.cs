using Arius.Core.Facade;
using Arius.Core.Repositories;

namespace Arius.Core.Commands.Rehydrate;

internal record RehydrateCommandOptions : RepositoryOptions
{
    public RehydrateCommandOptions(Repository repo) : base(repo.Options)
    {
    }
}