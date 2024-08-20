using Arius.Core.Facade;
using Arius.Web.Application;
using Arius.Web.Components;
using Arius.Web.Core;
using Arius.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arius.Web;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configure Services
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        // Ensure the database is created and applies any pending migrations
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Database.Migrate();
        }

        // Configure the HTTP request pipeline
        ConfigureMiddleware(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add Razor Components services
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Configure DbContext with connection string
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        // Register application services
        services.AddScoped<IRepositoryOptionsRepository, RepositoryOptionsRepository>();
        services.AddScoped<RepositoryOptionsService>();

        services.AddArius();
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        // Configure exception handling and HSTS
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // Configure remaining middleware
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseAntiforgery();

        // Map Razor Components
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }
}
