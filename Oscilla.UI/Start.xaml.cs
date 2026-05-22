using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Oscilla.UI
{
    public partial class Start : UserControl
    {
        public Start()
        {
            InitializeComponent();
            this.Loaded += Start_Loaded;
        }

        private void Start_Loaded(object sender, RoutedEventArgs e)
        {
            Storyboard sb = new Storyboard();
            IEasingFunction easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };

            // 1. 圆环呼吸放大 (从 0.9 缓慢放大到 1.0)
            DoubleAnimation scaleXAnim = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(1200)) { EasingFunction = easeOut };
            Storyboard.SetTarget(scaleXAnim, ScaleRing);
            Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("ScaleX"));
            sb.Children.Add(scaleXAnim);

            DoubleAnimation scaleYAnim = new DoubleAnimation(0.9, 1.0, TimeSpan.FromMilliseconds(1200)) { EasingFunction = easeOut };
            Storyboard.SetTarget(scaleYAnim, ScaleRing);
            Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleYAnim);

            // 2. 停留片刻后，整个界面淡出
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(600))
            {
                BeginTime = TimeSpan.FromMilliseconds(2000)
            };
            Storyboard.SetTarget(fadeOut, this);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath("Opacity"));
            sb.Children.Add(fadeOut);

            // 3. 动画结束后自毁释放资源
            sb.Completed += (s, ev) =>
            {
                if (this.Parent is Panel parent)
                {
                    parent.Children.Remove(this);
                }
            };

            sb.Begin();
        }
    }
}