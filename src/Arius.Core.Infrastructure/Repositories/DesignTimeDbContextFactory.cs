using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arius.Core.Infrastructure.Repositories;

internal class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SqliteStateDbContext> // used only for EF Core tools (e.g. dotnet ef migrations add ...)
{
    public SqliteStateDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<SqliteStateDbContext>();
        builder.UseSqlite();

        return new SqliteStateDbContext(builder.Options, _ => { });
    }
}