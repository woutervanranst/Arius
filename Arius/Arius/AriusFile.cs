using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arius
{



    /*
         * Conventie
         *  File.Name = de naam
         *  File.FullName = met full path
         * 
         * 
         */




    /// <summary>
    /// Een bestand met .arius als extensie
    /// </summary>
    class AriusFile
    {
        public AriusFile(FileInfo fi)
        {
            //if (!fi.FullName.EndsWith(".arius"))
            //    throw new ArgumentException();

            _fi = fi;
        }
        private readonly FileInfo _fi;

        public string Name => _fi.Name;
        public string FullName => _fi.FullName;
        public string DirectoryName => _fi.DirectoryName;

    }
}
