using System;
using System.Collections.Generic;
using System.IO;

namespace Arius
{
    internal interface IItem
    {
        string FullName { get; }
        string Name { get; }
        string NameWithoutExtension { get; }
    }
    internal interface IHashable
    {
        HashValue Hash { get; }
    }
    internal interface ILocalFile : IItem, IHashable
    {
        string DirectoryName { get; }
        public IRepository Root { get; }

        void Delete();
    }

    internal interface IArchivable : ILocalFile
    {
        DateTime CreationTimeUtc { get; set; }
        DateTime LastWriteTimeUtc { get; set; }

    }
    internal interface ILocalContentFile : ILocalFile, IArchivable
    {
        FileInfo PointerFileInfo { get; }
    }

    internal interface IPointerFile/*<TObject>*/ : ILocalFile, IArchivable
    {
        string RelativeContentName { get; }

        //TObject GetObject();
        //HashValue ManifestHash { get; }
    }
    internal interface IManifestFile : ILocalFile
    {
    }
    internal interface IChunkFile : IHashable, ILocalFile
    {
    }
    internal interface IChunker //<T> where T : ILocalContentFile
    {
        IEnumerable<IChunkFile> Chunk(ILocalContentFile fileToChunk);
        ILocalContentFile Merge(IEnumerable<IChunkFile> chunksToJoin);
    }



    internal interface IEncryptedLocalFile : ILocalFile
    {
    }
    internal interface IEncrypter
    {
        IEncryptedLocalFile Encrypt(ILocalFile fileToEncrypt, bool deletePlaintext = false);
        ILocalFile Decrypt(IEncryptedLocalFile fileToDecrypt, bool deleteEncrypted = false);
    }
    internal interface IEncryptedManifestFile : ILocalFile, IEncryptedLocalFile
    {
    }


    
    internal interface IEncryptedChunkFile : IHashable, ILocalFile, IEncryptedLocalFile
    {
    }



    internal interface IBlob : IItem
    {
    }
    internal interface IRemoteBlob : IBlob, IHashable
    {
    }
    internal interface IRemoteEncryptedChunkBlob : IRemoteBlob
    {
    }



    internal interface IBlobCopier
    {
        public void Upload<T>(IEnumerable<T> filesToUpload, string remoteDirectoryName, bool overwrite) where T : ILocalFile;
        void Download(IEnumerable<IBlob> blobsToDownload, DirectoryInfo target);
        void Download(string remoteDirectoryName, DirectoryInfo target);
    }



    internal interface IRepository
    {
        string FullName { get; }
    }
    internal interface IGetRepository<out T> : IRepository
    {
        T GetById(HashValue id);
        IEnumerable<T> GetAll();
    }

    internal interface IPutRepository<in T> : IRepository
    {
        void Put(T entity);
        void PutAll(IEnumerable<T> entities);
    }
}
