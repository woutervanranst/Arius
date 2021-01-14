using System.Collections.Generic;
using System.IO;
using Arius.Services;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal interface IFile
    {
        DirectoryInfo Directory { get; }
        DirectoryInfo Root { get; }
        public string Name { get; }
        public string RelativeName { get; }
        public string FullName { get; }
        public void Delete();
    }

    internal interface IFileWithHash : IFile
    {
        public HashValue Hash { get; }
    }
    internal interface IChunkFile : IFileWithHash
    {
        public bool Uploaded { get; set; }
    }
    internal interface IEncryptedFile : IFile
    {
    }
    

    internal interface IChunker
    {
        IEnumerable<IChunkFile> Chunk(BinaryFile fileToChunk);
        BinaryFile Merge(IEnumerable<IChunkFile> chunksToJoin);
    }


    internal interface IEncrypter
    {
        void Encrypt(IFile fileToEncrypt, FileInfo encryptedFile, SevenZipCommandlineEncrypter.Compression compressionLevel, bool deletePlaintext = false);
        void Decrypt(IEncryptedFile fileToDecrypt, FileInfo decryptedFile, bool deleteEncrypted = false);
    }

    internal interface IBlobCopier
    {
        void Upload(IEnumerable<IFile> fileToUpload, AccessTier tier, string remoteDirectoryName, bool overwrite);
        void Download(string remoteDirectoryName, IEnumerable<Blob2> blobsToDownload, DirectoryInfo target);
    }
}
