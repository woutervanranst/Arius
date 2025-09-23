using Microsoft.Extensions.Logging;

namespace Arius.Explorer.ChooseRepository;

/// <summary>
/// Interaction logic for Window.xaml
/// </summary>
public partial class Window
{
    private readonly ILogger<Window> logger;

    public Window(ILogger<Window> logger, ChooseRepositoryViewModel viewModel)
    {
        this.logger = logger;

        InitializeComponent();
        DataContext = viewModel;

        logger.LogInformation("Choose Repository Window initialized");
    }
}