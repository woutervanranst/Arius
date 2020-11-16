//using Azure.Storage.Blobs.Models;
//using System;

//namespace Arius
//{
//    internal abstract class RemoteAriusFile
//    {
//        protected RemoteAriusFile(AriusRemoteArchive archive, BlobItem bi)
//        {
//            if (!bi.Name.EndsWith(".arius"))
//                throw new ArgumentException("NOT AN ARIUS FILE"); //TODO

//            _archive = archive;
//            _bi = bi;
//        }

//        protected readonly AriusRemoteArchive _archive;
//        protected readonly BlobItem _bi;

//        public string Name => _bi.Name;
//        public abstract string Hash { get; }
//    }
//}