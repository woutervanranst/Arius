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
    /// Een Arius file met manifest en chunks
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

            //var p = eamf.CreatePointerFile(lcf);

            return new EncryptedAriusContent(lcf, eamf, eacs);
        }

        
        public EncryptedAriusContent(LocalContentFile lcf, EncryptedAriusManifestFile encryptedManifest, EncryptedAriusChunk[] encryptedChunks)
        {
            LocalContentFile = lcf;
            //PointerFile = pointerFile;
            EncryptedManifestFile = encryptedManifest;
            EncryptedAriusChunks = encryptedChunks;
        }

        public LocalContentFile LocalContentFile { get; }
        //public AriusPointerFile PointerFile { get; }
        public EncryptedAriusManifestFile EncryptedManifestFile { get; }
        public EncryptedAriusChunk[] EncryptedAriusChunks { get; }

    }
    
}
