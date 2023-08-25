namespace Arius.Core.BehaviorTests.StepDefinitions;

class RelativePath
{
    public RelativePath(string relativePath)
    {
        Value = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Trim('"'); // the trim seems to be super important for some weird specflow behavior where it binds "profile" to \"profile\"
    }

    public string Value { get; }
}