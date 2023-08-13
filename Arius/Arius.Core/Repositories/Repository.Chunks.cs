//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Net;
//using System.Threading.Tasks;
//using Arius.Core.Extensions;
//using Arius.Core.Models;
//using Arius.Core.Services;
//using Azure;
//using Azure.Storage.Blobs;
//using Azure.Storage.Blobs.Models;
//using Azure.Storage.Blobs.Specialized;
//using Microsoft.Extensions.Logging;

//namespace Arius.Core.Repositories;

//internal partial class Repository
//{
//    public ChunkRepository Chunks { get; init; }

//    internal class ChunkRepository
//    {
        
//        private readonly ILogger<Repository> logger;
//        private readonly BlobContainerClient container;
//        private readonly string              passphrase;

//        internal ChunkRepository(Repository parent, BlobContainerClient container, string passphrase)
//        {
//            this.logger = parent.logger;
//            this.container = container;
//            this.passphrase = passphrase;
//        }


//        // GET












//    // HYDRATE




//    // DELETE




//    // UPLOAD & DOWNLOAD



//    //internal async Task DownloadAsync(ChunkBlobBase cbb, FileInfo target)
//    //{
//    //    try
//    //    {
//    //        using (var ts = target.Create())
//    //        {
//    //            throw new NotImplementedException();
//    //            //if (!await bbc.HasMetadataTagAsync(SUCCESSFUL_UPLOAD_METADATA_TAG))
//    //            //    throw new InvalidOperationException($"ChunkList '{bh}' does not have the '{SUCCESSFUL_UPLOAD_METADATA_TAG}' tag and is potentially corrupt");

//    //            using (var ss = await cbb.OpenReadAsync())
//    //            {
//    //                await CryptoService.DecryptAndDecompressAsync(ss, ts, passphrase);
//    //            }
//    //        }
//    //    }
//    //    catch (Exception e)
//    //    {
//    //        throw; //TODO
//    //    }
//    //}
//}
