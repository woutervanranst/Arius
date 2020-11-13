using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Azure.Storage.Blobs;
using NUnit.Framework;


// A SetUpFixture outside of any namespace provides SetUp and TearDown for the entire assembly.
[SetUpFixture]
public class TestSetup
{
    public static string folder;
    public static BlobContainerClient container;
    public static string accountName;
    public static string accountKey;

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        // Executes once before the test run. (Optional)

        var sourceFolder = @"C:\Users\Wouter\Documents\NUnitTestSourceFolder";

        // Create temp folder
        var tempFolderName = RandomString(8).ToLower();
        folder = Path.Combine(Path.GetTempPath(), tempFolderName);
        Directory.CreateDirectory(folder);

        //Copy files to temp folder
        CopyFolder(sourceFolder, folder);

        // Create temp container
        accountName = "aurius";
        accountKey = Environment.GetEnvironmentVariable("ACCOUNT_KEY");

        var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
        var bsc = new BlobServiceClient(connectionString);
        container = bsc.CreateBlobContainer(tempFolderName);
    }

    [OneTimeTearDown]
    public void RunAfterAnyTests()
    {
        // Executes once after the test run. (Optional)

        Directory.Delete(folder, true);
        container.Delete();
    }


    private static Random random = new Random();
    private string RandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private void CopyFolder(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(sourceDir))
            CopyFolder(directory, Path.Combine(targetDir, Path.GetFileName(directory)));
    }

    public static void ExecuteCommandline(string args)
    {
        using var process = new Process();

        var exeFolder = @"C:\Users\Wouter\Documents\GitHub\Arius\Arius\Arius\bin\Debug\net5.0"; //AppDomain.CurrentDomain.BaseDirectory

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(exeFolder, "arius.exe"),

            UseShellExecute = false,
            Arguments = args,

            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        process.StartInfo = psi;

        string output = "";
        process.OutputDataReceived += (_, data) => output += data.Data + Environment.NewLine;
        process.ErrorDataReceived += (_, data) => output += data.Data + Environment.NewLine;

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        /*
         * NOTE If error -2147450749 is encountered:
         *Cannot use file stream for [C:\Users\Wouter\Documents\GitHub\Arius\Arius\Arius.Tests\bin\Debug\net5.0\arius.deps.json]: No such file or directory
            A fatal error was encountered. The library 'hostpolicy.dll' required to execute the application was not found in 'C:\Program Files\dotnet'.
            Failed to run as a self-contained app.
              - The application was run as a self-contained app because 'C:\Users\Wouter\Documents\GitHub\Arius\Arius\Arius.Tests\bin\Debug\net5.0\arius.runtimeconfig.json' was not found.
              - If this should be a framework-dependent app, add the 'C:\Users\Wouter\Documents\GitHub\Arius\Arius\Arius.Tests\bin\Debug\net5.0\arius.runtimeconfig.json' file and specify the appropriate framework.
         */

        Assert.AreEqual(0, process.ExitCode, message: output);
    }
}

namespace Arius.Tests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Archive()
        {
            TestSetup.ExecuteCommandline($"archive -n {TestSetup.accountName} -k {TestSetup.accountKey} -p woutervr -c {TestSetup.container.Name} --keep-local --tier hot {TestSetup.folder}");
            //Arius.RemoteEncryptedAriusManifest.AriusManifest x;
            //x.
            //Assert.Pass();
        }

        /*
         * Test cases
         * Create File
         * Duplicate file
         * Rename file
         * Delete file
         * Add file again that was previously deleted
         * Rename content file
         * rename .arius file
         * Modify the binary
            * azcopy fails
         * add binary > get .arius file > delete .arius file > archive again > arius file will reappear but cannot appear twice in the manifest
         *
         *
         *
         * add binary
         * add another binary
         * add the same binary
         *
         *
            //TODO test File X is al geupload ik kopieer 'X - Copy' erbij> expectation gewoon pointer erbij binary weg
         *
         *
         * geen lingering files
         *  localcontentfile > blijft staan
         * .7z.arius weg
         *
         * dedup > chunks weg
         * .7z.arius weg
         *
         * #2
         * change a manifest without the binary present
         *
         */
    }

    [TestFixture]
    public class Test
    {

    }
}
