using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Arius.Core.Features;
using Arius.Core.Features.Archive;
using Arius.Core.Features.Restore;
using Shouldly;

namespace Arius.Core.Tests.Architecture;

public class PublicClassTests
{
    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader()
        .LoadAssembly(typeof(Core.Bootstrapper).Assembly)
        .Build();

    [Fact]
    public void LimitedPublicClassesInAriusCore()
    {
        var allClasses = Architecture.Classes
            .Where(c => c.Namespace.FullName.StartsWith("Arius.Core"))
            .Where(c => !c.IsNested)
            .Where(c => !c.Name.Contains("<")) // Exclude compiler-generated classes
            .ToList();

        var publicClasses = allClasses
            .Where(c => c.Visibility == Visibility.Public)
            .Select(c => c.FullName)
            .ToList();

        publicClasses.ShouldBe([
            typeof(Core.Bootstrapper).FullName,
            typeof(Core.AriusConfiguration).FullName,

            typeof(RepositoryCommand<>).FullName,

            typeof(ArchiveCommand).FullName,
            typeof(Arius.Core.Features.Archive.ProgressUpdate).FullName,
            typeof(Arius.Core.Features.Archive.TaskProgressUpdate).FullName,
            typeof(Arius.Core.Features.Archive.FileProgressUpdate).FullName,
            
            typeof(RestoreCommand).FullName,
            typeof(RestoreCommandResult).FullName,
            typeof(Arius.Core.Features.Restore.ProgressUpdate).FullName,
        ], ignoreOrder: true);
    }

    [Fact]
    public void AllPublicClassesShouldBeSealed()
    {
        var publicClasses = Architecture.Classes
            .Where(c => c.Namespace.FullName.StartsWith("Arius.Core"))
            .Where(c => !c.IsNested)
            .Where(c => !c.Name.Contains("<")) // Exclude compiler-generated classes
            .Where(c => c.Visibility == Visibility.Public)
            .Where(c => c.IsAbstract != true) // Exclude abstract classes which cannot be sealed
            .ToList();

        var unsealedClasses = publicClasses
            .Where(c => c.IsSealed != true)
            .Select(c => c.FullName)
            .ToList();

        unsealedClasses.ShouldBeEmpty($"The following public classes should be sealed: {string.Join(", ", unsealedClasses)}");
    }
}