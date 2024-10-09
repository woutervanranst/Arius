using System;
using System.IO;
using Arius.Core.Facade;

namespace Arius.Core.Commands.Restore;

internal record RestoreCommand : RepositoryOptions
{
    public RestoreCommand(RepositoryOptions options, DirectoryInfo root, bool synchronize, bool download, bool keepPointers, DateTime? pointInTimeUtc) : base(options)
    {
        this.Synchronize    = synchronize;
        this.Download       = download;
        this.KeepPointers   = keepPointers;
        this.PointInTimeUtc = pointInTimeUtc ?? DateTime.UtcNow;
        this.Path           = root;
    }
    public RestoreCommand(string accountName, string accountKey, string containerName, string passphrase, DirectoryInfo root, bool synchronize, bool download, bool keepPointers, DateTime? pointInTimeUtc) : base(accountName, accountKey, containerName, passphrase)
    {
        this.Synchronize    = synchronize;
        this.Download       = download;
        this.KeepPointers   = keepPointers;
        this.PointInTimeUtc = pointInTimeUtc ?? DateTime.UtcNow;
        this.Path           = root;
    }

    public bool          Synchronize    { get; }
    public bool          Download       { get; }
    public bool          KeepPointers   { get; }
    public DateTime      PointInTimeUtc { get; }
    public DirectoryInfo Path           { get; }


    public int IndexBlock_Parallelism          => 16 * 2;
    public int DownloadBinaryBlock_Parallelism => 16 * 2;

    public override void Validate()
    {
        base.Validate();

        if (Path is null || !Path.Exists)
            throw new ArgumentException("The specified path does not exist", nameof(Path));

        //if (!Synchronize && !Download)
        //    throw new ArgumentException("Either specify --synchronize or --download"); //this is just silly to call

        //// validate the IRepositoryOptions (AccountName, AccountKey, Container, Passphrase)
        //RuleFor(o => (IRepositoryOptions)o)
        //    .SetInheritanceValidator(v =>
        //        v.Add<IRepositoryOptions>(new IRepositoryOptions.Validator()));

        //// Validate valid combination of Synchronize/Path/Download
        //RuleFor(o => o)
        //    .Custom((o, context) =>
        //    {
        //        if (o.Path is null || !o.Path.Exists)
        //            context.AddFailure("The specified path does not exist");

        //        //if (!o.Synchronize && !o.Download)
        //        //    context.AddFailure("Either specify --synchronize or --download"); //this is just silly to call
        //    });
    }
}


internal record RestorePointerFileEntriesCommand : RestoreCommand
{
    public RestorePointerFileEntriesCommand(RepositoryOptions options, DirectoryInfo root, bool download, bool keepPointers, DateTime pointInTimeUtc, params string[] relativeNames)
        : base(options: options, root: root, synchronize: false, download: download, keepPointers: keepPointers, pointInTimeUtc: pointInTimeUtc)
    {
        // NOTE: synchronize is always false here
        RelativeNames = relativeNames;
    }
    public string[] RelativeNames { get; }
}