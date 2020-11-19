using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace Arius
{
    internal interface IITem
    {
        string FullName { get; }
        string Name { get; }
    }
    internal interface ILocalFile : IITem, IHashable
    {
        string DirectoryName { get; }
    }

    internal interface ILocalContentFile : ILocalFile
    {
    }

    internal interface IPointerFile/*<TObject>*/ : ILocalFile
    {
        //TObject GetObject();
        string GetObjectName();
    }
    internal interface IManifestFile : ILocalFile
    {
    }

    internal interface IEncryptedManifestFile : ILocalFile
    {
    }
    internal interface IChunk : IHashable, ILocalFile
    {

    }




    internal interface IBlob : IITem
    {
    }


    internal interface IRepository<T> where T : ILocalFile
    {
        T GetById(HashValue id);
        IEnumerable<T> GetAll(Expression<Func<T, bool>> filter = null);

        void Put(T entity);
        void PutAll(IEnumerable<T> entities);
    }

    //internal class ChunkRepository : IRepository<IChunk>
    //{
    //    public IChunk GetById(HashValue id)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public IEnumerable<IChunk> GetAll(Expression<Func<IChunk, bool>> filter = null)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public void Put(IChunk entity)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    public void PutAll(IEnumerable<IChunk> entities)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}





    //internal interface IUploader<T, V> where T : ILocalFile where V : IBlob //class , IChunk<T>
    //{
    //    void Upload(IEnumerable<T> filesToUpload, BlobContainerClient target);
    //    void Download(IEnumerable<V> blobsToDownload);

    //    //IEnumerable<IRemote<T>> Upload(IEnumerable<T> chunksToUpload);
    //    //IEnumerable<IBlob> Upload<V>(IEnumerable<V> chunksToUpload) where V : T;
    //    //IEnumerable<T> Download(IEnumerable<IRemote<T>> chunksToDownload);
    //}

    internal interface IBlobCopier
    {
        void Upload(IEnumerable<ILocalFile> filesToUpload, BlobContainerClient target);
        void Download(IEnumerable<IBlob> blobsToDownload);
        public void Download(string directoryName, DirectoryInfo target);

        //IEnumerable<IRemote<T>> Upload(IEnumerable<T> chunksToUpload);
        //IEnumerable<IBlob> Upload<V>(IEnumerable<V> chunksToUpload) where V : T;
        //IEnumerable<T> Download(IEnumerable<IRemote<T>> chunksToDownload);
    }











    //internal interface IRemoteBlob : IBlob
    //{
    //}


    ////[FileExtension("*.*", true)]
    //internal interface IRemoteContentBlob : IRemoteBlob, IHashable
    //{
    //}

    //[FileExtension("*.arius.manifest")]
    //internal interface IRemoteManifestBlob : IRemoteBlob
    //{

    //}







    internal interface IHashable
    {
        HashValue Hash { get; }
    }




    
    internal interface IChunker //<T> where T : ILocalContentFile
    {
        IEnumerable<IChunk> Chunk(ILocalContentFile fileToChunk);
        ILocalContentFile Merge(IEnumerable<IChunk> chunksToJoin);
    }




    internal interface IEncrypted : ILocalFile
    {

    }

    //internal interface IEncrypter<T> where T : IFile
    //{
    //    IEncrypted<V> Encrypt<V>(V fileToEncrypt, string fileName) where V : T;
    //    T Decrypt(IEncrypted<T> fileToDecrypt);
    //}

    internal interface IEncrypter
    {
        IEncrypted Encrypt(ILocalFile fileToEncrypt, string fileName);
        ILocalFile Decrypt(IEncrypted fileToDecrypt);
    }






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

    internal interface ILocalRepository : IRepository<ILocalFile>
    {

    }

    internal interface IRemoteRepository : IRepository<ILocalFile>
    {

    }
}
