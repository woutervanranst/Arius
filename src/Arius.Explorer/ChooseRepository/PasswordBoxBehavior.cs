using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;

namespace Arius.Explorer.ChooseRepository;

public class PasswordBoxBehavior : Behavior<PasswordBox>
{
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.Register(
            nameof(Password),
            typeof(string),
            typeof(PasswordBoxBehavior),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordPropertyChanged));

    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PasswordChanged += OnPasswordChanged;
    }

    protected override void OnDetaching()
    {
        AssociatedObject.PasswordChanged -= OnPasswordChanged;
        base.OnDetaching();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var passwordBox = sender as PasswordBox;
        if (passwordBox != null && passwordBox.Password != Password)
        {
            Password = passwordBox.Password;
        }
    }

    private static void OnPasswordPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBoxBehavior behavior && behavior.AssociatedObject != null)
        {
            var newPassword = e.NewValue as string ?? string.Empty;
            if (behavior.AssociatedObject.Password != newPassword)
            {
                behavior.AssociatedObject.Password = newPassword;
            }
        }
    }
}