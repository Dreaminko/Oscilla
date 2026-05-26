using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Track = Oscilla.Core.Track; // 别名保驾护航

namespace Oscilla.UI
{
    public partial class OverlayTrackCards : UserControl
    {
        public OverlayTrackCards()
        {
            InitializeComponent();
        }

        public void Show(string title, List<Track> tracks)
        {
            TitleText.Text = title;
            CardsItemsControl.ItemsSource = tracks;
            this.Visibility = Visibility.Visible;

            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => {
                this.Visibility = Visibility.Collapsed;
                CardsItemsControl.ItemsSource = null; // 释放数据源绑定
            };
            this.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}