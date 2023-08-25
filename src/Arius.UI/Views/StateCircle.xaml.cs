using System.Windows;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using UserControl = System.Windows.Controls.UserControl;

namespace Arius.UI.Views
{
    /// <summary>
    /// Interaction logic for CustomCircle.xaml
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
            DependencyProperty.Register("LeftOuterColor", typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));

        public Brush RightOuterColor
        {
            get { return (Brush)GetValue(RightOuterColorProperty); }
            set { SetValue(RightOuterColorProperty, value); }
        }

        public static readonly DependencyProperty RightOuterColorProperty =
            DependencyProperty.Register("RightOuterColor", typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));

        public Brush LeftInnerColor
        {
            get { return (Brush)GetValue(LeftInnerColorProperty); }
            set { SetValue(LeftInnerColorProperty, value); }
        }

        public static readonly DependencyProperty LeftInnerColorProperty =
            DependencyProperty.Register("LeftInnerColor", typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));

        public Brush RightInnerColor
        {
            get { return (Brush)GetValue(RightInnerColorProperty); }
            set { SetValue(RightInnerColorProperty, value); }
        }

        public static readonly DependencyProperty RightInnerColorProperty =
            DependencyProperty.Register("RightInnerColor", typeof(Brush), typeof(StateCircle), new UIPropertyMetadata(Brushes.Transparent));
    }
}
