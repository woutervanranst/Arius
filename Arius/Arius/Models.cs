using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    public abstract class AriusFile : IFile
    {
        public abstract string FullName { get; }
    }

    public abstract class AriusLocalFile : AriusFile, ILocalFile
    {
        protected AriusLocalFile(FileInfo fi)
        {
            _fi = fi;
        }

        private readonly FileInfo _fi;

        public override string FullName => _fi.FullName;
    }

    internal class AriusPointerFile : AriusLocalFile, IPointerFile<IManifestBlob>, ILocalFile
    {
        public AriusPointerFile(LocalRootDirectory root, FileInfo fi) : base(fi)
        {
            if (!fi.Exists)
                throw new ArgumentException("The Pointer file does not exist");
        }
    }

    //public class Manifest : IManifest
    //{
    //    public string Name { get; }
    //}
}
