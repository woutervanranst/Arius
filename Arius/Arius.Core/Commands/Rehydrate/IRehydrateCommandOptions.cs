using Arius.Core.Facade;
using Arius.Core.Repositories;
using System.IO;
using System;

namespace Arius.Core.Commands.Rehydrate;

internal interface IRehydrateCommandOptions : IRepositoryOptions
{
}

internal record RehydrateCommandOptions : RepositoryOptions, IRehydrateCommandOptions
{
    public RehydrateCommandOptions(Repository repo) : base(repo.Options)
    {
    }
}