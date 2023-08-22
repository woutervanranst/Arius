using System.Configuration;
using System.Data;
using System.Windows;

namespace Arius.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly RepositoryExplorer mainWindow;

        public App(RepositoryExplorer mainWindow)
        {
            this.mainWindow = mainWindow;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            mainWindow.Show();
            base.OnStartup(e);
        }
    }
}