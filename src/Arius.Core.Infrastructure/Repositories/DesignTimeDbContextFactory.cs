using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arius.Core.Infrastructure.Repositories;

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteStateDatabaseContext> // used only for EF Core tools (e.g. dotnet ef migrations add ...)
{
    public SqliteStateDatabaseContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SqliteStateDatabaseContext>();
        builder.UseSqlite();

        return new SqliteStateDatabaseContext(builder.Options, _ => { });
    }
}