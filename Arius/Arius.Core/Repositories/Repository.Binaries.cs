//using Arius.Core.Extensions;
//using Arius.Core.Models;
//using Azure;
//using Azure.Storage.Blobs;
//using Azure.Storage.Blobs.Models;
//using Azure.Storage.Blobs.Specialized;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using System;
//using System.Collections.Generic;
//using System.IO.Compression;
//using System.Linq;
//using System.Net;
//using System.Text.Json;
//using System.Threading.Tasks;

//namespace Arius.Core.Repositories;

//internal partial class Repository
//{
//    public BinaryRepository Binaries { get; init; }

//    internal class BinaryRepository
//    {
//        private readonly ILogger<Repository>                                   logger;
//        private readonly Repository                                            repo;
//        private readonly BlobContainerClient                                   container;
        

//        internal BinaryRepository(Repository parent, BlobContainerClient container)
//        {
//            this.logger = parent.logger;
//            this.repo = parent;
//            this.container = container;
//        }



//        // --- BINARY PROPERTIES ------------------------------------------------

        

        

        

        

        

//        ///// <summary>
//        ///// Get all the (distinct) BinaryHashes
//        ///// </summary>
//        ///// <returns></returns>
//        //public async Task<BinaryHash[]> GetAllBinaryHashesAsync()
//        //{
//        //    await using var db = repo.GetAriusDbContext();
//        //    return await db.BinaryProperties
//        //        .Select(bp => bp.Hash)
//        //        .ToArrayAsync();
//        //    //return await db.PointerFileEntries
//        //    //    .Select(pfe => pfe.BinaryHash)
//        //    //    .Distinct()
//        //    //    .ToArrayAsync();
//        //}

//        // --- CHUNKLIST ------------------------------------------------

        
        
//    }
//}