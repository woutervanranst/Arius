using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Arius.UI;

/// <summary>
/// Interaction logic for RepositoryExplorerWindow.xaml
/// </summary>
public partial class RepositoryExplorerWindow : Window
{
    public RepositoryExplorerWindow(ExploreRepositoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

public class RepositoryChosenMessage
{
    public RepositoryChosenMessage(Repository repository)
    {
        ChosenRepository = repository;
    }

    public Repository ChosenRepository { get; }
}
