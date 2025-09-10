using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Arius.Explorer.RepositoryExplorer;

/// <summary>
/// Interaction logic for StateCircle.xaml
/// </summary>
public partial class StateCircle : UserControl
{
    public StateCircle()
    {
        InitializeComponent();
    }

    public Brush LeftOuterColor
    {
        get { return (Brush)GetValue(LeftOuterColorProperty); }
        set { SetValue(LeftOuterColorProperty, value); }
    }

    public static readonly DependencyProperty LeftOuterColorProperty =
        DependencyProperty.Register(nameof(LeftOuterColor), typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));

    public Brush RightOuterColor
    {
        get { return (Brush)GetValue(RightOuterColorProperty); }
        set { SetValue(RightOuterColorProperty, value); }
    }

    public static readonly DependencyProperty RightOuterColorProperty =
        DependencyProperty.Register(nameof(RightOuterColor), typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));

    public Brush LeftInnerColor
    {
        get { return (Brush)GetValue(LeftInnerColorProperty); }
        set { SetValue(LeftInnerColorProperty, value); }
    }

    public static readonly DependencyProperty LeftInnerColorProperty =
        DependencyProperty.Register(nameof(LeftInnerColor), typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));

    public Brush RightInnerColor
    {
        get { return (Brush)GetValue(RightInnerColorProperty); }
        set { SetValue(RightInnerColorProperty, value); }
    }

    public static readonly DependencyProperty RightInnerColorProperty =
        DependencyProperty.Register(nameof(RightInnerColor), typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));
}