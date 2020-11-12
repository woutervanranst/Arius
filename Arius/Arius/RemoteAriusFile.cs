using System;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    internal abstract class RemoteAriusFile
    {
        protected RemoteAriusFile(BlobItem bi)
        {
            if (!bi.Name.EndsWith(".arius"))
                throw new ArgumentException("NOT A CHUNK"); //TODO

            _bi = bi;
        }
        protected readonly BlobItem _bi;

        public string Name => _bi.Name;
        public abstract string Hash { get; }
    }
}