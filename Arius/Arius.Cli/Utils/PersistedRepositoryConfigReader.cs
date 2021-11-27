using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Arius.Cli.Commands;
using Arius.Core.Services;
using Microsoft.Extensions.Logging;

namespace Arius.Cli.Utils;

internal static class PersistedRepositoryConfigReader
{
    private const string CONFIG_FILE_NAME = "arius.config";

    public static (string accountName, string accountKey, string container) LoadSettings(DirectoryInfo path, string passphrase)
    {
        var configFile = new FileInfo(Path.Combine(path.FullName, CONFIG_FILE_NAME));
        if (!configFile.Exists)
            return default;

        try
        {
            using var ss = configFile.OpenRead();
            var ps = JsonSerializer.Deserialize<PersistedSettings>(ss);

            return (ps.AccountName, CryptoService.Decrypt(ps.EncryptedAccountKey, passphrase), ps.Container);
        }
        catch (CryptographicException e)
        {
            throw new ArgumentException($"Could not decrypt config file '{configFile}'. Check the password or delete the config.");
        }
        catch (AggregateException e) when (e.InnerException is InvalidDataException)
        {
            // Wrong Passphrase?
            throw;
        }
        catch (AggregateException e) when (e.InnerException is ArgumentNullException)
        {
            // No passphrase
            throw;
        }
        catch (JsonException e)
        {
            //configFile.Delete();

            //Console.WriteLine(e);
            throw;
        }

        return default;
    }

    public static void SaveSettings(ILogger logger, RepositoryOptions settings, DirectoryInfo root)
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

        var fn = Path.Combine(root.FullName, CONFIG_FILE_NAME);
        using var ts = File.Exists(fn)
            ? File.Open(fn, FileMode.Truncate, FileAccess.Write)  // FileInfo.OpenWrite APPENDS/does not truncate 
            : File.Open(fn, FileMode.CreateNew, FileAccess.Write);
        ms.CopyTo(ts);
        File.SetAttributes(fn, FileAttributes.Hidden | FileAttributes.System); // make it hidden so it is not archived by the ArchiveCommandBlocks.IndexBlock

        logger.LogDebug("Saved options to file");
    }

    private class PersistedSettings
    {
        public string AccountName { get; init; }
        public string EncryptedAccountKey { get; init; }
        public string Container { get; init; }
    }
}