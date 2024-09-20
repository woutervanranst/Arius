namespace Starterkit._keenthemes.libs;

// Core theme interface
public interface IKTTheme
{
    void AddHtmlAttribute(string scope, string attributeName, string attributeValue);
    
    void AddHtmlClass(string scope, string className);

    string PrintHtmlAttributes(string scope);

    string PrintHtmlClasses(string scope);

    string GetSvgIcon(string path, string classNames);

    string GetIcon(string iconName, string iconClass="", string iconType="");

    void SetModeSwitch(bool flag);
    
    bool IsModeSwitchEnabled();
    
    void SetModeDefault(string flag);

    string GetModeDefault();

    void SetDirection(string direction);

    string GetDirection();

    bool IsRtlDirection();

    string GetAssetPath(string path);

    string ExtendCssFilename(string path);

    string GetFavicon();

    string[] GetFonts();

    string[] GetGlobalAssets(String type);

    string GetAttributeValue(string scope, string attributeName);
}
