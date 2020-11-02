using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Security.Permissions;
using System.Threading.Tasks;

namespace AriusCore
{
    public class Arius
    {
        public Arius(string path, string passphrase, string accountName, string accountKey)
        {
            _path = path;

            _zip = new ZipUtils(passphrase);
            _blobUtils = new BlobUtils(accountName, accountKey);
        }

        private static FileSystemWatcher _watcher = new FileSystemWatcher();
        private ZipUtils _zip;
        private BlobUtils _blobUtils;

        private string _path;
        
        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public async Task Monitor()
        {

            // Create a new FileSystemWatcher and set its properties.
            _watcher.Path = _path;

            //// Watch for changes in LastAccess and LastWrite times, and
            //// the renaming of files or directories.
            //_watcher.NotifyFilter = NotifyFilters.LastAccess
            //                     | NotifyFilters.LastWrite
            //                     | NotifyFilters.FileName
            //                     | NotifyFilters.DirectoryName;

            // Watch all files.
            _watcher.Filter = "";

            //// Add event handlers.
            //_watcher.Changed += OnChanged;
            //_watcher.Created += OnChanged;
            //_watcher.Deleted += OnChanged;
            //_watcher.Renamed += OnRenamed;

            // Begin watching.
            //_watcher.EnableRaisingEvents = true;

            _watcher.IncludeSubdirectories = true;

            while (true)
            {
                var cgh = _watcher.WaitForChanged(WatcherChangeTypes.All);

                // TODO IGNORE DIRECTORY

                if (cgh.ChangeType == WatcherChangeTypes.Created)
                {
                    var source = Path.Combine(_path, cgh.Name);
                    var blobTarget = Path.Combine(_path, $"{cgh.Name}.7z.arius");
                    var localTarget = Path.Combine(_path, $"{cgh.Name}.arius");

                    // Compress & encrypt the file
                    try
                    {
                        _zip.Compress(source, blobTarget);
                    }
                    catch (Exception e)
                    {
                        continue; //TODO
                    }

                    // Move the file to blob
                    string blobName;
                    try
                    {
                        blobName = await _blobUtils.Upload(blobTarget);
                    }
                    catch (Exception e)
                    {
                        continue;
                    }

                    //Replace the file by the reference
                    try
                    {
                        File.WriteAllText(localTarget, blobName, System.Text.Encoding.UTF8);
                        
                        //Delete the source
                        File.Delete(source);
                    }
                    catch (Exception e)
                    {

                    }

                }

                await Task.Delay(100);
            }

        }

        private void Proc_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            //throw new NotImplementedException();
        }

        // Define the event handlers.
        private void OnChanged(object source, FileSystemEventArgs e) =>
            // Specify what is done when a file is changed, created, or deleted.
            Console.WriteLine($"File: {e.FullPath} {e.ChangeType}");

        private void OnRenamed(object source, RenamedEventArgs e) =>
            // Specify what is done when a file is renamed.
            Console.WriteLine($"File: {e.OldFullPath} renamed to {e.FullPath}");


    }
}
