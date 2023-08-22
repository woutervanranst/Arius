using Arius.Core.Facade;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
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
    public RepositoryExplorerWindow()
    {
        InitializeComponent();
    }
}

public class ExploreRepositoryViewModel : ObservableObject
{
    private RepositoryFacade currentRepository;

    public RepositoryFacade CurrentRepository
    {
        get => currentRepository;
        set => SetProperty(ref currentRepository, value);
    }

    public void SetRepository(RepositoryFacade repository)
    {
        CurrentRepository = repository;
        // Load additional data or perform other actions related to the chosen repository
    }
}