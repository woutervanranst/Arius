
using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using System;
namespace Arius.Core.Commands;

public interface IRepositoryOptions : ICommandOptions // the interface is public, the implementation is internal
{
    string AccountName { get; }
    string AccountKey { get; }
    string Container { get; }
    string Passphrase { get; }

    protected class Validator : AbstractValidator<IRepositoryOptions>
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