using Arius.Core.Facade;
using Arius.Core.Repositories;
using Arius.Core.Repositories.StateDb;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;
using static Arius.Core.Repositories.RepositoryBuilder;

namespace Arius.Core.Tests.UnitTests;

class FacadeTests
{
    [Test]
    public async Task QueryPointerFileEntriesSubdirectories_Execute_OK()
    {
        return;

        var db   = new MockedStateDbContextFactory(@"C:\Users\woute\AppData\Local\Arius\states\music\v2.sqlite");
        var repo = new Repository(NullLogger<Repository>.Instance, new MockedRepositoryOptions(), db, null);
        var f    = RepositoryFacade.Create(NullLoggerFactory.Instance, repo);


        var x    = await f.QueryPointerFileEntriesSubdirectories("", 3/*"Backup from 2012/Backup MP3 New Archive/"*/).ToListAsync();
    }
}


class MockedStateDbContextFactory : IStateDbContextFactory
{
    private readonly string dbPath;

    public MockedStateDbContextFactory(string dbPath)
    {
        this.dbPath = dbPath;
    }

    public Task LoadAsync()
    {
        return Task.CompletedTask;
    }

    public StateDbContext GetContext()
    {
        return new StateDbContext(dbPath);
    }

    public Task SaveAsync(DateTime versionUtc)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }
}

record MockedRepositoryOptions : RepositoryOptions
{
    public MockedRepositoryOptions() : base(string.Empty, string.Empty, string.Empty, string.Empty)
    {
    }
}