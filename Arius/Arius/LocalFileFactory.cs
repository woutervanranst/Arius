using System;
using System.IO;
using System.Reflection;
using System.Xml.XPath;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    internal class LocalFileFactory
    {
        public LocalFileFactory(IHashValueProvider contentFileHasher)
        {
            _contentFileHasher = contentFileHasher;
        }

        private readonly IHashValueProvider _contentFileHasher;

        public T Create<T>(FileInfo fi, IRepository root) where T : ILocalFile
        {
            ILocalFile result;

            //Func<T, FileInfo, bool> ka = (arg1, info) => typeof(arg1).GetCustomAttribute<ExtensionAttribute>()

            if (IsMatch<LocalPointerFile>(fi))
                result = new LocalPointerFile(root, fi, lf => _contentFileHasher.GetHashValue(lf)); 
            else if (IsMatch<LocalContentFile>(fi))
                result = new LocalContentFile(root, fi, lf => _contentFileHasher.GetHashValue(lf));
            else if (IsMatch<LocalEncryptedManifestFile>(fi))
                result = new LocalEncryptedManifestFile(root, fi, null);
            else if (IsMatch<LocalManifestFile>(fi))
                result = new LocalManifestFile(root, fi, lf => new HashValue { Value = lf.NameWithoutExtension });
            else if (IsMatch<EncryptedChunkFile>(fi))
                result = new EncryptedChunkFile(root, fi, lf => new HashValue { Value = lf.NameWithoutExtension });
            else
                throw new NotImplementedException();



            



            //if (typeof(T).IsAssignableFrom(typeof(IPointerFile<V>)))
            //{

            //}

            //if (typeof(T).Name == nameof(LocalContentFile))
            //    result = new LocalContentFile(root, fi, _contentFileHasher);
            //else if (typeof(T).IsAssignableTo(typeof(LocalPointerFile)))
            //    result = new LocalPointerFile(root, fi, _contentFileHasher);
            //else
            //    throw new NotImplementedException();

            return (T)result;

        }

        protected bool IsMatch<T>(FileInfo fi)
        {
            return typeof(T).GetCustomAttribute<ExtensionAttribute>().IsMatch(fi);
        }

        //public T Create<T, V>(LocalRootDirectory root, FileInfo fi) where T : IPointerFile<V>
        //{
        //    ILocalFile result;

        //    if (typeof(T).IsAssignableFrom(typeof(IPointerFile<V>)))
        //    {

        //    }

        //    if (typeof(T).Name == nameof(IPointerFile<V>))
        //        result = new LocalPointerFile(root, fi, _contentFileHasher);
        //    else
        //        throw new NotImplementedException();

        //    return (T)result;

        //}
    }
}
