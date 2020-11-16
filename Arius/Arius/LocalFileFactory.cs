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
        //public LocalFileFactory(AriusRootDirectory root)
        //{
        //    _root = root;
        //}

        //private readonly AriusRootDirectory _root;

        public T Create<T>(LocalRootDirectory root, FileInfo fi) where T : ILocalFile
        {
            if (typeof(T).Name == typeof(IPointerFile<IManifestBlob>).Name)
            {
                //return new AriusPointerFile(_root, fi) as T;
                ILocalFile result = new AriusPointerFile(root, fi);

                return (T)result;
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
