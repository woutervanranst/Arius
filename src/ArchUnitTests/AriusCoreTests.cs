using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.Fluent;
using Xunit;

//add a using directive to ArchUnitNET.Fluent.ArchRuleDefinition to easily define ArchRules
using static ArchUnitNET.Fluent.ArchRuleDefinition;


namespace ArchUnitTests;

public class AriusCoreTests
{
    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(
        typeof(Arius.Core.Application.Bootstrap).Assembly,
        typeof(Arius.Core.Domain.Hash).Assembly,
        typeof(Arius.Core.Infrastucture.BinaryRepository).Assembly,
        typeof(Arius.Core.Interfaces.IBinaryRepository).Assembly).Build();

    // https://archunitnet.readthedocs.io/en/latest/guide/#1-installation

    private readonly IObjectProvider<IType>     ExampleLayer        = Types().That().ResideInAssembly("ExampleAssembly").As("Example Layer");
    private readonly IObjectProvider<Class>     ExampleClasses      = Classes().That().ImplementInterface("IExampleInterface").As("Example Classes");
    private readonly IObjectProvider<IType>     ForbiddenLayer      = Types().That().ResideInNamespace("ForbiddenNamespace").As("Forbidden Layer");
    private readonly IObjectProvider<Interface> ForbiddenInterfaces = Interfaces().That().HaveFullNameContaining("forbidden").As("Forbidden Interfaces");

    


    [Fact]
    public void Test1()
    {

    }
}