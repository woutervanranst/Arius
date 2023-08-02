using FluentValidation;
using System;
using System.IO;
using Arius.Core.Facade;
using Arius.Core.Repositories;

namespace Arius.Core.Commands.Restore;

internal interface IRestoreCommandOptions : IRepositoryOptions
{
    bool Synchronize { get; }
    bool Download { get; }
    bool KeepPointers { get; }
    DateTime? PointInTimeUtc { get; }
    DirectoryInfo Path { get; }


    int IndexBlock_Parallelism => 16 * 2;
    int DownloadBinaryBlock_Parallelism => 16 * 2;


    internal class Validator : AbstractValidator<IRestoreCommandOptions>
    {
        public Validator()
        {
            // validate the IRepositoryOptions (AccountName, AccountKey, Container, Passphrase)
            RuleFor(o => (IRepositoryOptions)o)
                .SetInheritanceValidator(v =>
                    v.Add<IRepositoryOptions>(new IRepositoryOptions.Validator()));

            // Validate valid combination of Synchronize/Path/Download
            RuleFor(o => o)
                .Custom((o, context) =>
                {
                    if (o.Synchronize && o.Path is not DirectoryInfo)
                        context.AddFailure($"The synchronize flag is only valid for directories");

                    if (!o.Synchronize && !o.Download)
                        context.AddFailure("Either specify --synchronize or --download"); //this is just silly to call
                });
        }
    }
}

internal record RestoreCommandOptions : RepositoryOptions, IRestoreCommandOptions
{
    public RestoreCommandOptions(Repository repo, DirectoryInfo root, bool synchronize, bool download, bool keepPointers, DateTime? pointInTimeUtc) : base(repo.Options)
    {
        this.Synchronize    = synchronize;
        this.Download       = download;
        this.KeepPointers   = keepPointers;
        this.PointInTimeUtc = pointInTimeUtc;
        this.Path           = root;
    }

    public bool          Synchronize    { get; }
    public bool          Download       { get; }
    public bool          KeepPointers   { get; }
    public DateTime?     PointInTimeUtc { get; }
    public DirectoryInfo Path           { get; }
}