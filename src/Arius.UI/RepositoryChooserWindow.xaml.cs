using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Arius.UI;

/// <summary>
/// Interaction logic for RepositoryChooserWindow.xaml
/// </summary>
public partial class RepositoryChooserWindow : Window
{
    public RepositoryChooserWindow()
    {
        InitializeComponent();
    }
}

public class ChooseRepositoryViewModel : ObservableObject
{
    private readonly IExternalFacade _facade;
    private readonly IMessenger      _messenger;

    public ChooseRepositoryViewModel(IExternalFacade facade, IMessenger messenger)
    {
        _facade    = facade;
        _messenger = messenger;

        Repositories  = new ObservableCollection<Repository>(_facade.ListRepositories());
        ChooseCommand = new RelayCommand(ChooseRepository);
    }

    public ObservableCollection<Repository> Repositories { get; }

    public Repository SelectedRepository { get; set; }

    public ICommand ChooseCommand { get; }

    private void ChooseRepository()
    {
        // Send a message to indicate which repository was chosen
        _messenger.Send(new RepositoryChosenMessage(SelectedRepository));
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