﻿using System.Collections.Generic;
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
        public long Length { get; }
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


    internal interface IHashValueProvider
    {
        HashValue GetHashValue(BinaryFile hashable);
        HashValue GetHashValue(string fullName);
    }

    internal interface IChunker
    {
        IChunkFile[] Chunk(BinaryFile fileToChunk);
        BinaryFile Merge(IChunkFile[] chunksToJoin, FileInfo target);
    }

    internal interface IEncrypter
    {
        void Encrypt(IFile fileToEncrypt, FileInfo encryptedFile, SevenZipCommandlineEncrypter.Compression compressionLevel, bool deletePlaintext = false);
        void Decrypt(IEncryptedFile fileToDecrypt, FileInfo decryptedFile, bool deleteEncrypted = false);
    }

    internal interface IBlobCopier
    {
        void Upload(IEnumerable<IFile> fileToUpload, AccessTier tier, string remoteDirectoryName, bool overwrite);
        //void Download(string remoteDirectoryName, IEnumerable<Blob> blobsToDownload, DirectoryInfo target);
        //void Download(IEnumerable<BlobItem> blobItems, DirectoryInfo target);
        IEnumerable<FileInfo> Download(IEnumerable<BlobItem> blobsToDownload, DirectoryInfo target, bool flatten);
    }
}
