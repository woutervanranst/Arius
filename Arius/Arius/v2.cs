using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

    [AriusFileExtension("*.*", true)]
    public interface IContent : IRemote, ILocal
    {
    }

    public interface IManifest : IRemote
    {

    }

    [AriusFileExtension("*.arius.pointer")]
    public interface IPointer<T> : ILocal where T : IManifest
    {

    }

    

    public abstract class AriusFile : IFile
    {
        public abstract string FullName { get; }
    }
         
    public abstract class AriusLocalFile : AriusFile, ILocal
    {
        protected AriusLocalFile(FileInfo fi)
        {
            _fi = fi;
        }

        private readonly FileInfo _fi;

        public override string FullName => _fi.FullName;
    }

    internal class AriusPointerFile : AriusLocalFile, IPointer<IManifest>, ILocal
    {
        public AriusPointerFile(AriusRootDirectory root, FileInfo fi) : base(fi)
        {
            if (!fi.Exists)
                throw new ArgumentException("The Pointer file does not exist");
        }
    }

    //public class Manifest : IManifest
    //{
    //    public string Name { get; }
    //}





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
        IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : TEntity;
        //IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>> filter = null);
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


    internal interface IAriusRootDirectoryOptions : ICommandExecutorOptions
    {
        public string Path { get; set; }
    }

    internal class AriusRootDirectory : ILocalRepository<ILocal>
    {
        public AriusRootDirectory(ICommandExecutorOptions options, LocalFileFactory factory)
        {
            var root = ((IAriusRootDirectoryOptions)options).Path;
            _root = new DirectoryInfo(root);
            _factory = factory;
        }

        private readonly DirectoryInfo _root;
        private readonly LocalFileFactory _factory;

        public IEnumerable<T> Get<T>(Expression<Func<T, bool>> filter = null) where T : ILocal
        {
            var attr = typeof(T).GetCustomAttribute<AriusFileExtensionAttribute>();
            var localFiles = AriusFileExtensionAttribute.GetFilesWithExtension(_root, attr).Select(fi => _factory.Create<T>(this, fi));

            return localFiles;
        }

        public ILocal GetByID(object id)
        {
            throw new NotImplementedException();
        }

        public void Insert(ILocal entity)
        {
            throw new NotImplementedException();
        }

        public void Update(ILocal entityToUpdate)
        {
            throw new NotImplementedException();
        }
    }


    internal class LocalFileFactory
    {
        //public LocalFileFactory(AriusRootDirectory root)
        //{
        //    _root = root;
        //}

        //private readonly AriusRootDirectory _root;

        public T Create<T>(AriusRootDirectory root, FileInfo fi) where T : ILocal
        {
            if (typeof(T).Name == typeof(IPointer<IManifest>).Name)
            {
                //return new AriusPointerFile(_root, fi) as T;
                ILocal result = new AriusPointerFile(root, fi);

                return (T) result;
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }

    //public class Kak
    //{
    //    public void Ha()
    //    {
    //        var x = new AriusRootDirectory(new DirectoryInfo(@"c:\"));

    //        var content = x.Get<IPointer<IManifest>>();
    //        //x.Get(x => x.FullName.EndsWith() == )

    //    }
    //}
}

public static class Helpers
{
    public static bool Implements<I>(this Type type, I @interface) where I : class
    {
        // https://stackoverflow.com/questions/503263/how-to-determine-if-a-type-implements-a-specific-generic-interface-type


        if (((@interface as Type) == null) || !(@interface as Type).IsInterface)
            throw new ArgumentException("Only interfaces can be 'implemented'.");

        return (@interface as Type).IsAssignableFrom(type);
    }
}

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public class AriusFileExtensionAttribute : Attribute
{
    public AriusFileExtensionAttribute(string extension, bool excludeOthers = false)
    {
        Extension = extension;
        ExcludeOthers = excludeOthers;
    }
    public string Extension { get; init; }
    public bool ExcludeOthers { get; init; }

    public static FileInfo[] GetFilesWithExtension(DirectoryInfo dir, AriusFileExtensionAttribute attr)
    {
        var otherExtensions = Assembly.GetExecutingAssembly().GetCustomAttributes<AriusFileExtensionAttribute>().Select(attr => attr.Extension);
        return dir.GetFiles(attr.Extension)
            .Where(fi => !attr.ExcludeOthers || 
                         otherExtensions.Any(extToExclude => !fi.Name.EndsWith(extToExclude))).ToArray();
    }
}