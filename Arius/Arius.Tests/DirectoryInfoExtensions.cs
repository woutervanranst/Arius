using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Tests
{
    static class DirectoryInfoExtensions
    {
        /// <summary>
        /// Empty the directory
        /// </summary>
        /// <param name="dir"></param>
        public static void Clear(this DirectoryInfo dir)
        {
            if (dir.Exists) dir.Delete(true);
            dir.Create();
        }
    }
}
