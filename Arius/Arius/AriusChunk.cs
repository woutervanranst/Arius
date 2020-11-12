using System;
using System.IO;

namespace Arius
{
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
