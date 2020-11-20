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
        string RelativeContentName { get; }
        DateTime CreationTimeUtc { get; set; }
        DateTime LastWriteTimeUtc { get; set; }

    }
    internal interface ILocalContentFile : ILocalFile, IArchivable
    {
        FileInfo PointerFileInfo { get; }
    }

    internal interface IPointerFile/*<TObject>*/ : ILocalFile, IArchivable
    {
        //TObject GetObject();
        //string GetObjectName();
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
        public void Upload<T>(IEnumerable<T> filesToUpload, string remoteDirectoryName) where T : ILocalFile;
        void Download(IEnumerable<IBlob> blobsToDownload, DirectoryInfo target);
        void Download(string remoteDirectoryName, DirectoryInfo target);
    }



    internal interface IRepository
    {
        string FullName { get; }
    }
    //internal interface IRepository<T> : IRepository where T : IItem //TODO Refactor all IRepositoruy
    //{
    //    T GetById(HashValue id);
    //    IEnumerable<T> GetAll(Expression<Func<T, bool>> filter = null);

    //    void Put(T entity);
    //    void PutAll(IEnumerable<T> entities);
    //}
    //internal interface IRemoteRepository<out TRemote, in TLocal> : IRepository where TRemote : IBlob where TLocal : ILocalFile
    //{
    //    TRemote GetById(HashValue id);
    //    IEnumerable<TRemote> GetAll();

    //    void Put(TLocal entity);
    //    void PutAll(IEnumerable<TLocal> entities);
    //}

    //internal interface IRepository<T> : IRepository<T, T> where T : IItem
    //{
    //}
    //internal interface IRepository<TEntity> where TEntity : class
    //{
    //    //void Delete(TEntity entityToDelete);
    //    //void Delete(object id);
    //    IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : class, TEntity;
    //    //IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>> filter = null);
    //    TEntity GetByID(object id);
    //    //IEnumerable<TEntity> GetWithRawSql(string query, params object[] parameters);
    //    void Add(TEntity entity);

    //    void Add(IEnumerable<TEntity> entities);
    //    //void Update(TEntity entityToUpdate);
    //}

    //internal interface ILocalRepository<TEntity> : IRepository<TEntity> where TEntity : class, ILocalFile
    //{
    //    DirectoryInfo Root { get; }
    //}
    internal interface IRepository<out TGet, in TPut> : IRepository where TGet : IItem where TPut : IItem
    {
        TGet GetById(HashValue id);
        IEnumerable<TGet> GetAll();

        void Put(TPut entity);
        void PutAll(IEnumerable<TPut> entities);
    }


    internal interface IManifestService : IRepository<IManifestFile, IArchivable>
    {
        IManifestFile Create(IEnumerable<IRemoteEncryptedChunkBlob> encryptedChunks, IEnumerable<ILocalContentFile> localContentFile);
    }
    internal interface IPointerService : IRepository<IPointerFile, IPointerFile>
    {
    }

   

    internal interface ILocalRepository : IRepository<IArchivable, IArchivable> //, IPointerService
    {
    }

    internal interface IRemoteRepository : IRepository<ILocalFile, IArchivable>
    {
    }
}
