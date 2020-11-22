using System;
using System.IO;
using System.Reflection;
using Arius.Extensions;
using Arius.Models;
using Arius.Repositories;

namespace Arius.Services
{
    internal class LocalFileFactory
    {
        public LocalFileFactory(IHashValueProvider contentFileHasher)
        {
            _contentFileHasher = contentFileHasher;
        }

        private readonly IHashValueProvider _contentFileHasher;

        public ILocalFile Create(FileInfo fi, IRepository root)
        {
            if (IsMatch<LocalPointerFile>(fi))
                return new LocalPointerFile((LocalRootRepository)root, fi, lf => new HashValue { Value = File.ReadAllText(lf.FullName) }); 
            else if (IsMatch<LocalContentFile>(fi))
                return new LocalContentFile(root, fi, lf => _contentFileHasher.GetHashValue(lf));

            else if (IsMatch<EncryptedChunkFile>(fi))
                return new EncryptedChunkFile(root, fi, lf => new HashValue { Value = lf.NameWithoutExtension });

            else if (IsMatch<LocalManifestFile>(fi))
                return new LocalManifestFile(root, fi, lf => new HashValue { Value = lf.NameWithoutExtension });
            else if (IsMatch<LocalEncryptedManifestFile>(fi))
                return new LocalEncryptedManifestFile(root, fi, null);

            else
                throw new NotImplementedException();
        }

        protected bool IsMatch<T>(FileInfo fi)
        {
            return typeof(T).GetCustomAttribute<ExtensionAttribute>()!.IsMatch(fi);
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
