﻿using Arius.Core.Commands;
using FluentValidation;

namespace Arius.Core.Facade;

public interface IRepositoryOptions : ICommandOptions // the interface is public, the implementation is internal
{
    string AccountName { get; }
    string AccountKey { get; }
    string ContainerName { get; }
    string Passphrase { get; }

#pragma warning disable CS0108 // Member hides inherited member; missing new keyword -- not required
    protected class Validator : AbstractValidator<IRepositoryOptions>
#pragma warning restore CS0108
    {
        public Validator()
        {
            RuleFor(o => o.AccountName).NotEmpty();
            RuleFor(o => o.AccountKey).NotEmpty();
            RuleFor(o => o.ContainerName).NotEmpty();
            RuleFor(o => o.Passphrase).NotEmpty();
        }
    }
}