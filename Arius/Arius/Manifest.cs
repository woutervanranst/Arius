﻿using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Arius
{
    class Manifest
    {
        public static Manifest CreateManifest(string contentBlobName, string relativeFileName)
        {
            var manifest = new Manifest(contentBlobName, new List<ManifestEntry>());
            manifest.AddEntry(relativeFileName);

            return manifest;
        }

        public static Manifest GetManifest(BlobUtils blobUtils, SevenZipUtils sevenZipUtils, string contentBlobName, string passphrase)
        {
            var m = new Manifest(contentBlobName);

            var tempFileName1 = Path.GetTempFileName();
            blobUtils.Download(m.EncryptedManifestBlobName, tempFileName1);

            var tempFileName2 = Path.GetTempFileName();
            sevenZipUtils.DecryptFile(tempFileName1, tempFileName2, passphrase);
            File.Delete(tempFileName1);

            var json = File.ReadAllText(tempFileName2);
            File.Delete(tempFileName2);

            m._entries = JsonSerializer.Deserialize<List<ManifestEntry>>(json);

            return m;
        }

        private List<ManifestEntry> _entries;


        private Manifest(string contentBlobName, List<ManifestEntry> entries = null)
        {
            ContentBlobName = contentBlobName;
            _entries = entries ?? new List<ManifestEntry>();
        }

        public IEnumerable<ManifestEntry> Entries => _entries.Count == 0 ? throw new ArgumentException("Entries not initialized") : _entries;
        public string ContentBlobName { get; private set; }
        public string ManifestBlobName => $"{ContentBlobName}.manifest";
        public string EncryptedManifestBlobName => $"{ContentBlobName}.manifest.7z.arius";

        public void AddEntry(string relativeFileName, bool isDeleted = false)
        {
            _entries.Add(new ManifestEntry { RelativeFileName = relativeFileName, DateTime = DateTime.UtcNow, IsDeleted = isDeleted });
        }

        public void Upload(BlobUtils blobUtils, SevenZipUtils sevenZipUtils, string passphrase)
        {
            var tempManifestFileName = Path.Combine(Path.GetTempPath(), ManifestBlobName);
            var json = JsonSerializer.Serialize(_entries);
            File.WriteAllText(tempManifestFileName, json);

            var tempEncryptedManifestFileName = Path.GetTempFileName();

            sevenZipUtils.EncryptFile(tempManifestFileName, tempEncryptedManifestFileName, passphrase);
            File.Delete(tempManifestFileName);

            blobUtils.Upload(tempEncryptedManifestFileName, EncryptedManifestBlobName, AccessTier.Cool);
            File.Delete(tempEncryptedManifestFileName);
        }
    }

    class ManifestEntry
    {
        public string RelativeFileName { get; set; }
        public DateTime DateTime { get; set; }
        public bool IsDeleted { get; set; }
    }
}
