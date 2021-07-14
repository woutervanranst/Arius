using System.Collections.Generic;
using System.IO;
using Arius.Core.Models;
using Azure.Storage.Blobs.Models;

namespace Arius.Core.Services
{
    internal interface IBlobCopier
    {
        internal interface IOptions
        {
            string AccountName { get; }
            string AccountKey { get; }
            string Container { get; }
        }

        void Upload(IFile[] filesToUpload, AccessTier tier, string remoteDirectoryName, bool overwrite = false);
        IEnumerable<FileInfo> Download(BlobBase[] blobsToDownload, DirectoryInfo target, bool flatten);
    }
}