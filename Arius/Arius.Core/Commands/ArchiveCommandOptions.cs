using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using FluentValidation;
using System.IO;

namespace Arius.Core.Commands
{
    internal class ArchiveCommandOptions : AzureRepositoryOptions, 
        Facade.Facade.IOptions,

        ArchiveCommand.IOptions,

        //AzureRepository.IOptions,
        IBlobCopier.IOptions,
        IChunker.IOptions,
        PointerService.IOptions,
        IEncrypter.IOptions,
        IHashValueProvider.IOptions
    {
        public bool FastHash { get; private init; }
        public bool RemoveLocal { get; private init; }
        public AccessTier Tier { get; private init; }
        public bool Dedup { get; private init; }
        public string Path { get; private init; }

        internal ArchiveCommandOptions(string accountName, string accountKey, string passphrase, bool fastHash, string container, bool removeLocal, string tier, bool dedup, string path)
            : base(accountName, accountKey, container, passphrase)
        {
            FastHash = fastHash;
            RemoveLocal = removeLocal;
            Tier = tier;
            Dedup = dedup;
            Path = path;

            var validator = new Validator();
            validator.ValidateAndThrow(this);
        }

        private class Validator : AbstractValidator<ArchiveCommandOptions>
        {
            public Validator()
            {
                RuleFor(o => o.Path)
                    .NotEmpty()
                    .Custom((path, context) =>
                    {
                        if (!Directory.Exists(path))
                            context.AddFailure($"Directory {path} does not exist.");
                    });
                RuleFor(o => o.Tier).Must(tier =>
                    tier == AccessTier.Hot ||
                    tier == AccessTier.Cool ||
                    tier == AccessTier.Archive);
            }
        }
    }
}