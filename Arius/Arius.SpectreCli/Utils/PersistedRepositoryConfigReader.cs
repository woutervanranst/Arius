using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Arius.CliSpectre.Commands;
using Arius.Core.Services;

namespace Arius.CliSpectre.Utils
{
    internal static class PersistedRepositoryConfigReader
    {
        public static (string accountName, string accountKey, string container) LoadSettings(DirectoryInfo path, string passphrase)
        {
            var configFile = new FileInfo(Path.Combine(path.FullName, "arius.config"));
            if (!configFile.Exists)
                return default;

            try
            {
                using var ss = configFile.OpenRead();
                var ps = JsonSerializer.Deserialize<PersistedSettings>(ss);

                return (ps.AccountName, CryptoService.Decrypt(ps.EncryptedAccountKey, passphrase), ps.Container);
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
                //configFile.Delete();

                //Console.WriteLine(e);
                //throw;
            }

            return default;
        }

        public static void SaveSettings(RepositoryOptions settings)
        {
            var s = new PersistedSettings
            {
                AccountName = settings.AccountName,
                EncryptedAccountKey = CryptoService.Encrypt(settings.AccountKey, settings.Passphrase),
                Container = settings.Container
            };
            
            using var ms = new MemoryStream();
            JsonSerializer.Serialize(ms, s);
            ms.Seek(0, SeekOrigin.Begin);

            var fn = Path.Combine(settings.Path.FullName, "arius.config");
            using var ts = File.Open(fn, FileMode.Truncate, FileAccess.Write); // FileInfo.OpenWrite APPENDS/does not truncate 
            ms.CopyTo(ts);
            File.SetAttributes(fn, FileAttributes.Hidden); // make it hidden so it is not archived by the ArchiveCommandBlocks.IndexBlock
        }

        private class PersistedSettings
        {
            public string AccountName { get; init; }
            public string EncryptedAccountKey { get; init; }
            public string Container { get; init; }
        }
    }
}
