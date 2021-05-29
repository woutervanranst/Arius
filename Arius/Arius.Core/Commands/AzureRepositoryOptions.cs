using Arius.Core.Repositories;
using FluentValidation;
using System;

namespace Arius.Core.Commands
{
    internal class AzureRepositoryOptions : Facade.Facade.IOptions,
        AzureRepository.IOptions
    {
        public string AccountName { get; private init; }

        public string AccountKey { get; private init; }

        public string Container { get; private init; }

        public string Passphrase { get; private init; }

        internal AzureRepositoryOptions(string accountName, string accountKey, string container, string passphrase)
        {
            AccountName = accountName;
            AccountKey = accountKey;
            Passphrase = passphrase;
            Container = container;

            var validator = new Validator();
            validator.ValidateAndThrow(this);
        }

        private class Validator : AbstractValidator<AzureRepositoryOptions>
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