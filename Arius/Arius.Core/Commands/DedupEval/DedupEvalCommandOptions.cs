using Arius.Core.Services;
using System.IO;

namespace Arius.Core.Commands.DedupEval
{
    internal class DedupEvalCommandOptions : Facade.Facade.IOptions,
        DedupEvalCommand.IOptions,

        IHashValueProvider.IOptions
    {
        public DirectoryInfo Root { get; init; }

        public string Passphrase => string.Empty; //No passphrase/hash seed needed
        public bool FastHash => false; //Do not use fasthash
    }
}