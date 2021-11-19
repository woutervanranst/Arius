using Arius.Core.Services;
using FluentValidation;
using System;
using System.IO;

namespace Arius.Core.Commands.Restore;

public interface IRestoreCommandOptions : IRepositoryOptions
    //,
    //IHashValueProvider.IOptions
{
    bool Synchronize { get; }
    bool Download { get; }
    bool KeepPointers { get; }
    DateTime? PointInTimeUtc { get; }
    DirectoryInfo Path { get; }


    int IndexBlock_Parallelism => 1; // 16 * 2;
    int DownloadBinaryBlock_Parallelism => 1; //16 * 2;


    internal new class Validator : AbstractValidator<IRestoreCommandOptions>
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