namespace Starterkit._keenthemes.libs;

// Core theme class
public class KTTheme: IKTTheme
{
    // Theme variables
    private bool _modeSwitchEnabled = true;

    private string _modeDefault = "light";

    private string _direction = "ltr";

    private readonly SortedDictionary<string, SortedDictionary<string, string>> _htmlAttributes = new SortedDictionary<string, SortedDictionary<string, string>>();

    private readonly SortedDictionary<string, string[]> _htmlClasses = new SortedDictionary<string, string[]>();

    // Add HTML attributes by scope
    public void AddHtmlAttribute(string scope, string attributeName, string attributeValue)
    {
        SortedDictionary<string, string> attribute = new SortedDictionary<string, string>();
        if (_htmlAttributes.ContainsKey(scope))
        {
            attribute = _htmlAttributes[scope];
        }
        attribute[attributeName] = attributeValue;
        _htmlAttributes[scope] = attribute;
    }

    // Add HTML class by scope
    public void AddHtmlClass(string scope, string className)
    {
        var list = new List<string>();
        if (_htmlClasses.ContainsKey(scope))
        {
            list = _htmlClasses[scope].ToList();
        }
        list.Add(className);
        _htmlClasses[scope] = list.ToArray();
    }

    // Print HTML attributes for the HTML template
    public string PrintHtmlAttributes(string scope)
    {
        var list = new List<string>();
        if (_htmlAttributes.ContainsKey(scope))
        {
            foreach (KeyValuePair<string, string> attribute in _htmlAttributes[scope])
            {
                var item = attribute.Key + "=" + attribute.Value;
                list.Add(item);
            }
            return String.Join(" ", list);
        }
        return String.Empty;
    }

    // Print HTML classes for the HTML template
    public string PrintHtmlClasses(string scope)
    {
        if (_htmlClasses.ContainsKey(scope))
        {
            return String.Join(" ", _htmlClasses[scope]);
        }
        return String.Empty;
    }

    // Get SVG icon content
    public string GetSvgIcon(string path, string classNames)
    {
        var svg = System.IO.File.ReadAllText($"./wwwroot/assets/media/icons/{path}");

        return $"<span class=\"{classNames}\">{svg}</span>";
    }

    // Get keenthemes icon
    public string GetIcon(string iconName, string iconClass = "", string iconType = "")
        {
            string tag = "i";
            string output = "";
            string iconsFinalClass = iconClass=="" ? "" : " "+iconClass;

            if(iconType=="" && KTThemeSettings.Config.IconsType!=""){
                iconType = KTThemeSettings.Config.IconsType;
            }

            if(iconType==""){
                iconType= "duotone";
            }

            if(iconType=="duotone"){
                 int paths = KTIconsSettings.Config.ContainsKey(iconName) ? KTIconsSettings.Config[iconName] : 0;

                output += $"<{tag} class='ki-{iconType} ki-{iconName}{iconsFinalClass}'>";

                for (int i = 0; i < paths; i++)
                {
                    output += $"<span class='path{i+1}'></span>";
                }

                output += $"</{tag}>";
            } else {
                output = $"<{tag} class='ki-{iconType} ki-{iconName}{iconsFinalClass}'></{tag}>";
            }

            return output;
        }

    // Set dark mode enabled status
    public void SetModeSwitch(bool flag)
    {
        _modeSwitchEnabled = flag;
    }

    // Check dark mode status
    public bool IsModeSwitchEnabled()
    {
        return _modeSwitchEnabled;
    }

    // Set the mode to dark or light
    public void SetModeDefault(string flag)
    {
        _modeDefault = flag;
    }

    // Get current mode
    public string GetModeDefault()
    {
        return _modeDefault;
    }

    // Set style direction
    public void SetDirection(string direction)
    {
       _direction = direction;
    }

    // Get style direction
    public string GetDirection()
    {
        return _direction;
    }

    // Check if style direction is RTL
    public bool IsRtlDirection()
    {
        return _direction.ToLower() == "rtl";
    }

    public string GetAssetPath(string path)
    {
        return $"/{KTThemeSettings.Config.AssetsDir}{path}";
    }

    // Extend CSS file name with RTL
    public string ExtendCssFilename(string path)
    {

        if (IsRtlDirection())
        {
            path = path.Replace(".css", ".rtl.css");
        }

        return path;
    }

    // Include favicon from settings
    public string GetFavicon()
    {
        return GetAssetPath(KTThemeSettings.Config.Assets.Favicon);
    }

    // Include the fonts from settings
    public string[] GetFonts()
    {
        return KTThemeSettings.Config.Assets.Fonts.ToArray();
    }

    // Get the global assets
    public string[] GetGlobalAssets(String type)
    {
        List<string> files =
            type == "Css" ? KTThemeSettings.Config.Assets.Css : KTThemeSettings.Config.Assets.Js;
        List<string> newList = new List<string>();

        foreach (string file in files)
        {
            if (type == "Css")
            {
                newList.Add(GetAssetPath(ExtendCssFilename(file)));
            }
            else
            {
                newList.Add(GetAssetPath(file));
            }
        }

        return newList.ToArray();
    }

    public string GetAttributeValue(string scope, string attributeName){
        if (_htmlAttributes.ContainsKey(scope))
        {
            if (_htmlAttributes[scope].ContainsKey(attributeName))
            {
                return _htmlAttributes[scope][attributeName];
            }
            return String.Empty;
        }

        return String.Empty;
    }
}
