using Microsoft.Extensions.Logging;

namespace Arius.Explorer.RepositoryExplorer;

/// <summary>
/// Interaction logic for Window.xaml
/// </summary>
public partial class Window
{
    private readonly ILogger<Window> logger;

    public Window(ILogger<Window> logger, RepositoryExplorerViewModel viewModel)
    {
        this.logger = logger;

        InitializeComponent();
        DataContext = viewModel;

        logger.LogInformation("Repository Explorer Window initialized");
    }
}