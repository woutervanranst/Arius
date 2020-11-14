using System.IO;

namespace Arius
{
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
}