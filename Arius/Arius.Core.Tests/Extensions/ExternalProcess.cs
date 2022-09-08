using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Arius.Core.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arius.Core.Services;

internal static class ExternalProcess
{
    /// <summary>
    /// Find a file systemwide
    /// </summary>
    /// <param name="windowsExecutableName">The Windows executable name, including the '.exe' suffix</param>
    /// <param name="linuxExecutableName">The Linux executable name</param>
    /// <param name="logger"></param>
    /// <returns></returns>
    public static string FindFullName(string windowsExecutableName, string linuxExecutableName, ILogger logger = null)
    {
        logger ??= NullLoggerFactory.Instance.CreateLogger("");

        logger.LogDebug($"Looking for windows:{windowsExecutableName} / linux:{linuxExecutableName}");
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            logger.LogWarning("Environment variable PATH not found");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // 1. Find in the PATH folders
            if (!string.IsNullOrEmpty(path))
            {
                var executables = path.Split(';')
                    .Where(dir => !string.IsNullOrWhiteSpace(dir))
                    .Select(dir => new DirectoryInfo(dir))
                    .SelectMany(dir => dir.TryGetFiles(windowsExecutableName))
                    .ToArray();

                if (executables.Length > 0)
                {
                    var fullname = executables.First().FullName;

                    logger.LogDebug($"Found {executables.Length} instance(s) of {windowsExecutableName}. Returning the first one: {fullname}");

                    return fullname;
                }
            }

            // 2. Find using WHERE
            path = Path.GetPathRoot(Environment.SystemDirectory);

            try
            {
                logger.LogWarning($"Did not find {windowsExecutableName} in PATH variable. Searching on {path}. Consider adding the location to the PATH variable to improve speed.");
                var fullNames = RunSimpleProcess("where", $" /R {path} {windowsExecutableName}").Split(Environment.NewLine);

                logger.LogDebug($"Found {fullNames.Length} instance(s) of {windowsExecutableName}. Returning the first one: {fullNames.First()}");

                return fullNames.First();
            }
            catch (ApplicationException e)
            {
                throw new ArgumentException($"Could not find {windowsExecutableName} in {path}", nameof(windowsExecutableName), e); //TODO Karl this should terminate the application flow
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // 1. Find using WHICH
            // https://ss64.com/bash/which.html -- alternatief https://ss64.com/bash/whereis.html

            try
            {
                var fullNames = RunSimpleProcess("which", $"{linuxExecutableName}").Split(Environment.NewLine);

                logger.LogDebug($"Found {fullNames.Length} instance(s) of {linuxExecutableName}. Returning the first one: {fullNames.First()}");

                return fullNames.First();
            }
            catch (ApplicationException e)
            {
                throw new ArgumentException($"Could not find {linuxExecutableName} in /", nameof(linuxExecutableName), e);
            }
        }
        else
            throw new NotSupportedException($"{RuntimeInformation.OSDescription} is not supported");
    }

    public static string RunSimpleProcess(string fileName, string arguments)
    {
        // https://github.com/Nicholi/OpenSSLCompat/blob/0e682c7b86e25bb219b742792afc839b21f44e44/OpenSSLCompat/Program.cs#L137

        using var process = new Process();

        string errorMsg = string.Empty;
        string output = string.Empty;

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.OutputDataReceived += (_, data) => output += data.Data + Environment.NewLine;
        process.ErrorDataReceived += (_, data) => errorMsg += data.Data ?? string.Empty;

        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        var cmdString = $"{process.StartInfo.FileName} {process.StartInfo.Arguments}";

        var started = process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new ApplicationException($"Error in process execution { fileName} {arguments}: {(string.IsNullOrEmpty(errorMsg) ? output : errorMsg)}");

        return output;
    }


    //public Match Execute(string arguments, string regex, out string rawOutput)
    //{
    //    rawOutput = Execute(arguments);

    //    var match = Regex.Match(rawOutput, regex, RegexOptions.Singleline);

    //    if (!match.Success)
    //    {
    //        var executableName = new FileInfo(_executableFullName).Name;
    //        throw new ApplicationException($"Error parsing output for {executableName}{Environment.NewLine}Output:{Environment.NewLine}{rawOutput}");
    //    }

    //    return match;
    //}

    //public void Execute<T1>(string arguments, string regex, string t1Name, out string rawOutput, out T1 t1)
    //{
    //    var regexMatch = Execute(arguments, regex, out rawOutput);

    //    t1 = (T1)Convert.ChangeType(regexMatch.Groups[t1Name].Value, typeof(T1));
    //}
    //public void Execute<T1, T2, T3, T4>(string arguments, string regex, string t1Name, string t2Name, string t3Name, string t4Name, out string rawOutput, out T1 t1, out T2 t2, out T3 t3, out T4 t4)
    //{
    //    var regexMatch = Execute(arguments, regex, out rawOutput);

    //    t1 = (T1)Convert.ChangeType(regexMatch.Groups[t1Name].Value, typeof(T1));
    //    t2 = (T2)Convert.ChangeType(regexMatch.Groups[t2Name].Value, typeof(T2));
    //    t3 = (T3)Convert.ChangeType(regexMatch.Groups[t3Name].Value, typeof(T3));
    //    t4 = (T4)Convert.ChangeType(regexMatch.Groups[t4Name].Value, typeof(T4));
    //}
    //public void Execute<T1, T2, T3, T4, T5>(string arguments, string regex, string t1Name, string t2Name, string t3Name, string t4Name, string t5Name, out string rawOutput, out T1 t1, out T2 t2, out T3 t3, out T4 t4, out T5 t5)
    //{
    //    var regexMatch = Execute(arguments, regex, out rawOutput);

    //    t1 = (T1)Convert.ChangeType(regexMatch.Groups[t1Name].Value, typeof(T1));
    //    t2 = (T2)Convert.ChangeType(regexMatch.Groups[t2Name].Value, typeof(T2));
    //    t3 = (T3)Convert.ChangeType(regexMatch.Groups[t3Name].Value, typeof(T3));
    //    t4 = (T4)Convert.ChangeType(regexMatch.Groups[t4Name].Value, typeof(T4));
    //    t5 = (T5)Convert.ChangeType(regexMatch.Groups[t5Name].Value, typeof(T5));
    //}
}