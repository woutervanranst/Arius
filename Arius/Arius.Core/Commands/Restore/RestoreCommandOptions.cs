using Arius.Core.Services;
using FluentValidation;
using System;
using System.IO;

namespace Arius.Core.Commands.Restore;

internal class RestoreCommandOptions : RepositoryOptions,
    IHashValueProvider.IOptions
{
    internal RestoreCommandOptions(string accountName, string accountKey, string container, string passphrase, bool synchronize, bool download, bool keepPointers, string path, DateTime pointInTimeUtc)
        : base(accountName, accountKey, container, passphrase)
    {
        Synchronize = synchronize;
        Download = download;
        KeepPointers = keepPointers;
        PointInTimeUtc = pointInTimeUtc;

        // Check whether the given path exists (throws FileNotFoundException) and is a File or Directory
        Path = File.GetAttributes(path).HasFlag(FileAttributes.Directory) ? // as per https://stackoverflow.com/a/1395226/1582323
            new DirectoryInfo(path) : 
            new FileInfo(path);

        var validator = new Validator();
        validator.ValidateAndThrow(this);
    }

    //public bool FastHash => false; //Do not fasthash on restore to ensure integrity
    public bool Synchronize { get; }
    public bool Download { get; }
    public bool KeepPointers { get; }
    public DateTime PointInTimeUtc { get; }
    public FileSystemInfo Path { get; }


    public int IndexBlock_Parallelism => 1; // 16 * 2;
    public int DownloadBinaryBlock_Parallelism => 1; //16 * 2;
    public int PARALLELISLD => 100;


    private class Validator : AbstractValidator<RestoreCommandOptions>
    {
        public Validator()
        {
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