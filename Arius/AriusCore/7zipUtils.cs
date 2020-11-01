using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace AriusCore
{
    internal class ZipUtils
    {
        public ZipUtils()
        {

        }

        private string _sevenZipPath = @"C:\Program Files\7-Zip\7z.exe";

        public void Compress(string sourceFile, string targetFile, string password)
        {
            try
            {
                using (var proc = new Process())
                {
                    proc.StartInfo.FileName = _sevenZipPath;

                    // -mhe=on      = HEADER ENCRYPTION
                    // -mx0         = NO COMPRESSION
                    proc.StartInfo.Arguments = $"a -p{password} \"{targetFile}\" \"{sourceFile}\" -mhe=on -mx0";

                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.StartInfo.RedirectStandardError = true;

                    bool hasError = false;
                    string errorMsg = string.Empty;

                    proc.OutputDataReceived += (sender, data) => System.Diagnostics.Debug.WriteLine(data.Data);
                    proc.ErrorDataReceived += (sender, data) =>
                    {
                        if (data.Data == null)
                            return;

                        System.Diagnostics.Debug.WriteLine(data.Data);

                        hasError = true;
                        errorMsg += data.Data;
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();

                    proc.WaitForExit();

                    if (proc.ExitCode != 0 || hasError)
                    {
                        //7z output codes https://superuser.com/questions/519114/how-to-write-error-status-for-command-line-7-zip-in-variable-or-instead-in-te

                        if (File.Exists(targetFile))
                            File.Delete(targetFile);

                        throw new ApplicationException($"Error while compressing :  {errorMsg}");
                    }
                }
            }
            catch (Win32Exception e) when (e.Message == "The system cannot find the file specified.")
            {
                //7zip not installed
                throw new ApplicationException("7Zip CLI Not Installed", e);
            }
        }
    }
}
