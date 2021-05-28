using Arius.Core.Repositories;
using Arius.Core.Services;
using Azure.Storage.Blobs.Models;
using FluentValidation;
using System.IO;

namespace Arius.Core.Commands
{
    internal class ArchiveCommandOptions : Facade.Facade.IOptions,
            ArchiveCommand.IOptions,

            UploadEncryptedChunksBlockProvider.IOptions,
            RemoveDeletedPointersTaskProvider.IOptions,
            DeleteBinaryFilesTaskProvider.IOptions,

            AzureRepository.IOptions,
            IBlobCopier.IOptions,
            IChunker.IOptions,
            IEncrypter.IOptions,
            IHashValueProvider.IOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public bool FastHash { get; init; }
        public string Container { get; init; }
        public bool RemoveLocal { get; init; }
        public AccessTier Tier { get; init; }
        public bool Dedup { get; init; }
        public string Path { get; init; }

        private ArchiveCommandOptions() 
        {
        }

        public static ArchiveCommandOptions Create(string accountName, string accountKey, string passphrase, bool fastHash, string container, bool removeLocal, string tier, bool dedup, string path)
        {
            var options = new ArchiveCommandOptions
            {
                AccountName = accountName,
                AccountKey = accountKey,
                Passphrase = passphrase,
                FastHash = fastHash,
                Container = container,
                RemoveLocal = removeLocal,
                Tier = tier,
                Dedup = dedup,
                Path = path
            };

            var validator = new Validator();
            validator.ValidateAndThrow(options);

            return options;
        }

        private class Validator : AbstractValidator<ArchiveCommandOptions>
        {
            public Validator()
            {
                RuleFor(o => o.AccountName).NotEmpty();
                RuleFor(o => o.AccountKey).NotEmpty();
                RuleFor(o => o.Container).NotEmpty();
                RuleFor(o => o.Passphrase).NotEmpty();
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