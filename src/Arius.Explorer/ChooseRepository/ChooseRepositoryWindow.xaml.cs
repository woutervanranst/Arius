using Microsoft.Extensions.Logging;

namespace Arius.Explorer.ChooseRepository;

/// <summary>
/// Interaction logic for ChooseRepositoryWindow.xaml
/// </summary>
public partial class ChooseRepositoryWindow
{
    private readonly ILogger<ChooseRepositoryWindow> logger;

    public ChooseRepositoryWindow(ILogger<ChooseRepositoryWindow> logger, ChooseRepositoryViewModel viewModel)
    {
        this.logger = logger;

        InitializeComponent();
        DataContext = viewModel;

        logger.LogInformation("Choose Repository ChooseRepositoryWindow initialized");
    }
}