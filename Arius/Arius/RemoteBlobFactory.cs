using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;

namespace Arius
{
    internal class RemoteBlobFactory
    {
        //public IBlob Create(/*RemoteContainerRepository archive, */string blobItemName)/* where T : IRemote<IBlob> // where V : T*/
        //{
        //    IBlob result;

        //    if (typeof(RemoteEncryptedChunkBlob).GetCustomAttribute<ExtensionAttribute>().IsMatch(blobItemName))
        //        result = new RemoteEncryptedChunkBlob(blobItemName);
        //    else if (typeof(RemoteEncryptedManifestBlob).GetCustomAttribute<ExtensionAttribute>().IsMatch(blobItemName))
        //        result = new RemoteEncryptedManifestBlob(blobItemName);
        //    else
        //        throw new NotImplementedException();

        //    return /*(T)*/result;
        //}

        public T Create<T>(BlobItem bi, IRepository root) where T : IRemoteBlob
        {
            IRemoteBlob result;

            //Func<T, FileInfo, bool> ka = (arg1, info) => typeof(arg1).GetCustomAttribute<ExtensionAttribute>()

            //if (IsMatch<LocalPointerFile>(fi))
            //    result = new LocalPointerFile(root, fi, lf => _contentFileHasher.GetHashValue(lf));
            //else if (IsMatch<LocalContentFile>(fi))
            //    result = new LocalContentFile(root, fi, lf => _contentFileHasher.GetHashValue(lf));
            //else if (IsMatch<LocalEncryptedManifestFile>(fi))
            //    result = new LocalEncryptedManifestFile(root, fi, null);
            //else if (IsMatch<LocalManifestFile>(fi))
            result = new RemoteEncryptedChunkBlob(root, bi, b => new HashValue { Value = b.NameWithoutExtension });
            //else
            //    throw new NotImplementedException();

            return (T)result;
        }
    }
}
