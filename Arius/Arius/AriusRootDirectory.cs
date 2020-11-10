using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Azure.Storage.Blobs.Models;
using MoreLinq;

namespace Arius
{
    class AriusRootDirectory
    {
        public AriusRootDirectory(string path)
        {
            _root = new DirectoryInfo(path);
        }

        private readonly DirectoryInfo _root;

        public string FullName => _root.FullName;

        public IEnumerable<FileInfo> GetNonAriusFiles() => _root.GetFiles("*.*", SearchOption.AllDirectories).Where(fi => !fi.Name.EndsWith(".arius"));
        public IEnumerable<FileInfo> GetAriusFiles() => _root.GetFiles("*.arius", SearchOption.AllDirectories);

    }
}
