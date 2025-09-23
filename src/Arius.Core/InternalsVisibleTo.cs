using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Arius.Benchmarks")]
[assembly: InternalsVisibleTo("Arius.Core.Tests")]
[assembly: InternalsVisibleTo("arius")] // for Mediator
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")] // Required for NSubstitute
[assembly: InternalsVisibleTo("Arius.Core.DbMigrationV3V5")] // For migrations
