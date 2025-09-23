using Arius.Explorer.Shared.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace Arius.Explorer.ChooseRepository;

/// <summary>
/// Interaction logic for Window.xaml
/// </summary>
public partial class Window : IRecipient<CloseChooseRepositoryDialogMessage>
{
    public Window(ChooseRepositoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Register for close messages
        WeakReferenceMessenger.Default.Register<CloseChooseRepositoryDialogMessage>(this);
    }

    public void Receive(CloseChooseRepositoryDialogMessage message)
    {
        // Set dialog result and close
        DialogResult = true;
        Close();
    }


    protected override void OnClosed(EventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<CloseChooseRepositoryDialogMessage>(this);
        base.OnClosed(e);
    }
}