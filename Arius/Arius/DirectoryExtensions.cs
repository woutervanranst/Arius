using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius
{
    class DirectoryExtensions
    {
        public static void DeleteEmptySubdirectories(string parentDirectory)
        {
            Parallel.ForEach(Directory.GetDirectories(parentDirectory), directory => {
                DeleteEmptySubdirectories(directory);
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
            });
        }
    }

}
