using System.Configuration;
using System.Text.Json;

namespace Arius.Explorer.Settings;

public interface IApplicationSettings
{
    List<RepositoryOptions> RecentRepositories { get; set; }
    RepositoryOptions? LastOpenedRepository { get; set; }
    T? GetSetting<T>(string key);
    void SaveSetting<T>(string key, T value);
    void AddRecentRepository(RepositoryOptions repository);
    void RemoveRecentRepository(RepositoryOptions repository);
    void SetLastOpenedRepository(RepositoryOptions? repository);
}

public class ApplicationSettings : ApplicationSettingsBase, IApplicationSettings
{
    private static ApplicationSettings? defaultInstance;
    private const int MaxRecentRepositories = 10;

    public static ApplicationSettings Default
    {
        get
        {
            if (defaultInstance == null)
            {
                defaultInstance = (ApplicationSettings)Synchronized(new ApplicationSettings());
            }
            return defaultInstance;
        }
    }

    [UserScopedSetting]
    [DefaultSettingValue("")]
    public string RecentRepositoriesJson
    {
        get => (string)this[nameof(RecentRepositoriesJson)];
        set => this[nameof(RecentRepositoriesJson)] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("")]
    public string LastOpenedRepositoryJson
    {
        get => (string)this[nameof(LastOpenedRepositoryJson)];
        set => this[nameof(LastOpenedRepositoryJson)] = value;
    }

    public List<RepositoryOptions> RecentRepositories
    {
        get
        {
            try
            {
                if (string.IsNullOrEmpty(RecentRepositoriesJson))
                    return new List<RepositoryOptions>();

                var repositories = JsonSerializer.Deserialize<List<RepositoryOptions>>(RecentRepositoriesJson);
                return repositories ?? new List<RepositoryOptions>();
            }
            catch
            {
                return new List<RepositoryOptions>();
            }
        }
        set
        {
            try
            {
                RecentRepositoriesJson = JsonSerializer.Serialize(value);
                Save();
            }
            catch
            {
                // If serialization fails, clear the setting
                RecentRepositoriesJson = "";
                Save();
            }
        }
    }

    public RepositoryOptions? LastOpenedRepository
    {
        get
        {
            try
            {
                if (string.IsNullOrEmpty(LastOpenedRepositoryJson))
                    return null;

                return JsonSerializer.Deserialize<RepositoryOptions>(LastOpenedRepositoryJson);
            }
            catch
            {
                return null;
            }
        }
        set
        {
            try
            {
                LastOpenedRepositoryJson = value != null ? JsonSerializer.Serialize(value) : "";
                Save();
            }
            catch
            {
                LastOpenedRepositoryJson = "";
                Save();
            }
        }
    }

    public T? GetSetting<T>(string key)
    {
        try
        {
            if (Properties[key] == null)
                return default(T);

            var stringValue = (string)this[key];
            if (string.IsNullOrEmpty(stringValue))
                return default(T);

            if (typeof(T) == typeof(string))
                return (T)(object)stringValue;

            return JsonSerializer.Deserialize<T>(stringValue);
        }
        catch
        {
            return default(T);
        }
    }

    public void SaveSetting<T>(string key, T value)
    {
        try
        {
            // Ensure the property exists
            if (Properties[key] == null)
            {
                var property = new SettingsProperty(key)
                {
                    PropertyType = typeof(string),
                    Provider = Providers["LocalFileSettingsProvider"],
                    SerializeAs = SettingsSerializeAs.String,
                    DefaultValue = "",
                    IsReadOnly = false
                };
                property.Attributes.Add(typeof(UserScopedSettingAttribute), new UserScopedSettingAttribute());
                Properties.Add(property);
                Reload();
            }

            string stringValue = value is string str ? str : JsonSerializer.Serialize(value);
            this[key] = stringValue;
            Save();
        }
        catch
        {
            // If saving fails, silently ignore
        }
    }

    public void AddRecentRepository(RepositoryOptions repository)
    {
        var repositories = RecentRepositories;

        // Remove any existing entry for the same local directory
        repositories.RemoveAll(r => r.LocalDirectoryPath.Equals(repository.LocalDirectoryPath, StringComparison.OrdinalIgnoreCase));

        // Add at the beginning
        repositories.Insert(0, repository);

        // Keep only the most recent ones
        if (repositories.Count > MaxRecentRepositories)
        {
            repositories = repositories.Take(MaxRecentRepositories).ToList();
        }

        RecentRepositories = repositories;
    }

    public void RemoveRecentRepository(RepositoryOptions repository)
    {
        var repositories = RecentRepositories;
        repositories.RemoveAll(r => r.LocalDirectoryPath.Equals(repository.LocalDirectoryPath, StringComparison.OrdinalIgnoreCase));
        RecentRepositories = repositories;
    }

    public void SetLastOpenedRepository(RepositoryOptions? repository)
    {
        LastOpenedRepository = repository;
    }
}