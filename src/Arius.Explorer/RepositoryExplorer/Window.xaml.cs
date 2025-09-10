using Microsoft.Extensions.Logging;
using System.Windows;

namespace Arius.Explorer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> logger;

    public MainWindow(ILogger<MainWindow> logger)
    {
        this.logger = logger;
        InitializeComponent();
        
        logger.LogInformation("MainWindow initialized");
    }
}