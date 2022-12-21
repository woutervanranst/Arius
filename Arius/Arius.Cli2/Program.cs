// See https://stackoverflow.com/a/68394788/1582323

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.Configuration;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services => { services.AddTransient<AriusBla>(); })
    //.AddEnvironmentVariables()
    .Build();

var my = host.Services.GetRequiredService<AriusBla>();
return await my.ExecuteAsync();

class AriusBla
{
    private readonly ILogger<AriusBla> _logger;

    public AriusBla(ILogger<AriusBla> logger, IConfiguration config)
    {
        _logger = logger;

        var xx = Environment.GetCommandLineArgs();

        var x = config["args"];
    }

    public async Task<int> ExecuteAsync(CancellationToken stoppingToken = default)
    {
        _logger.LogInformation("Doing something");

        return 0;
    }
}