using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    /// <summary>
    /// Een Arius file met pointer, manifest en chunks
    /// </summary>
    internal class EncryptedAriusContent
    {
        public static EncryptedAriusContent Create(LocalContentFile lcf, bool dedup, string passphrase, AriusRootDirectory root) //AriusManifestFile amf, params EncryptedAriusChunk)
        {
            var eacs = lcf
                .GetChunks(dedup)
                .AsParallel()
                    .WithDegreeOfParallelism(1)
                .Select(ac => ac.GetEncryptedAriusChunk(passphrase))
                .ToArray();

            var eamf = lcf
                .GetManifest(eacs)
                .CreateAriusManifestFile(lcf.AriusManifestFullName)
                .CreateEncryptedAriusManifestFile(lcf.EncryptedAriusManifestFullName, passphrase, true);

            var p = eamf.CreatePointerFile(lcf);

            return new EncryptedAriusContent(lcf, p, eamf, eacs);
        }

        
        public EncryptedAriusContent(LocalContentFile lcf, AriusPointerFile pointerFile, EncryptedAriusManifestFile encryptedManifest, EncryptedAriusChunk[] encryptedChunks)
        {
            _lcf = lcf;
            PointerFile = pointerFile;
            EncryptedManifestFile = encryptedManifest;
            _encryptedChunks = encryptedChunks;
        }

        private readonly LocalContentFile _lcf;
        private readonly EncryptedAriusChunk[] _encryptedChunks;

        public AriusPointerFile PointerFile { get; }
        public EncryptedAriusManifestFile EncryptedManifestFile { get; }

        public void Archive(AriusRemoteArchive archive, AccessTier chunkTier)
        {
            var files = _encryptedChunks.Cast<AriusFile>().Union(new[] { EncryptedManifestFile });

            archive.Archive(files, chunkTier);
        }

        public void Restore()
        {
            //var chunkFiles = chunks.Select(c => new FileStream(Path.Combine(clf.FullName, BitConverter.ToString(c.Hash)), FileMode.Open, FileAccess.Read));
            //var concaten = new ConcatenatedStream(chunkFiles);

            //var restorePath = Path.Combine(clf.FullName, "haha.exe");
            //using var fff = File.Create(restorePath);
            //concaten.CopyTo(fff);
            //fff.Close();
        }
    }

    
}
