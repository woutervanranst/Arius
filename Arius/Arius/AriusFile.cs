using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Arius
{
    class AriusFile
    {
        //public AriusFile(string rootDirectory)
        //{

        //}

        public AriusFile(DirectoryInfo root, string relativePath)
        {
            if (!relativePath.EndsWith(".arius"))
                throw new ArgumentException($"{nameof(relativePath)} not an .arius file");

            _root = root;
            _fi = new FileInfo(Path.Combine(root.FullName, relativePath));
        }
        private readonly DirectoryInfo _root;
        private readonly FileInfo _fi;


        public void CreatePointer(string contentBlobName)
        {
            AriusFile.CreatePointer(_fi.FullName, contentBlobName);
        }





        public static string GetLocalContentName(string relativeName)
        {
            //Ref https://stackoverflow.com/questions/5650909/regex-for-extracting-certain-part-of-a-string

            var match = Regex.Match(relativeName, "^(?<relativeName>.*).arius$");
            return match.Groups["relativeName"].Value;
        }

        public static void CreatePointer(string localPointerFullName, string contentBlobName)
        {
            //TODO met directory enzo

            if (!localPointerFullName.EndsWith(".arius"))
                throw new ArgumentException($"{nameof(localPointerFullName)} not an .arius file");

            var fi = new FileInfo(localPointerFullName);
            if (!fi.Directory.Exists)
                fi.Directory.Create();

            File.WriteAllText(localPointerFullName, contentBlobName, Encoding.UTF8);
        }
    }
}
