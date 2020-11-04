using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Arius
{
    class LocalAriusFile
    {
        //public static AriusFile()
        //{

        //}

        public LocalAriusFile(DirectoryInfo root, string relativeAriusFileName, Manifest manifest)
        {
            if (!relativeAriusFileName.EndsWith(".arius"))
                throw new ArgumentException($"{nameof(relativeAriusFileName)} not an .arius file");

            _root = root;
            RelativeAriusFileName = relativeAriusFileName;
            //_fi = new FileInfo(Path.Combine(root.FullName, relativeAriusFileName));
            _m = manifest;
        }
        private readonly DirectoryInfo _root;
        //private readonly FileInfo _fi;
        private readonly Manifest _m;

        public string RelativeAriusFileName { get; private set; }
        public string AriusFileName => Path.Combine(_root.FullName, RelativeAriusFileName);
        public bool Exists => File.Exists(AriusFileName);

        //public void CreatePointer(string contentBlobName)
        //{
        //    LocalAriusFile.CreatePointer(_fi.FullName, contentBlobName);
        //}

        public void Create()
        {
            if (File.Exists(AriusFileName))
                throw new InvalidOperationException($"LocalAriusFile {AriusFileName} already exists");

            LocalAriusFile.CreatePointer(AriusFileName, _m.ContentBlobName);
        }


        public override string ToString() => RelativeAriusFileName;





        public static string GetLocalContentName(string relativeName)
        {
            //Ref https://stackoverflow.com/questions/5650909/regex-for-extracting-certain-part-of-a-string

            var match = Regex.Match(relativeName, "^(?<relativeName>.*).arius$");
            return match.Groups["relativeName"].Value;
        }

        public static void CreatePointer(string ariusFileName, string contentBlobName)
        {
            //TODO met directory enzo

            if (!ariusFileName.EndsWith(".arius"))
                throw new ArgumentException($"{nameof(ariusFileName)} not an .arius file");

            var fi = new FileInfo(ariusFileName);
            if (!fi.Directory.Exists)
                fi.Directory.Create();

            File.WriteAllText(ariusFileName, contentBlobName, Encoding.UTF8);
        }
    }
}
