//using System;
//using System.ComponentModel;
//using System.Diagnostics;
//using System.IO;
//using System.Text.RegularExpressions;

//namespace Arius
//{
//    class ExternalProcess
//    {
//        public ExternalProcess(string executableFullName)
//        {
//            _executableFullName = executableFullName;
//        }

//        private string _executableFullName;

//        public string Execute(string arguments)
//        {
//            try
//            {
//                // https://developers.redhat.com/blog/2019/10/29/the-net-process-class-on-linux/

//                using var process = new Process();

//                bool hasError = false;
//                string errorMsg = string.Empty;
//                string output = string.Empty;

//                var psi = new ProcessStartInfo
//                {
//                    FileName = _executableFullName,

//                    UseShellExecute = false,
//                    Arguments = arguments,

//                    RedirectStandardError = true,
//                    RedirectStandardOutput = true
//                };

//                process.StartInfo = psi;
//                process.OutputDataReceived += (sender, data) => output += data.Data + Environment.NewLine; //System.Diagnostics.Debug.WriteLine(data.Data);
//                process.ErrorDataReceived += (sender, data) =>
//                {
//                    if (data.Data == null)
//                        return;

//                    System.Diagnostics.Debug.WriteLine(data.Data);

//                    hasError = true;
//                    errorMsg += data.Data;
//                };

//                process.Start();
//                process.BeginOutputReadLine();
//                process.BeginErrorReadLine();


//                process.WaitForExit();

//                if (process.ExitCode != 0 || hasError)
//                    throw new ApplicationException(errorMsg);

//                return output;

//            }
//            catch (Win32Exception e) // Win32Exception: 'The system cannot find the file specified.'
//            {
//                Console.WriteLine(e);
//                throw;
//            }
//        }

//        public Match Execute(string arguments, string regex)
//        {
//            var o = Execute(arguments);

//            var match = Regex.Match(o, regex);

//            if (!match.Success)
//            {
//                var executableName = new FileInfo(_executableFullName).Name;
//                throw new ApplicationException($"Error parsing output for {executableName}{Environment.NewLine}Output:{Environment.NewLine}{o}");
//            }

//            return match;
//        }

//        public void Execute<T1, T2, T3, T4>(string arguments, string regex, string t1Name, string t2Name, string t3Name, string t4Name, out T1 t1, out T2 t2, out T3 t3, out T4 t4)
//        {
//            var output = Execute(arguments, regex);

//            t1 = (T1)Convert.ChangeType(output.Groups[t1Name].Value, typeof(T1));
//            t2 = (T2)Convert.ChangeType(output.Groups[t2Name].Value, typeof(T2));
//            t3 = (T3)Convert.ChangeType(output.Groups[t3Name].Value, typeof(T3));
//            t4 = (T4)Convert.ChangeType(output.Groups[t4Name].Value, typeof(T4));
//        }
//    }
//}
