using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Arius.Core.Commands
{
    internal class Options
    {
        public string AccountName
        {
            get => accountName ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (accountName is not null)
                    throw new InvalidOperationException("ALREADY SET");

                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("NOT VALID");

                accountName = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private string accountName;


        public string AccountKey
        {
            get => accountKey ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (accountKey is not null)
                    throw new InvalidOperationException("ALREADY SET");

                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("NOT VALID");

                accountKey = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private string accountKey;


        public string Container
        {
            get => container ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (container is not null)
                    throw new InvalidOperationException("ALREADY SET");

                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("NOT VALID");

                container = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private string container;


        public string Passphrase
        {
            get => passphrase ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (passphrase is not null)
                    throw new InvalidOperationException("ALREADY SET");

                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("NOT VALID");

                passphrase = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private string passphrase;


        public bool FastHash
        {
            get => fastHash ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (fastHash.HasValue)
                    throw new InvalidOperationException("ALREADY SET");

                fastHash = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private bool? fastHash;


        public bool RemoveLocal
        {
            get => removeLocal ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (removeLocal.HasValue)
                    throw new InvalidOperationException("ALREADY SET");

                removeLocal = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private bool? removeLocal;


        public AccessTier Tier
        {
            get => tier ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (tier is not null)
                    throw new InvalidOperationException("ALREADY SET");

                if (tier != AccessTier.Hot &&
                    tier != AccessTier.Cool &&
                    tier != AccessTier.Archive)
                    throw new ArgumentException("NOT VALID");

                tier = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private AccessTier? tier;


        public bool Dedup
        {
            get => dedup ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (dedup.HasValue)
                    throw new InvalidOperationException("ALREADY SET");

                dedup = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private bool? dedup;


        public string Path
        {
            get => path ?? throw new InvalidOperationException("NOT SET");
            set
            {
                if (path is not null)
                    throw new InvalidOperationException("ALREADY SET");

                if (string.IsNullOrEmpty(value))
                    throw new ArgumentException("NOT VALID");
                if (!Directory.Exists(value))
                    throw new ArgumentException($"Directory {value} does not exist.");

                path = value;

                //set => name = value ?? throw new ArgumentNullException(nameof(value), $"{nameof(Name)} cannot be null");
            }
        }
        private string path;
    }
}
