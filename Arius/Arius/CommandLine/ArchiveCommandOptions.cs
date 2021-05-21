//using Arius.Repositories;
//using Arius.Services;
//using Azure.Storage.Blobs.Models;

//namespace Arius.Core.Commands
//{
//    internal class ArchiveCommandOptions : ICommandExecutorOptions,
//        ISHA256HasherOptions,
//        IChunkerOptions,
//        IEncrypterOptions,
//        IAzCopyUploaderOptions,
//        AzureRepository.IAzureRepositoryOptions
//    {
//        public string AccountName { get; init; }
//        public string AccountKey { get; init; }
//        public string Passphrase { get; init; }
//        public bool FastHash { get; init; }
//        public string Container { get; init; }
//        public bool RemoveLocal { get; init; }
//        public AccessTier Tier { get; init; }
//        public bool Dedup { get; init; }
//        public string Path { get; init; }
//    }
//}
