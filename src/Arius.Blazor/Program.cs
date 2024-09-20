using Arius.Blazor;
using Arius.Blazor._keenthemes;
using Arius.Blazor._keenthemes.libs;
using Arius.Blazor.Data;
using Arius.Core.New;
using Arius.Web.Application;
using Arius.Web.Domain;
using Arius.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Syncfusion.Blazor;

namespace Arius.Web;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add SignalR
        builder.Services.AddSignalR();

        // Configure Services
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();

        // Ensure the database is created and applies any pending migrations
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Database.Migrate();
        }

        // Configure KTTheme and Icons settings
        IConfiguration themeConfiguration = new ConfigurationBuilder()
                                .AddJsonFile("_keenthemes/config/themesettings.json")
                                .Build();

        IConfiguration iconsConfiguration = new ConfigurationBuilder()
                                .AddJsonFile("_keenthemes/config/icons.json")
                                .Build();

        KTThemeSettings.init(themeConfiguration);
        KTIconsSettings.init(iconsConfiguration);

        app.MapHub<FileProcessingHub>("/jobsHub");

        // Configure the HTTP request pipeline
        ConfigureMiddleware(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Add Razor Components services with Interactive Server Components
        services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Add SyncFusion
        Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1NCaF1cXGNCf1FpRmJGdld5fUVHYVZUTXxaS00DNHVRdkdnWXdccXRWRmBZUk10WkE=");
        services.AddSyncfusionBlazor();

        // Configure DbContext with connection string
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        // Register application services
        services.AddScoped<IStorageAccountRepository, StorageAccountRepository>();
        services.AddScoped<IRepositoryRepository, RepositoryRepository>();
        services.AddScoped<RepositoryService>();
        services.AddSingleton<FileProcessingService>();

        services.AddArius(c => c.LocalConfigRoot = new DirectoryInfo(configuration.GetValue<string>("LocalConfigRoot")));

        // Add theme services
        services.AddSingleton<WeatherForecastService>();
        services.AddSingleton<IKTTheme, KTTheme>();
        services.AddSingleton<IBootstrapBase, BootstrapBase>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
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

        // Map Razor Components with Interactive Server Render Mode
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.UseRouting();

        // Map Blazor Hub
        app.MapBlazorHub();
        //app.MapFallbackToPage("/_Host");
    }
}
