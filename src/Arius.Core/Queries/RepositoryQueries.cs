using Arius.Core.Models;
using Arius.Core.Repositories;
using Arius.Core.Repositories.StateDb;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PostSharp.Patterns.Contracts;
using System.Linq.Expressions;
using Arius.Core.Facade;

namespace Arius.Core.Queries;

internal class RepositoryQueries
{
    private readonly ILoggerFactory loggerFactory;
    private readonly Repository     repository;

    public RepositoryQueries(ILoggerFactory loggerFactory, Repository repository)
    {
        this.loggerFactory = loggerFactory;
        this.repository    = repository;
    }

    public IAsyncEnumerable<(string RelativeParentPath, string DirectoryName, string Name)> GetEntriesAsync(
        string? relativeParentPathEquals = null,
        string? directoryNameEquals = null,
        string? nameContains = null)
    {
        return repository
            .GetPointerFileEntriesAsync(DateTime.Now, false, relativeParentPathEquals, directoryNameEquals, nameContains)
            .Select(pfe => (pfe.RelativeParentPath, pfe.DirectoryName, pfe.Name));
    }
}

//[NotifyPropertyChanged]
public interface IQueryFolderResponse
{
    //public enum HYDRATIONSTATE
    //{
    //    ARCHIVE,
    //    HYDRATED
    //}

    string Name { get; }
    long Length { get; }
    //HYDRATIONSTATE HydrationState { get; }
}



//[NotifyPropertyChanged]
internal record QueryFolderResponse : IQueryFolderResponse
{
    public QueryFolderResponse(string name)
    {
        this.Name = name;
    }

    public string Name { get; }

    public long                                Length         => BinaryProperties.OriginalLength; // This should work/cascade https://doc.postsharp.net/model/notifypropertychanged/inotifypropertychanged-customization
    
    internal BinaryFile       BinaryFile       { get; set; }
    internal PointerFile      PointerFile      { get; set; }
    internal PointerFileEntry PointerFileEntry { get; set; }
    internal ChunkEntry BinaryProperties { get; set; }

}

internal class RepositoryQueries2
{
    private readonly ILogger<RepositoryQueries2> logger;
    private readonly ILoggerFactory             loggerFactory;
    private readonly DirectoryInfo              root;
    private readonly Repository                 repository;
    private readonly FileSystemService          fileSystemService;
    private readonly FileService                fileService;

    public RepositoryQueries2(ILoggerFactory loggerFactory, DirectoryInfo root, Repository repository, FileSystemService fileSystemService, FileService fileService)
    {
        this.logger            = loggerFactory.CreateLogger<RepositoryQueries2>();
        this.loggerFactory     = loggerFactory;
        this.root              = root;
        this.repository        = repository;
        this.fileSystemService = fileSystemService;
        this.fileService       = fileService;
    }

    public async IAsyncEnumerable<IQueryFolderResponse> QueryFolderAsync(DirectoryInfo path)
    {
        var results = new ConcurrentDictionary<string, QueryFolderResponse>();

        // Scan the filesystem
        await foreach (var fs in GetFileSystemStuffAsync())
        {
            var result = new QueryFolderResponse(fs.Name);
            if (results.TryAdd(fs.Name, result))
                yield return result; // this is a new entry, yield it

            // by now the value is IN the collection
            result = results[fs.Name];

            result.PointerFile = fs.PointerFile;
            result.BinaryFile  = fs.BinaryFile;
        }

        // Scan the database
        var prefix = Path.GetRelativePath(root.FullName, path.FullName);

        throw new NotImplementedException();

        //await foreach (var db in repository.GetPointerFileEntriesWithBinaryPropertiesAsync(prefix))
        //{
        //    var name = PointerFileInfo.GetBinaryFileName(db.PointerFileEntry.RelativeName);

        //    var result = new QueryFolderResponse(name);

        //    if (results.TryAdd(name, result))
        //        yield return result;

        //    result = results[name];

        //    result.PointerFileEntry = db.PointerFileEntry;
        //    result.BinaryProperties = db.BinaryProperties;
        //}


        async IAsyncEnumerable<(string Name, BinaryFile BinaryFile, PointerFile PointerFile)> GetFileSystemStuffAsync()
        {
            var encounteredFiles = new HashSet<string>();

            foreach (var fib in fileSystemService.GetAllFileInfos(path))
            {
                var binaryFileName = GetBinaryFileName(fib);

                if (!encounteredFiles.Add(binaryFileName))
                    continue; //we already encountered this binaryfilename and yielded it

                BinaryFile  bf = null;
                PointerFile pf = null;

                if (fib is PointerFileInfo pfi)
                {
                    pf = fileService.GetExistingPointerFile(root, pfi);
                    bf = await fileService.GetExistingBinaryFileAsync(pf, assertHash: true);
                }
                else if (fib is BinaryFileInfo bfi)
                {
                    bf = await fileService.GetExistingBinaryFileAsync(root, bfi, false);
                    pf = fileService.GetExistingPointerFile(bf);
                }
                else
                    throw new NotImplementedException();

                yield return (binaryFileName, bf, pf);
            }


            string GetBinaryFileName(FileInfoBase fib) =>
                fib switch
                {
                    PointerFileInfo pfi => PointerFileInfo.GetBinaryFileName(pfi.Name),
                    BinaryFileInfo bfi  => bfi.Name,
                    _                   => throw new NotImplementedException()
                };
        }
    }
}

//using Arius.Core.Models;
//using Arius.Core.Repositories;
//using Arius.Core.Services;
//using Microsoft.Extensions.Logging;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Threading.Tasks;

