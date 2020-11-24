using System;
using System.Reflection;
using Arius.Extensions;
using Arius.Models;
using Azure.Storage.Blobs.Models;

namespace Arius.Services
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
            return typeof(T).GetCustomAttribute<ExtensionAttribute>()!.IsMatch(bi);
        }
    }
}
