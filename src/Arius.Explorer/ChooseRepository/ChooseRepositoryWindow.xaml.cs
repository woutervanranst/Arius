namespace Arius.Explorer.ChooseRepository;

/// <summary>
/// Interaction logic for Window.xaml
/// </summary>
public partial class Window
{
    public Window(ChooseRepositoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}