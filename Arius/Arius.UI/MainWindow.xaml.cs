using Arius.Repositories;
using Arius.UI.Properties;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Arius.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            //this.DataContext = new MainViewModel();
        }

        //public IEnumerable<string> ha
        //{
        //    get
        //    {
        //        return new List<string>() { "haha", "hehe" };
        //    }
        //}
    }

    internal static class StringExtensions
    {
        public static string Protect(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            byte[] entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
            byte[] data = Encoding.UTF8.GetBytes(value);
            string protectedData = Convert.ToBase64String(ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser));
            return protectedData;
        }

        public static string Unprotect(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            byte[] protectedData = Convert.FromBase64String(value);
            byte[] entropy = Encoding.UTF8.GetBytes(Assembly.GetExecutingAssembly().FullName);
            string data = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser));
            return data;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {

        public MainViewModel()
        {
            AccountName = Settings.Default.AccountName;
            AccountKey = Settings.Default.AccountKey.Unprotect();
        }

        public string AccountName
        {
            get => storageAccountName;
            set
            {
                storageAccountName = value;
                
                Settings.Default.AccountName = value;
                Settings.Default.Save();

                LoadContainers();
            }
        }
        private string storageAccountName;

        public string AccountKey
        {
            get => storageAccountKey;
            set
            {
                storageAccountKey = value;

                Settings.Default.AccountKey = value.Protect();
                Settings.Default.Save();

                LoadContainers();
            }
        }
        private string storageAccountKey;

        private void LoadContainers()
        {
            Task.Run(() =>
            {
                if (string.IsNullOrEmpty(AccountName) || string.IsNullOrEmpty(AccountKey))
                    return;

                try
                {
                    var asa = new AzureStorageAccount(AccountName, AccountKey);
                    Containers = asa.GetAzureRepositoryNames().Select(containerName => new ContainerViewModel(AccountName, AccountKey, containerName));
                }
                catch (Exception e) when (e is FormatException || e is StorageException)
                {
                    MessageBox.Show("Invalid combination of Account Name/Key", App.Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    return;
                }

                SelectedContainer = Containers.First();
                OnPropertyChanged(nameof(SelectedContainer));
                OnPropertyChanged(nameof(Containers));
            });
        }

        public IEnumerable<ContainerViewModel> Containers { get; private set; }


        public ContainerViewModel SelectedContainer { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ContainerViewModel
    {
        public ContainerViewModel(string accountName, string accountKey, string containerName)
        {
            Name = containerName;
        }
        public string Name { get; init; }
    }
}
