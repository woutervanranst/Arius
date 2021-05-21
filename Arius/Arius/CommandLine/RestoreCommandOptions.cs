//using Arius.Repositories;
//using Arius.Services;
//using Azure.Storage.Blobs.Models;
//using System;

//namespace Arius.Core.Commands
//{
//    internal class RestoreCommandOptions : ICommandExecutorOptions,
//        IChunkerOptions,
//        ISHA256HasherOptions,
//        IAzCopyUploaderOptions,
//        IEncrypterOptions,
//        AzureRepository.IAzureRepositoryOptions
//    {
//        public string AccountName { get; init; }
//        public string AccountKey { get; init; }
//        public string Passphrase { get; init; }
//        public bool FastHash => false; //Do not fasthash on restore to ensure integrity
//        public string Container { get; init; }
//        public bool Synchronize { get; init; }
//        public bool Download { get; init; }
//        public bool KeepPointers { get; init; }
//        public string Path { get; init; }

//        public bool Dedup => false;
//        public AccessTier Tier { get => throw new NotImplementedException(); init => throw new NotImplementedException(); } // Should not be used
//        public bool RemoveLocal { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
//        public int MinSize { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
//        public bool Simulate { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
//    }
//}