using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Oscilla.UI
{
    public partial class OverlayRemoveSource : UserControl
    {
        // 定义一个事件，让父级 SourceView 知道用户点了删除
        public event Action? OnConfirmed;

        public OverlayRemoveSource()
        {
            InitializeComponent();
        }

        public void Show(string sourceName)
        {
            ConfirmText.Text = $"确定要移除库源 '{sourceName}' 吗？\n\n注意：这仅从列表中卸载，不会删除您的原始音乐文件。";
            this.Visibility = Visibility.Visible;
            this.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            OnConfirmed?.Invoke();
            Hide();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Hide();

        private void Hide()
        {
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) => this.Visibility = Visibility.Collapsed;
            this.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}