using Arius.Facade;
using System;

namespace Arius.UI.ViewModels
{
    internal class ContainerViewModel : ViewModelBase
    {
        public ContainerViewModel(ContainerFacade cf)
        {
            this.cf = cf ?? throw new ArgumentNullException(nameof(ContainerViewModel.cf));
        }
        private readonly ContainerFacade cf;


        public string Name => cf.Name;

        public AzureRepositoryFacade GetAzureRepositoryFacade(string passphrase)
        {
            return cf.GetAzureRepositoryFacade(passphrase);
        }
    }
}
