using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arius.Core.Shared.StateRepositories;

internal class StateRepositoryDbContextDesignTimeFactory : IDesignTimeDbContextFactory<StateRepositoryDbContext>
{
    public StateRepositoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<StateRepositoryDbContext>();

        // Use a temporary SQLite database for migrations design-time
        optionsBuilder.UseSqlite("Data Source=:memory:");

        return new StateRepositoryDbContext(optionsBuilder.Options);
    }
}