using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.Core.Services;
using Arius.SpectreCli.Commands;

namespace Arius.CliSpectre.Utils
{
    internal static class PersistedRepositoryConfigReader
    {
        public static (string accountName, string accountKey, string container) LoadSettings(DirectoryInfo path, string passphrase)
        {
            var configFile = new FileInfo(Path.Combine(path.FullName, "arius.config"));

            try
            {
                if (configFile.Exists)
                {
                    using var ss = configFile.OpenRead();
                    using var ms = new MemoryStream();
                    CryptoService.DecryptAndDecompressAsync(ss, ms, passphrase).Wait();
                    ms.Seek(0, SeekOrigin.Begin);
                    var s = JsonSerializer.Deserialize<RepositorySettings>(ms);

                    return (s.AccountName, s.AccountKey, s.Container);
                }
            }
            catch (AggregateException e) when (e.InnerException is InvalidDataException)
            {
                // Wrong Passphrase?
            }
            catch (AggregateException e) when (e.InnerException is ArgumentNullException)
            {
                // No passphrase
            }
            catch (JsonException e)
            {
                configFile.Delete();
                //Console.WriteLine(e);
                //throw;
            }

            return default;
        }

        public static void SaveSettings(RepositorySettings settings)
        {
            var configFile = new FileInfo(System.IO.Path.Combine(settings.Path.FullName, "arius.config"));
            using var ms0 = new MemoryStream();
            JsonSerializer.Serialize(ms0, this);
            ms0.Seek(0, SeekOrigin.Begin);
            using var ts = configFile.OpenWrite();
            CryptoService.CompressAndEncryptAsync(ms0, ts, settings.Passphrase).Wait();
            configFile.Attributes = FileAttributes.Hidden; // make it hidden so it is not archived by the ArchiveCommandBlocks.IndexBlock
        }

        private class PersistedSettings
        {
            private string AccountName { get; set; }
            private string EncryptedAccountKey { get; set; }
            private string Container { get; set; }
        }
    }
}
