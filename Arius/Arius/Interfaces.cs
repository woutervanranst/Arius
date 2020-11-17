using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    internal interface IFile
    {
        string FullName { get; }
        string Name { get; }
    }

    internal interface ILocalFile : IFile, IHashable
    {
    }

    internal interface ILocalContentFile : ILocalFile
    {
    }

    internal interface IPointerFile<TObject> : ILocalFile
    {
        TObject GetObject();
        string GetObjectName();
    }





    internal interface IBlob
    {
        string Name { get; }
    }

    internal interface IRemote<TObject> : IBlob
    {
        TObject GetRemoteObject();
    }






    internal interface IManifestFile : IFile
    {

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




    internal interface IChunk<T> : IHashable, IFile // where T : IEnumerable<T>
    {

    }
    internal interface IChunker<T> where T : class, ILocalContentFile
    {
        IEnumerable<IChunk<T>> Chunk(T fileToChunk);
        T Merge(IEnumerable<IChunk<T>> chunksToJoin);
    }




    internal interface IEncrypted<T> where T : IFile
    {

    }

    internal interface IEncrypter<T> where T : IFile
    {
        IEncrypted<T> Encrypt(T fileToEncrypt);
        T Decrypt(IEnumerable<T> fileToDecrypt);
    }


    internal interface IRepository<TEntity> where TEntity : class
    {
        //void Delete(TEntity entityToDelete);
        //void Delete(object id);
        IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : class, TEntity;
        //IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>> filter = null);
        TEntity GetByID(object id);
        //IEnumerable<TEntity> GetWithRawSql(string query, params object[] parameters);
        void Insert(TEntity entity);
        void Update(TEntity entityToUpdate);
    }

    internal interface ILocalRepository<TEntity> : IRepository<TEntity> where TEntity : class, ILocalFile
    {
        DirectoryInfo Root { get; }
    }

}
