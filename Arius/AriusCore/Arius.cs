using System;
using System.IO;
using System.Security.Permissions;
using System.Threading.Tasks;

namespace AriusCore
{
    public class Arius
    {
        public Arius(string path)
        {
            _path = path;
        }

        private string _path;
        private static FileSystemWatcher _watcher = new FileSystemWatcher();

        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        public void Monitor()
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
                var x = _watcher.WaitForChanged(WatcherChangeTypes.All);

                

                Task.Delay(100);
            }

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
