using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using System.IO;

namespace Arius.Core.Commands
{
    internal class RestoreCommandOptions : Facade.Facade.IOptions,
            RestoreCommand.IOptions,

            SynchronizeBlockProvider.IOptions,
            DownloadBlockProvider.IOptions,
            ProcessPointerChunksBlockProvider.IOptions,
            MergeBlockProvider.IOptions,

            //IChunker.IOptions, // geen IChunker options

            IBlobCopier.IOptions,
            IHashValueProvider.IOptions,
            IEncrypter.IOptions,
            AzureRepository.IOptions
    {
        public string AccountName { get; init; }
        public string AccountKey { get; init; }
        public string Passphrase { get; init; }
        public bool FastHash => false; //Do not fasthash on restore to ensure integrity
        public string Container { get; init; }
        public bool Synchronize { get; init; }
        public bool Download { get; init; }
        public bool KeepPointers { get; init; }
        public string Path { get; init; }

        //public bool Dedup => false;
        //public AccessTier Tier { get => throw new NotImplementedException(); init => throw new NotImplementedException(); } // Should not be used
        //public bool RemoveLocal { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        //public int MinSize { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
        //public bool Simulate { get => throw new NotImplementedException(); init => throw new NotImplementedException(); }
    
        private RestoreCommandOptions()
        {
        }

        public static RestoreCommandOptions Create(string accountName, string accountKey, string container, string passphrase, bool synchronize, bool download, bool keepPointers, string path)
        {
            var options = new RestoreCommandOptions
            {
                AccountName = accountName,
                AccountKey = accountKey,
                Passphrase = passphrase,
                Container = container,
                Synchronize = synchronize,
                Download = download,
                KeepPointers = keepPointers,
                Path = path
            };

            var validator = new Validator();
            validator.ValidateAndThrow(options);

            return options;
        }

        private class Validator : AbstractValidator<RestoreCommandOptions>
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
            }
        }
    }
}
