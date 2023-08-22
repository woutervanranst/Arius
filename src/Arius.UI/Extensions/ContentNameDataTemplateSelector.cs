using Arius.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Arius.UI.Extensions
{
    internal class ContentNameDataTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var element = container as FrameworkElement;
            var itemViewModel = item as ItemViewModel;

            if (itemViewModel.IsDeleted)
                return element.FindResource("DeletedContentNameTemplate") as DataTemplate;
            else
                return element.FindResource("ContentNameTemplate") as DataTemplate;
        }
    }
}
