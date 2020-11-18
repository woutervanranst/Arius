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
    class RemoteBlobFactory
    {
        public IBlob Create(/*RemoteContainerRepository archive, */string blobItemName)/* where T : IRemote<IBlob> // where V : T*/
        {
            IBlob result;

            if (typeof(RemoteEncryptedContentBlob).GetCustomAttribute<ExtensionAttribute>().IsMatch(blobItemName))
                result = new RemoteEncryptedContentBlob(blobItemName);
            else if (typeof(RemoteEncryptedManifestBlob).GetCustomAttribute<ExtensionAttribute>().IsMatch(blobItemName))
                result = new RemoteEncryptedManifestBlob(blobItemName);
            else
                throw new NotImplementedException();

            return /*(T)*/result;
        }
    }
}
