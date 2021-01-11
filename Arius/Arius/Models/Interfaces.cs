using System;
using System.Collections.Generic;
using System.IO;
using Arius.Services;
using Azure.Storage.Blobs.Models;

namespace Arius.Models
{
    internal interface IFile
    {
        DirectoryInfo Directory { get; }
        public string Name { get; }
        public string FullName { get; }
        public void Delete();
    }

    internal interface IFileWithHash : IFile
    {
        public HashValue Hash { get; }
    }

    internal interface IEncryptedFile : IFile
    {
    }

    internal interface IChunkFile : IFileWithHash
    {
    }

    //internal interface IItem
    //{
    //    string FullName { get; }
    //    string Name { get; }
    //    //string NameWithoutExtension { get; }
    //}
    //    internal interface IHashable
    //    {
    //        HashValue Hash { get; }
    //    }
    //internal interface IFile : IItem //, IHashable
    //{
    //    string DirectoryFullName { get; }
    //    //public IRepository Root { get; }

    //    void Delete();
    //}



    //    internal interface IArchivable : ILocalFile
    //    {
    //        string RelativeName { get; }
    //        DateTime CreationTimeUtc { get; set; }
    //        DateTime LastWriteTimeUtc { get; set; }

    //    }
    //    internal interface ILocalContentFile : ILocalFile, IArchivable
    //    {
    //        FileInfo PointerFileInfo { get; }
    //    }

    //    internal interface IPointerFile : ILocalFile, IArchivable, IHashable
    //    {
    //        FileInfo LocalContentFileInfo { get; }

    //        /// <summary>
    //        /// The HashValue of the Content of the ManifestFile
    //        /// </summary>
    //        new HashValue Hash { get; }

    //        //string ManifestHashValue { get; }

    //        //TObject GetObject();
    //        //HashValue ManifestHash { get; }
    //    }
    //    internal interface IManifestFile : ILocalFile
    //    {
    //    }
    //    internal interface IChunkFile : IHashable, ILocalFile
    //    {
    //    }
    internal interface IChunker //<T> where T : ILocalContentFile
    {
        IChunkFile[] Chunk(BinaryFile fileToChunk);
        BinaryFile Merge(IChunkFile[] chunksToJoin);
    }



    //    internal interface IEncryptedLocalFile : ILocalFile
    //    {
    //    }
    internal interface IEncrypter
    {
        void Encrypt(IFile fileToEncrypt, FileInfo encryptedFile, SevenZipCommandlineEncrypter.Compression compressionLevel, bool deletePlaintext = false);
        void Decrypt(IEncryptedFile fileToDecrypt, FileInfo decryptedFile, bool deleteEncrypted = false);
    }
    //    internal interface IEncryptedManifestFile : ILocalFile, IEncryptedLocalFile
    //    {
    //    }



    //    internal interface IEncryptedChunkFile : IHashable, ILocalFile, IEncryptedLocalFile
    //    {
    //    }



    //    internal interface IBlob : IItem
    //    {
    //    }
    //    internal interface IRemoteBlob : IBlob, IHashable
    //    {
    //    }
    //    internal interface IRemoteEncryptedChunkBlobItem : IRemoteBlob
    //    {
    //        AccessTier AccessTier { get; }
    //        bool CanDownload();
    //        IRemoteEncryptedChunkBlobItem Hydrated { get; }
    //        string Folder { get; }
    //    }



    internal interface IBlobCopier
    {
        void Upload(IFile fileToUpload, AccessTier tier, string remoteDirectoryName, bool overwrite);
        void Download(string remoteDirectoryName, IEnumerable<Blob2> blobsToDownload, DirectoryInfo target);
}



//    internal interface IRepository
//    {
//        string FullName { get; }
//    }
//    internal interface IGetRepository<out T> : IRepository
//    {
//        T GetById(HashValue id);
//        IEnumerable<T> GetAll();
//    }

//    internal interface IPutRepository<in T> : IRepository
//    {
//        void Put(T entity);
//        void PutAll(IEnumerable<T> entities);
//    }
}
