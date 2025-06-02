using Arius.Core;
using Arius.Core.Commands;
using Arius.Core.Models;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System.Collections.Concurrent;

namespace Arius.Cli;

internal class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddUserSecrets<Program>();
        builder.Services.AddArius(c => { });

        // Application Insights automatically picks up connection string from appsettings
        builder.Services.AddApplicationInsightsTelemetryWorkerService();

        var host = builder.Build();

        var mediator = host.Services.GetRequiredService<IMediator>();
        var config   = host.Services.GetRequiredService<IConfiguration>();

        AnsiConsole.Write(
            new FigletText("Arius")
                .LeftJustified()
                .Color(Color.Red));

        await AnsiConsole.Progress()
            .AutoRefresh(true)
            .AutoClear(false)
            .HideCompleted(true)
            .Columns(
                new ElapsedTimeColumn(),
                //new SpinnerColumn(Spinner.Known.Star),
                new ProgressBarColumn(),
                new TaskDescriptionColumn() { Alignment = Justify.Right }
                //new PaddedTaskDescriptionColumn(50),
                //new TextPathColumn(),
            )
            .StartAsync(async ctx =>
            {
                try
                {
                    var queue = new ConcurrentQueue<ProgressUpdate>();
                    var pu    = new Progress<ProgressUpdate>(u => queue.Enqueue(u));

                    // 3. Send the MediatR command and wait for completion
                    // The handler will report progress via 'progressUpdates'
                    var c = new ArchiveCommand
                    {
                        AccountName      = config["ArchiveSettings:AccountName"] ?? throw new ArgumentException("A"),
                        AccountKey       = config["ArchiveSettings:AccountKey"] ?? throw new ArgumentException("AccountKey must be configured in user secrets"),
                        ContainerName    = config["ArchiveSettings:ContainerName"] ?? throw new ArgumentException("C"),
                        Passphrase       = config["ArchiveSettings:Passphrase"] ?? throw new ArgumentException("Passphrase must be configured in user secrets"),
                        RemoveLocal      = bool.Parse(config["ArchiveSettings:RemoveLocal"] ?? "false"),
                        Tier             = Enum.Parse<StorageTier>(config["ArchiveSettings:Tier"] ?? "Cool"),
                        LocalRoot        = new DirectoryInfo(config["ArchiveSettings:LocalRoot"] ?? throw new ArgumentException("D")),
                        ProgressReporter = pu
                    };

                    var t = mediator.Send(c);


                    var taskDictionary = new ConcurrentDictionary<string, ProgressTask>();

                    while (!t.IsCompleted)
                    {
                        while (queue.TryDequeue(out var u))
                        {
                            if (u is TaskProgressUpdate tpu)
                            {
                                var task = taskDictionary.GetOrAdd(tpu.TaskName, taskName => ctx.AddTask($"[blue]{taskName}[/]").IsIndeterminate());

                                // Optionally display some extra status text in the description
                                if (!string.IsNullOrWhiteSpace(tpu.StatusMessage))
                                {
                                    // E.g. "[blue]file.txt[/] (Reading... 50%)"
                                    task.Description = $"[blue]{tpu.TaskName}[/] ({tpu.StatusMessage})";
                                }

                                // If the file is complete, we can stop the task
                                if (tpu.Percentage >= 100)
                                    task.StopTask();
                            }
                            else if (u is FileProgressUpdate fpu)
                            {
                                var task = taskDictionary.GetOrAdd(fpu.FileName, fileName => ctx.AddTask($"[blue]{fileName}[/]"));

                                // Optionally display some extra status text in the description
                                if (!string.IsNullOrWhiteSpace(fpu.StatusMessage))
                                {
                                    // E.g. "[blue]file.txt[/] (Reading... 50%)"
                                    task.Description = $"[blue]{TruncateAndRightJustify(fpu.FileName, 50)}[/] ({TruncateAndLeftJustify(fpu.StatusMessage, 20)})";
                                }

                                task.Value = fpu.Percentage;

                                // If the file is complete, we can stop the task
                                if (fpu.Percentage >= 100)
                                    task.StopTask();
                            }
                            else
                                throw new ArgumentException();
                        }
                    }

                    await t; // to ensure error propagation

                    // Once the handler completes, all updates should have been reported
                    AnsiConsole.MarkupLine("[green]All files processed![/]");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }

            });
    }

    static string TruncateAndRightJustify(string input, int width)
    {
        //if (width <= 0) 
        //    return string.Empty;

        //string truncated = input.Length > width ? input[^width..] : input;

        //return truncated.PadLeft(width);

        if (width <= 0) return string.Empty;

        const string ellipsis     = "...";
        int          contentWidth = width - ellipsis.Length;

        // Ensure there's enough space for the ellipsis
        if (contentWidth <= 0)
        {
            return ellipsis[..width];
        }

        // Truncate from the left and prepend with ellipsis
        string truncated = input.Length > contentWidth
            ? ellipsis + input[^contentWidth..]
            : input;

        // Right justify the resulting string by padding it on the left
        return truncated.PadLeft(width);
    }

    static string TruncateAndLeftJustify(string input, int width)
    {
        if (width <= 0) 
            return string.Empty;

        string truncated = input.Length > width ? input[..width] : input;

        return truncated.PadRight(width);
    }
}

//internal sealed class PaddedTaskDescriptionColumn : ProgressColumn
//{
//    private readonly int _width;

//    public PaddedTaskDescriptionColumn(int width)
//    {
//        _width = width;
//    }

//    // Disable wrapping so we can manually handle it (pad or truncate).
//    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
//    {
//        var description = task.Description ?? string.Empty;

//        // Truncate if too long
//        if (description.Length > _width)
//        {
//            description = description.Substring(0, _width - 1) + "…";
//        }

//        // Pad right if too short
//        if (description.Length < _width)
//        {
//            description = description.PadRight(_width);
//        }

//        return new Markup(description.EscapeMarkup());
//    }

//    protected override bool NoWrap => true;
//}

//internal sealed class TextPathColumn : ProgressColumn
//{
//    // Optional: let you set a custom style or justification, etc.
//    public Style?   RootStyle      { get; init; }
//    public Style?   SeparatorStyle { get; init; }
//    public Style?   StemStyle      { get; init; }
//    public Style?   LeafStyle      { get; init; }
//    public Justify? Justification  { get; init; }

//    // Disable wrapping so Spectre.Console will rely on your `TextPath` for measuring.
//    protected override bool NoWrap => true;

//    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
//    {
//        // If there's no description, just return a blank text
//        if (string.IsNullOrWhiteSpace(task.Description))
//        {
//            return new Markup("[grey]No path[/]");
//        }

//        // Create a new TextPath from the task description
//        var path = new TextPath(task.Description)
//        {
//            RootStyle      = RootStyle ?? Style.Plain,
//            SeparatorStyle = SeparatorStyle ?? Style.Plain,
//            StemStyle      = StemStyle ?? Style.Plain,
//            LeafStyle      = LeafStyle ?? Style.Plain,
//            Justification  = Justification
//        };

//        return path;
//    }
//}