using Arius.Core.Services;
using FluentValidation;
using System.IO;

namespace Arius.Core.Commands.Restore;

internal class RestoreCommandOptions : ServicesOptions,
    Facade.Facade.IOptions,
    IHashValueProvider.IOptions
{
    //public bool FastHash => false; //Do not fasthash on restore to ensure integrity
    public bool Synchronize { get; private init; }
    public bool Download { get; private init; }
    public bool KeepPointers { get; private init; }
    public string Path { get; private init; }


    public int PARALLELISLD => 100;

    internal RestoreCommandOptions(string accountName, string accountKey, string container, string passphrase, bool synchronize, bool download, bool keepPointers, string path)
        : base(accountName, accountKey, container, passphrase)
    {
        Synchronize = synchronize;
        Download = download;
        KeepPointers = keepPointers;
        Path = path;

        var validator = new Validator();
        validator.ValidateAndThrow(this);
    }

    private class Validator : AbstractValidator<RestoreCommandOptions>
    {
        public Validator()
        {
            RuleFor(o => o.Path)
                .NotEmpty()
                .Custom((path, context) =>
                {
                    if (!(Directory.Exists(path) || File.Exists(path)))
                        context.AddFailure($"File or folder '{path}' does not exist.");
                });
        }
    }
}