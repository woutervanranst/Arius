using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arius
{
    internal class AriusPointer : AriusFile
    {
        public static AriusPointer CreateAriusPointer(string ariusPointerFullName, string manifestName)
        {
            File.WriteAllText(ariusPointerFullName, manifestName);
            return new AriusPointer(new FileInfo(ariusPointerFullName));
        }

        public AriusPointer(FileInfo fi) : base(fi)
        {
        }
    }
}
