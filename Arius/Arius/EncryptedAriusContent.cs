using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Arius
{
    /// <summary>
    /// Een Arius file met manifest en chunks
    /// </summary>
    class EncryptedAriusContent
    {
        public static EncryptedAriusContent CreateEncryptedAriusContent(LocalContentFile lcf, bool dedup, string passphrase, DirectoryInfo root) //AriusManifestFile amf, params EncryptedAriusChunk)
        {
            //DirectoryInfo tempDir = new DirectoryInfo(Path.Combine(_fi.Directory.FullName, _fi.Name + ".arius"));
            //tempDir.Create();

            var eacs = lcf
                .GetChunks(dedup)
                .AsParallel()
                    .WithDegreeOfParallelism(1)
                .Select(ac => ac.AsEncryptedAriusChunk(passphrase))
                .ToArray();

            var eamf = lcf
                .GetManifest(eacs)
                .GetAriusManifestFile(lcf.AriusManifestFullName)
                .AsEncryptedAriusManifestFile(passphrase);


            return new EncryptedAriusContent(eamf, eacs);
        }


        public EncryptedAriusContent(EncryptedAriusManifestFile eamf, EncryptedAriusChunk[] eacs)
        {
            _eamf = eamf;
            _eacs = eacs;
        }
        private readonly EncryptedAriusManifestFile _eamf;
        private readonly EncryptedAriusChunk[] _eacs;

        public void Upload()
        {

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
