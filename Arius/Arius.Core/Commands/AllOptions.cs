
using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using System;
namespace Arius.Core.Commands
{
    internal class AllOptions : 
        Facade.Facade.IOptions,
        
        Repository.IOptions,

        IHashValueProvider.IOptions,
        PointerService.IOptions,
        IBlobCopier.IOptions //TODO remove this? see https://github.com/woutervanranst/Arius/issues/28
    {
        public string AccountName { get; private init; }
        public string AccountKey { get; private init; }
        public string Container { get; private init; }
        public string Passphrase { get; private init; }
        public string Path { get; private init; }

        internal AllOptions(string accountName, string accountKey, string container, string passphrase, string path)
        {
            AccountName = accountName;
            AccountKey = accountKey;
            Passphrase = passphrase;
            Container = container;
            Path = path;

            var validator = new Validator();
            validator.ValidateAndThrow(this);
        }
        private class Validator : AbstractValidator<AllOptions>
        {
            public Validator()
            {
                RuleFor(o => o.AccountName).NotEmpty();
                RuleFor(o => o.AccountKey).NotEmpty();
                RuleFor(o => o.Container).NotEmpty();
                RuleFor(o => o.Passphrase).NotEmpty();
                RuleFor(o => o.Path).NotEmpty();
            }
        }
    }
}