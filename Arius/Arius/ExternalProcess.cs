using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

//using System.IO.FileSystem.AccessControl; // Security.AccessControl.DirectorySecurity;

namespace Arius
{
    class ExternalProcess
    {
        
        public static string FindFullName(ILogger logger, string windowsExecutableName, string linuxExecutableName)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                throw new NotImplementedException();

            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 1. Find in the PATH folders
                var executables = path.Split(';')
                    .Select(dir => new DirectoryInfo(dir))
                    .SelectMany(dir => dir.TryGetFiles(windowsExecutableName))
                    .ToArray();

                if (executables.Length > 0)
                {
                    var fullname = executables.First().FullName;

                    logger.LogInformation($"Found {executables.Length} instance(s) of {windowsExecutableName}. Returning the first one: {fullname}");

                    return fullname;
                }

                // 2. Find using WHERE
                path = Path.GetPathRoot(Environment.SystemDirectory);
                var p = new ExternalProcess("where");

                try
                {
                    logger.LogWarning($"Did not find {windowsExecutableName} in PATH variable. Searching on {path}. Consider adding the location to the PATH variable to improve speed.");
                    var fullNames = p.Execute($" /R {path} {windowsExecutableName}").Split(Environment.NewLine);

                    logger.LogInformation($"Found {fullNames.Length} instance(s) of {windowsExecutableName}. Returning the first one: {fullNames.First()}");

                    return fullNames.First();
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException($"Could not find {windowsExecutableName} in {path}", nameof(windowsExecutableName), e);
                }
            }
            else
                throw new NotImplementedException();
        }
        public ExternalProcess(string executableFullName)
        {
            _executableFullName = executableFullName;
        }

        private string _executableFullName;

        public string Execute(string arguments)
        {
            try
            {
                // https://developers.redhat.com/blog/2019/10/29/the-net-process-class-on-linux/

                using var process = new Process();

                bool hasError = false;
                string errorMsg = string.Empty;
                string output = string.Empty;

                var psi = new ProcessStartInfo
                {
                    FileName = _executableFullName,

                    UseShellExecute = false,
                    Arguments = arguments,

                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                process.StartInfo = psi;
                process.OutputDataReceived += (sender, data) => output += data.Data + Environment.NewLine; //System.Diagnostics.Debug.WriteLine(data.Data);
                process.ErrorDataReceived += (sender, data) =>
                {
                    if (data.Data == null)
                        return;

                    System.Diagnostics.Debug.WriteLine(data.Data);

                    hasError = true;
                    errorMsg += data.Data;
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();


                process.WaitForExit();

                if (process.ExitCode != 0 || hasError)
                    throw new ApplicationException(errorMsg);

                return output;

            }
            catch (Win32Exception e) // Win32Exception: 'The system cannot find the file specified.'
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public Match Execute(string arguments, string regex)
        {
            var o = Execute(arguments);

            var match = Regex.Match(o, regex);

            if (!match.Success)
            {
                var executableName = new FileInfo(_executableFullName).Name;
                throw new ApplicationException($"Error parsing output for {executableName}{Environment.NewLine}Output:{Environment.NewLine}{o}");
            }

            return match;
        }

        public void Execute<T1, T2, T3, T4>(string arguments, string regex, string t1Name, string t2Name, string t3Name, string t4Name, out T1 t1, out T2 t2, out T3 t3, out T4 t4)
        {
            var output = Execute(arguments, regex);

            t1 = (T1)Convert.ChangeType(output.Groups[t1Name].Value, typeof(T1));
            t2 = (T2)Convert.ChangeType(output.Groups[t2Name].Value, typeof(T2));
            t3 = (T3)Convert.ChangeType(output.Groups[t3Name].Value, typeof(T3));
            t4 = (T4)Convert.ChangeType(output.Groups[t4Name].Value, typeof(T4));
        }
    }
}
