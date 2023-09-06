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

class PointerFileEntryRepositoryTests
{
    [Test]
    public async Task Kak()
    {
        var db   = new MockedStateDbContextFactory2(@"C:\Users\woute\AppData\Local\Arius\states\music\v2.sqlite");
        var repo = new Repository(NullLogger<Repository>.Instance, new MockedRepositoryOptions2(), db, null);
        var f    = RepositoryFacade.Create(NullLoggerFactory.Instance, repo);


        var x    = await f.QueryPointerFileEntriesSubdirectories("", 3/*"Backup from 2012/Backup MP3 New Archive/"*/).ToListAsync();
    }
}


class MockedStateDbContextFactory2 : IStateDbContextFactory
{
    private readonly string dbPath;

    public MockedStateDbContextFactory2(string dbPath)
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

record MockedRepositoryOptions2 : RepositoryOptions
{
    public MockedRepositoryOptions2() : base(string.Empty, string.Empty, string.Empty, string.Empty)
    {
    }
}