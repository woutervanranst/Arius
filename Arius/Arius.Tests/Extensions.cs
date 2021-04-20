using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Arius.Models;

namespace Arius.Tests
{
    static class Extensions
    {
        

        public static FileInfo GetPointerFileInfo(this FileInfo localContentFileFileInfo)
        {
            return new FileInfo(localContentFileFileInfo.FullName + PointerFile.Extension);
        }
    }
}
