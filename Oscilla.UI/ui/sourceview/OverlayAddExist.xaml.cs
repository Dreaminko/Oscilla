using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Oscilla.UI
{
    public partial class OverlayAddExist : UserControl
    {
        public OverlayAddExist()
        {
            InitializeComponent();
        }

        public void Show(string message)
        {
            MessageText.Text = message;
            this.Visibility = Visibility.Visible;

            // 优雅的淡入动画
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => this.Visibility = Visibility.Collapsed;
            this.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}