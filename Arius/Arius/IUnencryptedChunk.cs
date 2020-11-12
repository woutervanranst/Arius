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
}