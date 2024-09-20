using Arius.Blazor._keenthemes.libs;

namespace Arius.Blazor._keenthemes;

public interface IBootstrapBase
{
    void InitThemeMode();
    
    void InitThemeDirection();
    
    void InitRtl();

    void InitLayout();

    void Init(IKTTheme theme);
}