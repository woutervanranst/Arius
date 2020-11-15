using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Arius.V4
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

    public interface IContent : IRemote, ILocal
    {
    }

    public interface IManifest : IRemote
    {

    }

    public interface IPointer<T> : ILocal where T : IManifest
    {

    }

    public abstract class AriusFile : IFile
    {
        protected AriusFile(FileInfo fi)
        {
            _fi = fi;
        }

        private readonly FileInfo _fi;

        public string FullName => _fi.FullName;
    }

    

    public interface ILocal : IFile
    {

    }

    public interface IRemote : IBlob
    {

    }

    //public interface IChunk<T> where T : IEnumerable<T>
    //{

    //}
    public interface IChunked<T> : IFile where T : IFile
    {

    }

    //public class Ha<T> : IChunked<IChunk<T>>
    //{

    //}

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
        //IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : TEntity;
        IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>> filter = null);
        TEntity GetByID(object id);
        //IEnumerable<TEntity> GetWithRawSql(string query, params object[] parameters);
        void Insert(TEntity entity);
        void Update(TEntity entityToUpdate);
    }

    public interface ILocalRepository<TEntity> : IRepository<TEntity> where TEntity : class, ILocal
    {
    }

    //public class RemoteArchive : IRepository<IEncrypted<IChunked<IContent>>>, IRepository<IEncrypted<IManifest>>
    //{
    //    IEnumerable<IEncrypted<IChunked<IContent>>> IRepository<IEncrypted<IChunked<IContent>>>.Get(Expression<Func<IEncrypted<IChunked<IContent>>, bool>> filter)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    IEnumerable<IEncrypted<IManifest>> IRepository<IEncrypted<IManifest>>.Get(Expression<Func<IEncrypted<IManifest>, bool>> filter)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    IEncrypted<IChunked<IContent>> IRepository<IEncrypted<IChunked<IContent>>>.GetByID(object id)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    IEncrypted<IManifest> IRepository<IEncrypted<IManifest>>.GetByID(object id)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    void IRepository<IEncrypted<IChunked<IContent>>>.Insert(IEncrypted<IChunked<IContent>> entity)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    void IRepository<IEncrypted<IManifest>>.Insert(IEncrypted<IManifest> entity)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    void IRepository<IEncrypted<IChunked<IContent>>>.Update(IEncrypted<IChunked<IContent>> entityToUpdate)
    //    {
    //        throw new NotImplementedException();
    //    }

    //    void IRepository<IEncrypted<IManifest>>.Update(IEncrypted<IManifest> entityToUpdate)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

    public class LocalRootFolder : ILocalRepository<IContent>, ILocalRepository<IPointer<IManifest>>
    {
        public LocalRootFolder(DirectoryInfo root)
        {

        }

        private readonly DirectoryInfo _root;

        //public IEnumerable<ILocal> Get<T>(Expression<Func<ILocal, bool>> filter = null) where T : ILocal
        //{
        //    Type.GetTypeCode(T)
        //    var x = _root.GetFiles(default(T)!.Extension)

        //    return null;
        //}

        //public IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null)
        //{
        //    throw new NotImplementedException();
        //}

        //public IEnumerable<TContent> Get<TContent>(Expression<Func<TContent, bool>> filter = null)
        //{
        //    var x = _root.GetFiles(); //(default(T)!.Extension)

        //    return null;
        //}
        //public IEnumerable<IPointer<IManifest>> Get<TPointer>(Expression<Func<IPointer<IManifest>, bool>> filter = null)
        //{
        //    var x = _root.GetFiles(); //(default(T)!.Extension)

        //    return null;
        //}

        //public ILocal GetByID(object id)
        //{
        //    throw new NotImplementedException();
        //}

        //public void Insert(ILocal entity)
        //{
        //    throw new NotImplementedException();
        //}

        //public void Update(ILocal entityToUpdate)
        //{
        //    throw new NotImplementedException();
        //}

        //public IEnumerable<X> Get<X>(Expression<Func<X, bool>> filter = null) where X : ILocal
        //{
        //    throw new NotImplementedException();
        //}
        //public IEnumerable<X2> Get<X2>(Expression<Func<X2, bool>> filter = null) where X2 : IContent
        //{
        //    throw new NotImplementedException();
        //}
        //public IEnumerable<T> Get<T>(Expression<Func<ILocal, bool>> filter = null) where T : ILocal
        //{


        //    foreach (object obj in source)
        //    {
        //        if (obj is TResult result)
        //            yield return result;
        //    }
        //}


        //    throw new NotImplementedException();
        //}

        //public IEnumerable<ILocal> Get(Expression<Func<ILocal, bool>> filter = null)
        //{
        //    throw new NotImplementedException();
        //}


        //public ILocal GetByID(object id)
        //{
        //    throw new NotImplementedException();
        //}

        //public void Insert(ILocal entity)
        //{
        //    throw new NotImplementedException();
        //}

        //public void Update(ILocal entityToUpdate)
        //{
        //    throw new NotImplementedException();
        //}


        public IEnumerable<IContent> Get(Expression<Func<IContent, bool>> filter = null)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IPointer<IManifest>> Get(Expression<Func<IPointer<IManifest>, bool>> filter = null)
        {
            throw new NotImplementedException();
        }

        IPointer<IManifest> IRepository<IPointer<IManifest>>.GetByID(object id)
        {
            throw new NotImplementedException();
        }

        public void Insert(IPointer<IManifest> entity)
        {
            throw new NotImplementedException();
        }

        public void Update(IPointer<IManifest> entityToUpdate)
        {
            throw new NotImplementedException();
        }

        IContent IRepository<IContent>.GetByID(object id)
        {
            throw new NotImplementedException();
        }

        public void Insert(IContent entity)
        {
            throw new NotImplementedException();
        }

        public void Update(IContent entityToUpdate)
        {
            throw new NotImplementedException();
        }
    }


    public class Kak
    {
        void Ha()
        {
            var x = new LocalRootFolder(new DirectoryInfo(@"c:\"));
            //x.Get(x => x.FullName.EndsWith() == )

        }
    }
}
