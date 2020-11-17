using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Arius.CommandLine;
using Microsoft.Extensions.Logging;

namespace Arius
{
    internal interface IEncrypterOptions : ICommandExecutorOptions
    {
        string Passphrase { get; }
    }

    internal class SevenZipEncrypter<T> : IEncrypter<T> where T : IFile
    {


        public SevenZipEncrypter(ICommandExecutorOptions options, ILogger<SevenZipEncrypter<T>> logger)
        {
            _passphrase = ((IEncrypterOptions) options).Passphrase;

            _7zExecutableFullName = Task.Run(() => ExternalProcess.FindFullName(logger, "7z.exe", "7z"));
        }

        private readonly string _passphrase;
        private readonly Task<string> _7zExecutableFullName;


        public IEncrypted<T> Encrypt(T fileToChunk)
        {
            var k = _7zExecutableFullName.Result;
            throw new NotImplementedException();
        }

        public T Decrypt(IEnumerable<T> chunksToJoin)
        {
            throw new NotImplementedException();
        }
    }
}
