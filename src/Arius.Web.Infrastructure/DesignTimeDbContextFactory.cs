using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Arius.Web.Infrastructure;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext> // used only for EF Core tools (e.g. dotnet ef migrations add ...)
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var basePath      = Directory.GetCurrentDirectory();
        var directoryInfo = new DirectoryInfo(basePath);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(directoryInfo.Parent.GetDirectories("Arius.Web").Single().FullName)
            .AddJsonFile("appsettings.json")
            .Build();

        var builder          = new DbContextOptionsBuilder<ApplicationDbContext>();
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        builder.UseSqlite(connectionString);

        return new ApplicationDbContext(builder.Options);
    }
}