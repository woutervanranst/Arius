﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Azure.Storage.Blobs;
using NUnit.Framework;

namespace Arius.Tests
{
    [SetUpFixture]
    public class TestSetup
    {
        public static DirectoryInfo sourceFolder;
        public static DirectoryInfo rootDirectoryInfo;
        public static BlobContainerClient container;
        public static string accountName;
        public static string accountKey;
        public static string passphrase = "myPassphrase";

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // Executes once before the test run. (Optional)

            //sourceFolder = new DirectoryInfo(@"C:\Users\Wouter\Documents\NUnitTestSourceFolder");
            sourceFolder = PopulateSourceDirectory();

            // Create temp folder
            var tempFolderName = RandomString(8).ToLower();
            rootDirectoryInfo = new DirectoryInfo(Path.Combine(Path.GetTempPath(), tempFolderName));
            rootDirectoryInfo.Create();


            // Create temp container
            accountName = Environment.GetEnvironmentVariable("ACCOUNT_NAME");
            if (string.IsNullOrEmpty(accountName))
                throw new ArgumentException("Environment variable ACCOUNT_NAME not specified");

            accountKey = Environment.GetEnvironmentVariable("ACCOUNT_KEY");
            if (string.IsNullOrEmpty(accountKey))
                throw new ArgumentException("Environment variable ACCOUNT_KEY not specified");

            var connectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
            var bsc = new BlobServiceClient(connectionString);
            container = bsc.CreateBlobContainer(tempFolderName);
        }

        private DirectoryInfo PopulateSourceDirectory()
        {
            var sourceDirectory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), ".ariustestsourcedir"));
            if (sourceDirectory.Exists) sourceDirectory.Delete(true);
            sourceDirectory.Create();

            CreateRandomFile(Path.Combine(sourceDirectory.FullName, "fileA.1"), 0.5);
            CreateRandomFile(Path.Combine(sourceDirectory.FullName, "fileB.1"), 2);
            CreateRandomFile(Path.Combine(sourceDirectory.FullName, "file with space.txt"), 5);

            return sourceDirectory;
        }

        private void CreateRandomFile(string fileFullName, double sizeInMB)
        {
            byte[] data = new byte[8192];
            var rng = new Random();
            using (FileStream stream = File.OpenWrite(fileFullName))
            {
                for (int i = 0; i < sizeInMB * 128; i++)
                {
                    rng.NextBytes(data);
                    stream.Write(data, 0, data.Length);
                }
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            rootDirectoryInfo.Delete(true);
            container.Delete();
        }


        private static Random random = new Random();
        private static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public static FileInfo CopyFile(FileInfo source, DirectoryInfo targetDir)
        {
            return source.CopyTo(Path.Combine(targetDir.FullName, source.Name));
        }
        public static FileInfo CopyFile(FileInfo source, DirectoryInfo targetDir, string targetName)
        {
            return source.CopyTo(Path.Combine(targetDir.FullName, targetName));
        }

        public static FileInfo CopyFile(FileInfo source, string targetName)
        {
            return source.CopyTo(Path.Combine(source.DirectoryName, targetName));
        }

        public static void MoveFile(FileInfo source, string targetName)
        {
            source.MoveTo(Path.Combine(source.DirectoryName, targetName));
        }

        private static void CopyFolder(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
                System.IO.File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));

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
}