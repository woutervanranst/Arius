using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arius
{
    internal interface IChunk
    {
        string DirectoryName { get; }
        string FullName { get; }
        string Hash { get; }
        EncryptedAriusChunk GetEncryptedAriusChunk(string passphrase);
    }

    /// <summary>
    /// Binary chunk (NOT ENCRYPTED / ZIPPED)
    /// </summary>
    internal class AriusChunk : AriusFile, IChunk
    {
        public AriusChunk(FileInfo file, string hash) : base(file)
        {
            //_hash = hash;
            Hash = hash;
        }
        //private readonly string _hash;
        public string Hash { get; private set; }


        public EncryptedAriusChunk GetEncryptedAriusChunk(string passphrase)
        {
            return EncryptedAriusChunk.GetEncryptedAriusChunk(this, passphrase);
        }
    }

    /// <summary>
    /// Encrypted + zipped binary chunk
    /// </summary>
    internal class EncryptedAriusChunk : AriusFile
    {
        public static EncryptedAriusChunk GetEncryptedAriusChunk(IChunk chunk, string passphrase)
        {
            var encryptedAriusChunkFullName = GetEncryptedAriusChunkFullName(chunk);

            // IF ALREADY EXISTS ON REMOTE ......

            var szu = new SevenZipUtils();
            szu.EncryptFile(chunk.FullName, encryptedAriusChunkFullName, passphrase);

            return new EncryptedAriusChunk(new FileInfo(encryptedAriusChunkFullName));
        }

        private static string GetEncryptedAriusChunkFullName(IChunk chunk)
        {
            return $"{Path.Combine(chunk.DirectoryName, chunk.Hash)}.7z.arius";
        }

        private EncryptedAriusChunk(FileInfo encryptedAriusChunk) : base(encryptedAriusChunk) { }
    }
}
