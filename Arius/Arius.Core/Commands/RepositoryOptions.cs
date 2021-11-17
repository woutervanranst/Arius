
using Arius.Core.Repositories;
using Arius.Core.Services;
using FluentValidation;
using System;
namespace Arius.Core.Commands;

public interface IRepositoryOptions // the interface is public, the implementation is internal
{
    string AccountName { get; }
    string AccountKey { get; }
    string Container { get; }
    string Passphrase { get; }
}

internal class RepositoryOptions : 
    Facade.IOptions,
    IRepositoryOptions
{
    public string AccountName { get; }
    public string AccountKey { get; }
    public string Container { get; }
    public string Passphrase { get; }

    internal RepositoryOptions(string accountName, string accountKey, string container, string passphrase) 
    {
        AccountName = accountName;
        AccountKey = accountKey;
        Container = container;
        Passphrase = passphrase;

        var validator = new Validator();
        validator.ValidateAndThrow(this);
    }
    private class Validator : AbstractValidator<RepositoryOptions>
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