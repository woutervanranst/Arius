//using Azure.Storage.Blobs;
//using SevenZip;
//using System;
//using System.Collections.Generic;
//using System.Collections.Immutable;
//using System.IO;
//using System.Linq;
//using System.Text.Json;
//using System.Text.Json.Serialization;
//using Azure.Storage.Blobs.Models;

//namespace Arius
//{
//    internal class RemoteEncryptedAriusManifest : RemoteAriusFile
//    {
//        /// <summary>
//        /// Create a New Manifest & upload it
//        /// </summary>
//        public static RemoteEncryptedAriusManifest Create(string localContentFileHash, IEnumerable<RemoteEncryptedAriusChunk> chunks, AriusRemoteArchive archive, string passphrase)
//        {
//            //if (lcfs.Select(lcf => lcf.Hash).Distinct().Count() > 1)
//            //    throw new ArgumentException(
//            //        "The specified LocalContentFiles have different hashes and do not belong to the same manifest");

//            //TODO manifest does not yet exit remte
//            //if (archive.GetRemoteEncryptedAriusManifestFileByHash(lcfs.First().Hash)

//            var manifest = new AriusManifest(chunks.Select(c => c.Name), localContentFileHash);
//            var remoteManifest = manifest.Create(archive, passphrase);

//            return remoteManifest;
//        }

        
//        public IEnumerable<string> GetEncryptedChunkNames(string passphrase)
//        {
//            var manifest = AriusManifest.FromRemote(this, passphrase);
//            return manifest.EncryptedChunks;
//        }



//}