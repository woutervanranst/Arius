using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arius
{
    /// <summary>
    /// Het gewone bestand met content erin
    /// </summary>
    internal class LocalContentFile : IChunk
    {
        public LocalContentFile(DirectoryInfo root, FileInfo localContent)
        {
            _root = root;
            _localContent = localContent;
            _hash = new Lazy<string>(() => FileUtils.GetHash(_localContent.FullName));
        }
        private readonly DirectoryInfo _root;
        private readonly FileInfo _localContent;
        private readonly Lazy<string> _hash;

        public EncryptedAriusContent CreateAriusContentFile(bool dedup, string passphrase, DirectoryInfo root)
        {
            return EncryptedAriusContent.CreateAriusContentFile(this, dedup, passphrase, root);
        }

        public IChunk[] GetChunks(bool dedup)
        {
            if (dedup)
            {
                throw new NotImplementedException();

                //var sb = new StreamBreaker();

                //using var fs = new FileStream(_fi.FullName, FileMode.Open, FileAccess.Read);
                //var chunks = sb.GetChunks(fs, fs.Length, SHA256.Create()).ToImmutableArray();
                //fs.Position = 0;

                //DirectoryInfo tempDir = new DirectoryInfo(Path.Combine(_fi.Directory.FullName, _fi.Name + ".arius"));
                //tempDir.Create();

                //foreach (var chunk in chunks)
                //{
                //    var chunkFullName = Path.Combine(tempDir.FullName, BitConverter.ToString(chunk.Hash));

                //    byte[] buff = new byte[chunk.Length];
                //    fs.Read(buff, 0, (int)chunk.Length);

                //    using var fileStream = File.Create(chunkFullName);
                //    fileStream.Write(buff, 0, (int)chunk.Length);
                //    fileStream.Close();
                //}

                //fs.Close();

                //var laf = new LocalAriusManifest(this);
                //var lac = chunks.Select(c => new LocalAriusChunk("")).ToImmutableArray();

                //var r = new AriusFile(this, laf, lac);

                //return r;
            }
            else
            {
                return new IChunk[] { this };
            }
        }

        internal AriusPointer GetPointer()
        {
            return AriusPointer.CreateAriusPointer(AriusPointerFullName, AriusManifestName);
        }

        public EncryptedAriusChunk GetEncryptedAriusChunk(string passphrase)
        {
            return EncryptedAriusChunk.GetEncryptedAriusChunk(this, passphrase);
        }

        public AriusManifest GetManifest(params EncryptedAriusChunk[] chunks)
        {
            return AriusManifest.CreateManifest(this, chunks);
        }

        public string Hash => _hash.Value;
        public string FullName => _localContent.FullName;
        public string DirectoryName => _localContent.DirectoryName;
        public string RelativeName => Path.GetRelativePath(_root.FullName, FullName);
        public string AriusManifestName => $"{Hash}.manifest.arius";
        public string AriusManifestFullName => Path.Combine(DirectoryName, AriusManifestName);
        public string AriusPointerFullName => $"{FullName}.arius";
        public DateTime CreationTimeUtc => _localContent.CreationTimeUtc;
        public DateTime LastWriteTimeUtc => _localContent.LastWriteTimeUtc;
    }

   
}
