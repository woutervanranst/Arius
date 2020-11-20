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
        public T Create<T>(BlobItem bi, IRepository root) where T : IRemoteBlob
        {
            IRemoteBlob result;

            if (IsMatch<RemoteEncryptedChunkBlob>(bi))
                result = new RemoteEncryptedChunkBlob(root, bi, b => new HashValue { Value = b.NameWithoutExtension });
            else
                throw new NotImplementedException();

            return (T)result;
        }

        protected bool IsMatch<T>(BlobItem bi)
        {
            return typeof(T).GetCustomAttribute<ExtensionAttribute>().IsMatch(bi);
        }
    }
}
