using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    public interface IFile
    {
        string FullName { get; }
        //string Extension { get; }
    }

    public interface IBlob
    {
        string Name { get; }
    }

    [FileExtension("*.*", true)]
    public interface IContent : IRemoteBlob, ILocalFile
    {
    }

    public interface IManifestBlob : IRemoteBlob
    {

    }

    [FileExtension("*.arius.pointer")]
    public interface IPointerFile<T> : ILocalFile where T : IManifestBlob
    {

    }

    public interface ILocalFile : IFile
    {

    }

    public interface IRemoteBlob : IBlob
    {

    }

    //public interface IChunk<T> where T : IEnumerable<T>
    //{

    //}
    public interface IChunkedFile<T> : IFile where T : IFile
    {

    }

    public interface IChunker<T> where T : IFile
    {
        IEnumerable<T> Chunk(T fileToChunk);
        T Merge(IEnumerable<T> chunksToJoin);
    }

    public interface IEncrypted<T> where T : IFile
    {

    }

    public interface IEncrypter<T> where T : IFile
    {
        IEncrypted<T> Chunk(T fileToChunk);
        T Merge(IEnumerable<T> chunksToJoin);
    }


    public interface IRepository<TEntity> where TEntity : class
    {
        //void Delete(TEntity entityToDelete);
        //void Delete(object id);
        IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : TEntity;
        //IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>> filter = null);
        TEntity GetByID(object id);
        //IEnumerable<TEntity> GetWithRawSql(string query, params object[] parameters);
        void Insert(TEntity entity);
        void Update(TEntity entityToUpdate);
    }

    public interface ILocalRepository<TEntity> : IRepository<TEntity> where TEntity : class, ILocalFile
    {
    }

}
