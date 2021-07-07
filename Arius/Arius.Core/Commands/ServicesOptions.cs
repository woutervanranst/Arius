
using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using System;
namespace Arius.Core.Commands
{
    internal class ServicesOptions : 
        Facade.Facade.IOptions,
        
        Repository.IOptions,

        IHashValueProvider.IOptions,
        IBlobCopier.IOptions, //TODO remove this? see https://github.com/woutervanranst/Arius/issues/28
        IEncrypter.IOptions
    {
        public string AccountName { get; private init; }
        public string AccountKey { get; private init; }
        public string Container { get; private init; }
        public string Passphrase { get; private init; }

        internal ServicesOptions(string accountName, string accountKey, string container, string passphrase)
        {
            AccountName = accountName;
            AccountKey = accountKey;
            Passphrase = passphrase;
            Container = container;

            var validator = new Validator();
            validator.ValidateAndThrow(this);
        }
        private class Validator : AbstractValidator<ServicesOptions>
        {
            public Validator()
            {
                RuleFor(o => o.AccountName).NotEmpty();
                RuleFor(o => o.AccountKey).NotEmpty();
                RuleFor(o => o.Container).NotEmpty();
                RuleFor(o => o.Passphrase).NotEmpty();
            }
        }
    }
}