using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Sqlite.Infrastructure.Internal;

namespace Arius.Core.Infrastructure.Extensions;

public static class DbContextOptionsExtensions
{
    public static string GetDatabasePath(this DbContextOptions dbContextOptions)
    {
        var connectionString = dbContextOptions.FindExtension<SqliteOptionsExtension>()?.ConnectionString;
        if (connectionString is not null)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return builder.DataSource;
        }
        else
        {
            throw new InvalidOperationException("Unable to determine the database file path.");
        }
    }
}