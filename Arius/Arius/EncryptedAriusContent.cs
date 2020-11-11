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
    /// Een Arius file met manifest en chunks //TODO Waarom bestaat deze class? volgens mij kan die weg - is gewoon een empty shell. de commands in CCreate draaien gewoon op lcf
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

            var p = eamf.CreatePointerFile(lcf); //TODO voor consistency de pointer hier buiten aanmaken / eg na de encryptedAriusContentsToUpload call?

            return new EncryptedAriusContent(lcf, eamf, eacs);
        }

        
        public EncryptedAriusContent(LocalContentFile lcf, EncryptedAriusManifestFile encryptedManifest, EncryptedAriusChunk[] encryptedChunks)
        {
            LocalContentFile = lcf;
            //PointerFile = pointerFile; //NOTE dit kunnnen er meerdere zijn
            EncryptedManifestFile = encryptedManifest;
            EncryptedAriusChunks = encryptedChunks;
        }

        public LocalContentFile LocalContentFile { get; }
        //public AriusPointerFile PointerFile { get; } //NOTE dit kunnnen er meerdere zijn
        public EncryptedAriusManifestFile EncryptedManifestFile { get; }
        public EncryptedAriusChunk[] EncryptedAriusChunks { get; }

    }
    
}
