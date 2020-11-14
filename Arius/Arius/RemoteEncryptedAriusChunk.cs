using System;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    internal class RemoteEncryptedAriusChunk : RemoteAriusFile
    {
        public RemoteEncryptedAriusChunk(AriusRemoteArchive archive, BlobItem bi) : base(archive, bi)
        {
            if (!(bi.Name.EndsWith(".7z.arius") && !bi.Name.EndsWith(".manifest.7z.arius")))
                throw new ArgumentException("NOT A CHUNK"); //TODO
        }

        public override string Hash => _bi.Name.TrimEnd(".7z.arius");
    }
}