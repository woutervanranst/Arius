using System.Collections.ObjectModel;
using System.Configuration;

namespace Arius.Explorer.Settings;

public interface IApplicationSettings
{
    ObservableCollection<RepositoryOptions> RecentRepositories { get; }
    int RecentLimit { get; set; }
    void Save();
}

public class ApplicationSettings : ApplicationSettingsBase, IApplicationSettings
{
    private static ApplicationSettings? defaultInstance;

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
    [SettingsSerializeAs(SettingsSerializeAs.Xml)]
    public ObservableCollection<RepositoryOptions> RecentRepositories
    {
        get => (ObservableCollection<RepositoryOptions>)(this[nameof(RecentRepositories)] 
               ??= new ObservableCollection<RepositoryOptions>());
        set => this[nameof(RecentRepositories)] = value;
    }

    [UserScopedSetting]
    [DefaultSettingValue("10")]
    public int RecentLimit
    {
        get => (int)this[nameof(RecentLimit)];
        set => this[nameof(RecentLimit)] = value;
    }
}

public interface IRecentRepositoryManager
{
    /// Full, most-recent-first
    IReadOnlyList<RepositoryOptions> GetAll();

    /// Null if none
    RepositoryOptions? GetMostRecent();

    /// Update last-opened and persist; creates or updates existing.
    void TouchOrAdd(RepositoryOptions repo);

    /// Remove one (optional)
    void Remove(Func<RepositoryOptions, bool> predicate);
}

public sealed class RecentRepositoryManager : IRecentRepositoryManager
{
    private readonly IApplicationSettings settings;

    public RecentRepositoryManager(IApplicationSettings settings)
    {
        this.settings = settings;
    }

    public IReadOnlyList<RepositoryOptions> GetAll() =>
        settings.RecentRepositories
                 .OrderByDescending(r => r.LastOpened)
                 .ToList();

    public RepositoryOptions? GetMostRecent() =>
        settings.RecentRepositories
                 .OrderByDescending(r => r.LastOpened)
                 .FirstOrDefault();

    public void TouchOrAdd(RepositoryOptions repo)
    {
                    DefaultValue = "",
                    IsReadOnly = false
                };
                property.Attributes.Add(typeof(UserScopedSettingAttribute), new UserScopedSettingAttribute());
                Properties.Add(property);
                Reload();
            }

        if (existing is null)
        {
            repo.LastOpened = now;
            settings.RecentRepositories.Add(repo);
        }
        else
        {
            // Update mutable fields you want to keep fresh (+ last opened)
            existing.AccountKeyProtected = repo.AccountKeyProtected;
        }

        // Reorder + Trim
        var ordered = settings.RecentRepositories
                               .OrderByDescending(r => r.LastOpened)
                               .ToList();

        while (ordered.Count > settings.RecentLimit)
            ordered.RemoveAt(ordered.Count - 1);

        // Write back to the observable collection in place (keeps bindings alive)
        settings.RecentRepositories.Clear();
        foreach (var r in ordered) settings.RecentRepositories.Add(r);

        settings.Save();
    }

    public void Remove(Func<RepositoryOptions, bool> predicate)
    {
        var toRemove = settings.RecentRepositories.Where(predicate).ToList();
        foreach (var r in toRemove) settings.RecentRepositories.Remove(r);
        if (toRemove.Count > 0) settings.Save();
    }
}
