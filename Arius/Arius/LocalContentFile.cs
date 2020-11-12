﻿using System;
using System.IO;

namespace Arius
{
    /// <summary>
    /// Het gewone bestand met content erin
    /// </summary>
    internal class LocalContentFile : IUnencryptedChunk
    {
        public LocalContentFile(AriusRootDirectory root, FileInfo localContent, string hashSalt)
        {
            _root = root;
            _localContent = localContent;
            _hash = new Lazy<string>(() => FileUtils.GetHash(hashSalt, _localContent.FullName));
        }
        private readonly AriusRootDirectory _root;
        private readonly FileInfo _localContent;
        private readonly Lazy<string> _hash;

        public IUnencryptedChunk[] GetChunks(bool dedup)
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
                return new IUnencryptedChunk[] { this };
            }
        }

        /// <summary>
        /// Get this LocalContentFile (in it s entirety) as an EncryptedAriusChunk (ie when not deduping)
        /// IUnencryptedChunk implementation
        /// </summary>
        /// <param name="passphrase"></param>
        /// <returns></returns>
        public EncryptedAriusChunk GetEncryptedAriusChunk(string passphrase)
        {
            return EncryptedAriusChunk.GetEncryptedAriusChunk(this, passphrase, false);
        }

        /// <summary>
        /// The Hash of the unencrypted file
        /// </summary>
        public string Hash => _hash.Value;
        public string FullName => _localContent.FullName;
        public string DirectoryName => _localContent.DirectoryName;
        public string RelativeName => Path.GetRelativePath(_root.FullName, FullName);
        //public string AriusManifestName => $"{Hash}.manifest.arius"; //TODO NOG GEBRUIKT?
        //public string EncryptedAriusManifestName => $"{Hash}.manifest.7z.arius"; //TODO NOG GEBRUIKT?
        //public string AriusManifestFullName => Path.Combine(DirectoryName, AriusManifestName); //TODO NOG GEBRUIKT?
        //public string EncryptedAriusManifestFullName => Path.Combine(DirectoryName, EncryptedAriusManifestName); //TODO NOG GEBRUIKT?
        public string AriusPointerFileFullName => $"{FullName}.arius";
        public FileInfo AriusPointerFileInfo => new FileInfo(AriusPointerFileFullName);
        public DateTime CreationTimeUtc => _localContent.CreationTimeUtc;
        public DateTime LastWriteTimeUtc => _localContent.LastWriteTimeUtc;

        public override string ToString() => RelativeName;
    }


}
