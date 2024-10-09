using NSubstitute.Core;

namespace Arius.Core.Tests;

internal static class NSubstituteExtensions
{
    public static bool IsSubstitute(this object obj)
    {
        return obj is ICallRouterProvider;
    }

    public static T With<T>(this T substitute, Action<T> setup) where T : class
    {
        setup(substitute);
        return substitute;
    }
}