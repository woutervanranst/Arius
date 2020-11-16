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

        public T Create<T>(LocalRootDirectory root, FileInfo fi) where T : ILocalFile
        {
            ILocalFile result;

            if (typeof(T).Name == typeof(IPointerFile<IRemoteManifestBlob>).Name)
                result = new LocalPointerFile(root, fi, _contentFileHasher);
            else if (typeof(T).Name == nameof(ILocalContentFile))
                result = new LocalContentFile(root, fi, _contentFileHasher);
            else
                throw new NotImplementedException();

            return (T)result;

        }
    }
}
