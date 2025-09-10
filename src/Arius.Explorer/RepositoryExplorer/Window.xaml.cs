using Microsoft.Extensions.Logging;

namespace Arius.Explorer.RepositoryExplorer;

/// <summary>
/// Interaction logic for Window.xaml
/// </summary>
public partial class Window
{
    private readonly ILogger<Window> logger;
    private readonly WindowViewModel viewModel;

    public Window(ILogger<Window> logger, WindowViewModel viewModel)
    {
        this.logger = logger;
        this.viewModel = viewModel;
        
        InitializeComponent();
        DataContext = viewModel;
        
        logger.LogInformation("Repository Explorer Window initialized");
    }
}