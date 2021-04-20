using Arius.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Extensions
{
    static class FileInfoExtensions
    {
        public static bool IsPointerFile(this FileInfo fi) => fi.Name.EndsWith(PointerFile.Extension, StringComparison.CurrentCultureIgnoreCase);
    }
}
