using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arius
{
    interface IChunk
    {
        string FullName { get; }
        string Hash { get;  }
    }

    /// <summary>
    /// Binary chunk (NOT ENCRYPTED / ZIPPED)
    /// </summary>
    class AriusChunk : AriusFile, IChunk
    {
        public AriusChunk(FileInfo file, string hash) : base(file)
        {
            //_hash = hash;
            Hash = hash;
        }
        //private readonly string _hash;
        public string Hash { get; private set; }


        public EncryptedAriusChunk AsEncryptedAriusChunk(string passphrase)
        {
            return EncryptedAriusChunk.GetEncryptedAriusChunk(this, passphrase);
        }
    }

    /// <summary>
    /// Encrypted + zipped binary chunk
    /// </summary>
    class EncryptedAriusChunk : AriusFile
    {
        public static EncryptedAriusChunk GetEncryptedAriusChunk(AriusChunk ariusChunk, string passphrase)
        {
            var encryptedAriusChunkFullName = GetEncryptedAriusChunkFullName(ariusChunk);

            var szu = new SevenZipUtils();
            szu.EncryptFile(ariusChunk.FullName, encryptedAriusChunkFullName, passphrase);

            return new EncryptedAriusChunk(new FileInfo(encryptedAriusChunkFullName));
        }

        private static string GetEncryptedAriusChunkFullName(AriusChunk chunk) => $"{Path.Combine(chunk.DirectoryName, chunk.Hash)}.7z.arius";

        //public override string FullName => ;


        private EncryptedAriusChunk(FileInfo encryptedAriusChunk) : base(encryptedAriusChunk) { }
    }
}
