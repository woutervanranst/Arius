using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Arius.Extensions;
using Microsoft.Extensions.Logging;

namespace Arius.Services
{
    class ExternalProcess
    {
        public static string FindFullName(ILogger logger, string windowsExecutableName, string linuxExecutableName)
        {
            logger.LogDebug($"Looking for windows:{windowsExecutableName} / linux:{linuxExecutableName}");
            var path = Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrEmpty(path))
                logger.LogWarning("Environment variable PATH not found");
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 1. Find in the PATH folders
                if (!string.IsNullOrEmpty(path))
                { 
                    var executables = path.Trim(';').Split(';')
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
                var p = new ExternalProcess("where");

                try
                {
                    logger.LogWarning($"Did not find {windowsExecutableName} in PATH variable. Searching on {path}. Consider adding the location to the PATH variable to improve speed.");
                    var fullNames = p.Execute($" /R {path} {windowsExecutableName}").Split(Environment.NewLine);

                    logger.LogDebug($"Found {fullNames.Length} instance(s) of {windowsExecutableName}. Returning the first one: {fullNames.First()}");

                    return fullNames.First();
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException($"Could not find {windowsExecutableName} in {path}", nameof(windowsExecutableName), e);
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // 1. Find using WHICH
                var p = new ExternalProcess("which");

                try
                {
                    var fullNames = p.Execute($" / {linuxExecutableName}").Split(Environment.NewLine);

                    logger.LogDebug($"Found {fullNames.Length} instance(s) of {linuxExecutableName}. Returning the first one: {fullNames.First()}");

                    return fullNames.First();
                }
                catch (ArgumentException e)
                {
                    throw new ArgumentException($"Could not find {linuxExecutableName} in /", nameof(linuxExecutableName), e);
                }
            }
            
            throw new NotImplementedException();
        }


        public ExternalProcess(string executableFullName)
        {
            _executableFullName = executableFullName;
        }

        private string _executableFullName;

        //DataReceivedEventHandler? OutputDataReceived;

        //public event DataReceivedEventHandler? ErrorDataReceived;

//        internal void OutputReadNotifyUser(
//#nullable disable
//            string data)
//        {
//            DataReceivedEventHandler outputDataReceived = this.OutputDataReceived;
//            if (outputDataReceived == null)
//                return;
//            DataReceivedEventArgs e = new DataReceivedEventArgs(data);
//            ISynchronizeInvoke synchronizingObject = this.SynchronizingObject;
//            if (synchronizingObject != null && synchronizingObject.InvokeRequired)
//                synchronizingObject.Invoke((Delegate)outputDataReceived, new object[2]
//                {
//                    (object) this,
//                    (object) e
//                });
//            else
//                outputDataReceived((object)this, e);
//        }

        public string Execute(string arguments)
        {
            try
            {
                // https://developers.redhat.com/blog/2019/10/29/the-net-process-class-on-linux/

                using var process = new Process();

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
                process.OutputDataReceived += (_, data) => output += data.Data + Environment.NewLine;
                process.ErrorDataReceived += (_, data) => errorMsg += data.Data ?? string.Empty;
                
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();


                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new ApplicationException(string.IsNullOrEmpty(errorMsg) ? output : errorMsg);

                return output;

            }
            catch (Win32Exception e) // Win32Exception: 'The system cannot find the file specified.'
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public Match Execute(string arguments, string regex, out string rawOutput)
        {
            rawOutput = Execute(arguments);

            var match = Regex.Match(rawOutput, regex);

            if (!match.Success)
            {
                var executableName = new FileInfo(_executableFullName).Name;
                throw new ApplicationException($"Error parsing output for {executableName}{Environment.NewLine}Output:{Environment.NewLine}{rawOutput}");
            }

            return match;
        }

        public void Execute<T1, T2, T3, T4>(string arguments, string regex, string t1Name, string t2Name, string t3Name, string t4Name, out string rawOutput, out T1 t1, out T2 t2, out T3 t3, out T4 t4)
        {
            var regexMatch = Execute(arguments, regex, out rawOutput);

            t1 = (T1)Convert.ChangeType(regexMatch.Groups[t1Name].Value, typeof(T1));
            t2 = (T2)Convert.ChangeType(regexMatch.Groups[t2Name].Value, typeof(T2));
            t3 = (T3)Convert.ChangeType(regexMatch.Groups[t3Name].Value, typeof(T3));
            t4 = (T4)Convert.ChangeType(regexMatch.Groups[t4Name].Value, typeof(T4));
        }
    }
}
