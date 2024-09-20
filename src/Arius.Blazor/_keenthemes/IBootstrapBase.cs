using Starterkit._keenthemes.libs;

namespace Starterkit._keenthemes;

public interface IBootstrapBase
{
    void InitThemeMode();
    
    void InitThemeDirection();
    
    void InitRtl();

    void InitLayout();

    void Init(IKTTheme theme);
}