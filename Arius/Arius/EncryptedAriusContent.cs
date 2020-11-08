using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace Arius
{
    /// <summary>
    /// Een Arius file met manifest en chunks
    /// </summary>
    internal class EncryptedAriusContent
    {
        public static EncryptedAriusContent CreateAriusContentFile(LocalContentFile lcf, bool dedup, string passphrase, DirectoryInfo root) //AriusManifestFile amf, params EncryptedAriusChunk)
        {
            var eacs = lcf
                .GetChunks(dedup)
                .AsParallel()
                    .WithDegreeOfParallelism(1)
                .Select(ac => ac.GetEncryptedAriusChunk(passphrase))
                .ToArray();

            var eamf = lcf
                .GetManifest(eacs)
                .GetAriusManifestFile(lcf.AriusManifestFullName)
                .AsEncryptedAriusManifestFile(passphrase, true);

            var p = lcf.GetPointer();

            return new EncryptedAriusContent(lcf, p, eamf, eacs);
        }

        
        public EncryptedAriusContent(LocalContentFile lcf, AriusPointer pointer, EncryptedAriusManifestFile eamf, EncryptedAriusChunk[] eacs)
        {
            _lcf = lcf;
            _pointer = pointer;
            _eamf = eamf;
            _eacs = eacs;
        }

        private readonly LocalContentFile _lcf;
        private readonly AriusPointer _pointer;
        private readonly EncryptedAriusManifestFile _eamf;
        private readonly EncryptedAriusChunk[] _eacs;

        public void Upload(BlobUtils bu)
        {
            //var x = _eacs.Cast<AriusFile>(); //.Union((IEnumerable<AriusFile>)_eamf);

            bu.Upload(_eacs);
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
