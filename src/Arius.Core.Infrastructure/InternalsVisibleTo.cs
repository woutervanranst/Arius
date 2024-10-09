/*
 * This is required to test the internals of the Arius.Core assembly
 */

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Arius.Core.Tests")]
[assembly: InternalsVisibleTo("Arius.Benchmarks")]
[assembly: InternalsVisibleTo("Arius.ArchUnit")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]