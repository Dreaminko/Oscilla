using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Oscilla.UI
{
    public partial class OverlayChangeDiff : UserControl
    {
        public OverlayChangeDiff()
        {
            InitializeComponent();
        }

        public void ShowDiff(string header, List<string> newFiles, List<string> missingFiles)
        {
            StatusTitle.Text = header;

            // 转换路径，只显示歌名，保持界面精简美观
            NewFilesList.ItemsSource = ConvertToNames(newFiles);
            MissingFilesList.ItemsSource = ConvertToNames(missingFiles);

            this.Visibility = Visibility.Visible;
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            this.BeginAnimation(OpacityProperty, fadeIn);
        }

        private List<string> ConvertToNames(List<string> paths)
        {
            var list = new List<string>();
            foreach (var path in paths)
            {
                try { list.Add(Path.GetFileName(path)); }
                catch { list.Add(path); }
            }
            if (list.Count == 0) list.Add("(无变动)");
            return list;
        }

        private void Dismiss_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => {
                this.Visibility = Visibility.Collapsed;
                NewFilesList.ItemsSource = null;
                MissingFilesList.ItemsSource = null;
            };
            this.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}