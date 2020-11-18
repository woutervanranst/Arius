using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;

namespace Arius
{
    internal class LocalFileFactory
    {
        public LocalFileFactory(IHashValueProvider contentFileHasher)
        {
            _contentFileHasher = contentFileHasher;
        }

        private readonly IHashValueProvider _contentFileHasher;

        public T Create<T>(ILocalRepository<ILocalFile> root, FileInfo fi) where T : ILocalFile
        {
            ILocalFile result;

            if (typeof(LocalPointerFile).GetCustomAttribute<ExtensionAttribute>().IsMatch(fi))
                result = new LocalPointerFile(root, fi, _contentFileHasher); 
            else if (typeof(LocalContentFile).GetCustomAttribute<ExtensionAttribute>().IsMatch(fi))
                result = new LocalContentFile(root, fi, _contentFileHasher);
            else if (typeof(EncryptedLocalContentFile).GetCustomAttribute<ExtensionAttribute>().IsMatch(fi))
                result = new EncryptedLocalContentFile(root, fi, _contentFileHasher);
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
