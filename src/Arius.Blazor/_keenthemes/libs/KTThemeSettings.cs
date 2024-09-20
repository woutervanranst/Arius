namespace Starterkit._keenthemes.libs;

class KTThemeSettings
{
    public static KTThemeBase Config = new KTThemeBase();

    public static void init(IConfiguration configuration)
    {
        Config = configuration.GetSection("Theme").Get<KTThemeBase>() ?? Config;
    }
}
