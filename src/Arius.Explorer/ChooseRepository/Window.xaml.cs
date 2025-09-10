namespace Arius.Explorer.ChooseRepository;

/// <summary>
/// Interaction logic for Window.xaml
/// </summary>
public partial class Window
{
    public Window(WindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}