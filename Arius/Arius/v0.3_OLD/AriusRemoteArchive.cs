//using Azure.Storage;
//using Azure.Storage.Blobs;
//using Azure.Storage.Blobs.Models;
//using Azure.Storage.Sas;
//using System;
//using System.Collections.Generic;
//using System.Collections.Immutable;
//using System.IO;
//using System.Linq;
//using System.Runtime.InteropServices;
//using Microsoft.Extensions.Azure;

//namespace Arius
//{
//    internal class AriusRemoteArchive
//    {
//        static AriusRemoteArchive()
//        {
//            /*

//            AzCopyPath = @"C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\Common7\IDE\CommonExtensions\Microsoft\ModelBuilder\AzCopyService\azcopy.exe";
//        }

//        private static readonly string AzCopyPath;


//        public bool Exists(string file)
//        {
//            return _bcc.GetBlobClient(file).Exists();
//        }


//        


//        


//        //public void Download(string blobName, string fileName)
//        //{
//        //    var bc = _bcc.GetBlobClient(blobName);

//        //    bc.DownloadTo(fileName);
//        //}


//        public IEnumerable<RemoteEncryptedAriusManifest> GetRemoteEncryptedAriusManifests()
//        {
//            return _bcc.GetBlobs()
//                .Where(b => b.Name.EndsWith(".manifest.7z.arius"))
//                .Select(bi => new RemoteEncryptedAriusManifest(this, bi)); //TODO extract "validators" to some other class?
//        }

//        public IEnumerable<RemoteEncryptedAriusChunk> GetRemoteEncryptedAriusChunks()
//        {
//            return _bcc.GetBlobs()
//                .Where(b => b.Name.EndsWith(".7z.arius") && !b.Name.EndsWith(".manifest.7z.arius"))
//                .Select(bi => new RemoteEncryptedAriusChunk(this, bi));
//        }

//        public RemoteEncryptedAriusChunk GetRemoteEncryptedAriusChunk(string hash)
//        {
//            var bi = GetBlobItem($"{hash}.7z.arius");

//            return new RemoteEncryptedAriusChunk(this, bi);
//        }

//        public RemoteEncryptedAriusManifest GetRemoteEncryptedAriusManifestByHash(string hash)
//        {
//            var bi = GetBlobItem($"{hash}.manifest.7z.arius");

//            return new RemoteEncryptedAriusManifest(this, bi);
//        }
//        public RemoteEncryptedAriusManifest GetRemoteEncryptedAriusManifestByBlobItemName(string blobItemName)
//        {
//            var bi = GetBlobItem(blobItemName);

//            return new RemoteEncryptedAriusManifest(this, bi);
//        }

//        private BlobItem GetBlobItem(string name)
//        {
//            var bi = _bcc
//                .GetBlobs(prefix: name)
//                .SingleOrDefault(bi => bi.Name == name); //TODO test met nonexisting hash

//            if (bi is null)
//                throw new ArgumentException("NO CHUNK FOR HASH");

//            return bi;
//        }


//        public void UploadEncryptedAriusManifest(string file, string hash) //TODO Strinlgy Typed RemoteEncryptedAriusManifest : RemoteFile ?
//        {
//            var blobName = $"{hash}.manifest.7z.arius";
//            var bc = _bcc.GetBlobClient(blobName);

//            using var s = File.Open(file, FileMode.Open, FileAccess.Read);

//            if (bc.Exists())
//            {
//                bc.Upload(s, overwrite: true);
//            }
//            else
//            {
//                bc.Upload(s);
//                bc.SetAccessTier(AccessTier.Cool);
//            }
            
//            s.Close();
//        }

//        public void Download(string blobName, string file)
//        {
//            var bc = _bcc.GetBlobClient(blobName);

//            if (!bc.Exists())
//                throw new ArgumentException("CONTENT/MANIFEST DOES NOT EXIST"); //TODO

//            bc.DownloadTo(file);
//        }
//    }
//}
