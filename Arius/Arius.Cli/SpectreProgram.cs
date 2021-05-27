//using Spectre.Console.Cli;
//using System;
//using System.ComponentModel;
//using System.Diagnostics.CodeAnalysis;

//namespace Arius.Cli
//{
//    public static class SpectreProgram
//    {
//        public static int Main(string[] args)
//        {
//            var app = new CommandApp();

//            app.Configure(config =>
//            {
//                config.SetApplicationName("arius");

//                config.AddCommand<ArchiveCommand>("archive")
//                    .WithDescription("Archive to blob");
//                config.AddCommand<RestoreCommand>("restore");

//                config.Settings.PropagateExceptions = true;
//                //, add =>
//                //{
//                //    //add
//                //});

//#if DEBUG
//                config.PropagateExceptions();
//                config.ValidateExamples();
//#endif
//            });

//            Environment.ExitCode = 1; //Set default to 1 (Error)
//            /*
//             * https://shapeshed.com/unix-exit-codes/
//             * 
//                What exit code should I use?
//                The Linux Documentation Project has a list of reserved codes that also offers advice on what code to use for specific scenarios. These are the standard error codes in Linux or UNIX.

//                1 - Catchall for general errors
//                2 - Misuse of shell builtins (according to Bash documentation)
//                126 - Command invoked cannot execute
//                127 - “command not found”
//                128 - Invalid argument to exit
//                128+n - Fatal error signal “n”
//                130 - Script terminated by Control-C
//                255\* - Exit status out of range
//            */

//            Environment.ExitCode = app.Run(args);

//            return Environment.ExitCode;
//        }
//    }

//    internal class ArchiveCommand : Command<ArchiveCommand.Settings>
//    {
//        public ArchiveCommand()
//        {
//        }

//        public sealed class Settings : CommandSettings
//        {
//            [CommandArgument(0, "<AccountName>")]
//            [Description("Blob Account Name")]
//            public string AccountName { get; init; }

//            [CommandOption("--accountkey|-k")]
//            [Description("Blob Account Key")]
//            public string AccountKey { get; init; }

//            public string Passphrase { get; init; }
//            public bool FastHash { get; init; }
//            public string Container { get; init; }
//            public bool RemoveLocal { get; init; }
//            public string Tier { get; init; }
//            public bool Dedup { get; init; }
//            public string Path { get; init; }
//        }

//        public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
//        {
//            //throw new NotImplementedException();

//            return 0;
//        }
//    }


//    internal class RestoreCommand : Command<RestoreCommand.Settings>
//    {
//        public sealed class Settings : CommandSettings
//        {
//            public string AccountName { get; init; }
//            public string AccountKey { get; init; }
//            public string Passphrase { get; init; }
//            public string Container { get; init; }
//            public bool Synchronize { get; init; }
//            public bool Download { get; init; }
//            public bool KeepPointers { get; init; }
//            public string Path { get; init; }
//        }

//        public override int Execute([NotNull] CommandContext context, [NotNull] Settings settings)
//        {
//            throw new NotImplementedException();
//        }
//    }
//}
