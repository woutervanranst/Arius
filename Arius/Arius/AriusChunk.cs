using System;
using System.IO;

namespace Arius
{
    internal interface IUnencryptedChunk
    {
        string DirectoryName { get; }
        string FullName { get; }

        /// <summary>
        /// The hash of the unencrytped chunk
        /// </summary>
        string Hash { get; }

        EncryptedAriusChunk GetEncryptedAriusChunk(string passphrase);
    }

    /// <summary>
    /// Binary chunk (NOT ENCRYPTED / ZIPPED)
    /// </summary>
    internal class AriusChunk : AriusFile, IUnencryptedChunk
    {
        public AriusChunk(FileInfo file, string hash) : base(file)
        {
            Hash = hash;
        }

        /// <summary>
        /// The hash of the unencrypted chunk
        /// </summary>
        public string Hash { get; }

        public EncryptedAriusChunk GetEncryptedAriusChunk(string passphrase)
        {
            return EncryptedAriusChunk.GetEncryptedAriusChunk(this, passphrase, true);
        }
    }

    /// <summary>
    /// Encrypted + zipped binary chunk
    /// </summary>
    internal class EncryptedAriusChunk : AriusFile
    {
        public static EncryptedAriusChunk GetEncryptedAriusChunk(IUnencryptedChunk unencryptedChunk, string passphrase, bool deleteUnencrypted)
        {
            var encryptedAriusChunkFullName = GetEncryptedAriusChunkFullName(unencryptedChunk);

            // IF ALREADY EXISTS ON REMOTE ......

            var szu = new SevenZipUtils();
            szu.EncryptFile(unencryptedChunk.FullName, encryptedAriusChunkFullName, passphrase);

            if (deleteUnencrypted)
                File.Delete(unencryptedChunk.FullName);

            return new EncryptedAriusChunk(new FileInfo(encryptedAriusChunkFullName), unencryptedChunk);
        }

        private static string GetEncryptedAriusChunkFullName(IUnencryptedChunk chunk) =>
            $"{Path.Combine(chunk.DirectoryName, chunk.Hash)}.7z.arius";

        private EncryptedAriusChunk(FileInfo encryptedAriusChunk, IUnencryptedChunk unencryptedChunk) : base(encryptedAriusChunk)
        {
            // TODO ik denk niet dat unecnryptedChunk nodig is?
            //_unencryptedChunk = unencryptedChunk;
        }

        //private readonly IUnencryptedChunk _unencryptedChunk;

        //public string UnencryptedHash => _unencryptedChunk.Hash;

        public override string ToString() => base.Name;
    }

    //class EncryptedAriusChunkEqualityComparer : IEqualityComparer<EncryptedAriusChunk>
    //{
    //    public bool Equals([AllowNull] EncryptedAriusChunk x, [AllowNull] EncryptedAriusChunk y)
    //    {
    //        return x?.UnencryptedHash == y?.UnencryptedHash;
    //    }

    //    public int GetHashCode([DisallowNull] EncryptedAriusChunk obj)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

}
