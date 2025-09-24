using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
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

            typeof(Core.Features.StorageAccountCommandProperties).FullName,
            typeof(Core.Features.RepositoryCommandProperties).FullName,

            typeof(Core.Features.Commands.Archive.ArchiveCommand).FullName,
            typeof(Core.Features.Commands.Archive.ProgressUpdate).FullName,
            typeof(Core.Features.Commands.Archive.TaskProgressUpdate).FullName,
            typeof(Core.Features.Commands.Archive.FileProgressUpdate).FullName,
            
            typeof(Core.Features.Commands.Restore.RestoreCommand).FullName,
            typeof(Core.Features.Commands.Restore.RestoreCommandResult).FullName,
            typeof(Core.Features.Commands.Restore.RehydrationDetail).FullName,
            typeof(Core.Features.Commands.Restore.ProgressUpdate).FullName,
            typeof(Core.Features.Commands.Restore.TaskProgressUpdate).FullName,
            typeof(Core.Features.Commands.Restore.FileProgressUpdate).FullName,
            
            typeof(Core.Features.Queries.ContainerNames.ContainerNamesQuery).FullName,
            
            // PointerFileEntries query types
            typeof(Core.Features.Queries.PointerFileEntries.PointerFileEntriesQuery).FullName,
            typeof(Core.Features.Queries.PointerFileEntries.PointerFileEntriesQueryResult).FullName,
            typeof(Core.Features.Queries.PointerFileEntries.PointerFileEntriesQueryDirectoryResult).FullName,
            typeof(Core.Features.Queries.PointerFileEntries.PointerFileEntriesQueryFileResult).FullName,
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