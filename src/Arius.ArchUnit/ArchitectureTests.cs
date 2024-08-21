using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Arius.Core.Repositories;
using Arius.Core.Repositories.BlobRepository;
using System.Reflection;
using ArchUnitNET.Fluent;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Arius.ArchUnit;


[TestFixture]
public class ArchitectureTests
{
    // https://archunitnet.readthedocs.io/en/latest/guide/#32-class-dependency-rule


    private static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(typeof(Repository).Assembly).Build();

    private static readonly IObjectProvider<IType> AllowedTypes = Types()
        .That().Are(typeof(Repository))
        .Or().Are(typeof(StateContainerFolder))
        .Or().Are(typeof(BlobContainerFolder<>))
        .Or().Are(typeof(ChunkBlobContainerFolder))
        .Or().Are(typeof(ChunkBlob))
        .Or().Are(typeof(ChunkListBlob))
        .Or().Are(typeof(RepositoryBuilder));
}

[TestFixture]
public class BlobAccessibilityTests
{
    private static readonly System.Type[] AllowedTypes = new[]
    {
            typeof(Repository),
            typeof(StateContainerFolder),
            typeof(BlobContainerFolder<>),
            typeof(ChunkBlobContainerFolder),
            typeof(ChunkBlob),
            typeof(ChunkListBlob),
            typeof(RepositoryBuilder)
        };

    [Test]
    [Ignore("Werkt niet")]
    public void Blob_Class_Should_Only_Be_Accessed_By_Specified_Types()
    {
        var blobType = typeof(Blob);

        // Find all types in the assembly that use the Blob class
        var typesInAssembly = blobType.Assembly.GetTypes();

        foreach (var type in typesInAssembly)
        {
            // Skip the allowed types
            if (AllowedTypes.Contains(type))
                continue;

            // Check if the type uses Blob
            var usesBlob = UsesBlobClass(type, blobType);

            Assert.IsFalse(usesBlob, $"{type.FullName} should not access {blobType.FullName}");
        }


        bool UsesBlobClass(System.Type type, System.Type blobType)
        {
            // Check if the type has any members (fields, properties, methods, etc.) that use Blob
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            foreach (var member in members)
            {
                switch (member)
                {
                    case FieldInfo field when field.FieldType == blobType:
                    case PropertyInfo property when property.PropertyType == blobType:
                    case MethodInfo method when method.ReturnType == blobType || method.GetParameters().Any(p => p.ParameterType == blobType):
                    case ConstructorInfo constructor when constructor.GetParameters().Any(p => p.ParameterType == blobType):
                        return true;
                }
            }

            return false;
        }
    }
}