//namespace Arius.Core.Queries;

//public interface IQueryFolderResponse
//{
//    public enum HYDRATIONSTATE
//    {
//        ARCHIVE,
//        HYDRATED
//    }

//    string         Name           { get; }
//    int            Length         { get; }
//    HYDRATIONSTATE HydrationState { get; }
//}



//internal record QueryFolderResponse : IQueryFolderResponse
//{
//    public QueryFolderResponse(string name)
//    {
//        this.Name = Name;
//    }

//    public string                              Name           { get; }
//    public int                                 Length         { get; set; }
//    public IQueryFolderResponse.HYDRATIONSTATE HydrationState { get; set; }

//    public BinaryFile       BinaryFile       { get; set; }
//    public PointerFile      PointerFile      { get; set; }
//    public PointerFileEntry PointerFileEntry { get; set; }
//    public BinaryProperties BinaryProperties { get; set; }

//}

//internal class RepositoryQueries
//{
//    private readonly ILogger<RepositoryQueries> logger;
//    private readonly ILoggerFactory             loggerFactory;
//    private readonly DirectoryInfo              root;
//    private readonly Repository                 repository;
//    private readonly FileSystemService          fileSystemService;
//    private readonly FileService                fileService;

//    public RepositoryQueries(ILoggerFactory loggerFactory, DirectoryInfo root, Repository repository, FileSystemService fileSystemService, FileService fileService)
//    {
//        this.logger            = loggerFactory.CreateLogger<RepositoryQueries>();
//        this.loggerFactory     = loggerFactory;
//        this.root              = root;
//        this.repository        = repository;
//        this.fileSystemService = fileSystemService;
//        this.fileService       = fileService;
//    }

//    public async IAsyncEnumerable<IQueryFolderResponse> QueryFolderAsync(DirectoryInfo path)
//    {
//        // Get what we need from the database
//        var dbTask = Task.Run(async () =>
//        {
//            //repository.Binaries.GetPropertiesAsync()

//            //var prefix = Path.GetRelativePath(root.FullName, path.FullName);

//            //return await repository.PointerFileEntries
//            //    .GetPointerFileEntriesAsync(prefix)
//            //    .ToDictionaryAsync(pfe => PointerFileInfo.GetBinaryFileName(pfe.RelativeName), pfe => pfe);

//            var r = new Dictionary<string, (PointerFileEntry PointerFileEntry, BinaryProperties BinaryProperties)>();

//            var prefix = Path.GetRelativePath(root.FullName, path.FullName);

//            await foreach (var pfe in repository.PointerFileEntries.GetPointerFileEntriesAsync(prefix))
//            {
//                var bp = await repository.Binaries.GetPropertiesAsync(pfe.BinaryHash);

//                var name = PointerFileInfo.GetBinaryFileName(pfe.RelativeName);

//                r.Add(name, (pfe, bp));
//            }

//            return r;
//        });

//        // Get what we need from the filesystem
//        var fsTask = Task.Run(async () =>
//        {
//            return await GetPointerFileEntriesAsync()
//                .ToDictionaryAsync(e => e.Name, e => e);
//        });

//        await Task.WhenAll(dbTask, fsTask);

//        var dbs = await dbTask;
//        var fss = await fsTask;

//        foreach (var name in dbs.Keys.Concat(fss.Keys).Distinct())
//        {
//            dbs.TryGetValue(name, out var db);
//            fss.TryGetValue(name, out var fs);

//            yield return new QueryFolderResponse(name)
//            {
//                BinaryFile       = fs.BinaryFile,
//                PointerFile      = fs.PointerFile,
//                PointerFileEntry = db.PointerFileEntry,
//                BinaryProperties = db.BinaryProperties,
//                HydrationState   = IQueryFolderResponse.HYDRATIONSTATE.HYDRATED
//            };
//        }






//        //var pf = fileService.GetExistingPointerFile(null, pfi);

//        //var bfrn = PointerFileInfo.GetBinaryFileName(pf.RelativeName);

//        //catch (IOException e) when (e.Message.Contains("virus"))
//        //{
//        //    logger.LogInformation($"Skipping {fib} due to {e.Message}");
//        //}


//        async IAsyncEnumerable<(string Name, BinaryFile BinaryFile, PointerFile PointerFile)> GetPointerFileEntriesAsync()
//        {
//            var encounteredFiles = new HashSet<string>();

//            foreach (var fib in fileSystemService.GetAllFileInfos(path))
//            {
//                var binaryFileName = GetBinaryFileName(fib);

//                if (!encounteredFiles.Add(binaryFileName))
//                    continue; //we already encountered this binaryfilename and yielded it

//                BinaryFile  bf = null;
//                PointerFile pf = null;

//                if (fib is PointerFileInfo pfi)
//                {
//                    pf  = fileService.GetExistingPointerFile(root, pfi);
//                    bf = await fileService.GetExistingBinaryFileAsync(r.PointerFile, assertHash: true);
//                }
//                else if (fib is BinaryFileInfo bfi)
//                {
//                    bf = await fileService.GetExistingBinaryFileAsync(root, bfi, false);
//                    pf = fileService.GetExistingPointerFile(r.BinaryFile);
//                }
//                else
//                    throw new NotImplementedException();

//                yield return (binaryFileName, bf, pf);
//            }


//        string GetBinaryFileName(FileInfoBase fib)
//        {
//            return fib switch
//            {
//                PointerFileInfo pfi => PointerFileInfo.GetBinaryFileName(pfi.Name),
//                BinaryFileInfo bfi  => bfi.Name,
//                _                   => throw new NotImplementedException()
//            };
//        }
//    }
//}