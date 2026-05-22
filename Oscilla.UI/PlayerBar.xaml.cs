using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // 必须引入它才能识别拖拽事件
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Oscilla.Core;

namespace Oscilla.UI
{
    public partial class PlayerBar : UserControl
    {
        private bool _isPlaying = false;
        private Storyboard _flowAnimation;
        private double _totalSeconds = 0;

        private DispatcherTimer _volumeTimer;
        private bool _isVolumeExpanded = false;

        private bool _isUserDragging = false;

        private readonly Geometry _playGeometry = Geometry.Parse("M 2,0 L 6,0 L 6,14 L 2,14 Z M 10,0 L 14,0 L 14,14 L 10,14 Z"); // Play 按钮图标 (原先的赋值，我没动)
        private readonly Geometry _pauseGeometry = Geometry.Parse("M 2,0 L 14,7 L 2,14 Z"); // Pause 按钮图标 (原先的赋值，我没动)

        public PlayerBar()
        {
            InitializeComponent();
            CreateFlowAnimation();

            _volumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _volumeTimer.Tick += VolumeTimer_Tick;

            // ==========================================
            // 订阅 AudioEngine 实时反馈
            // ==========================================
            AudioEngine.Instance.PositionChanged += (current, total, percent) =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateProgress(current);
                });
            };

            AudioEngine.Instance.TrackEnded += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    // 【注释已更新】：如果开启了单曲循环(uni)，底层引擎(NAudio)会内部重置进度，根本不会触发这个事件
                    LibraryManager.PlayNext();
                });
            };

            // ==========================================
            // 订阅全局切歌事件
            // ==========================================
            LibraryManager.TrackChanged += (track) =>
            {
                if (track == null) return;

                // 【注释已更新】：调用 NAudio 内核播放
                AudioEngine.Instance.Play(track);

                // 更新 UI 元数据
                UpdateTrackInfo(
                    track.Title,
                    track.Bitrate,
                    track.SampleRate,
                    track.BitDepth,
                    ConvertDurationToSeconds(track.Duration)
                );

                _isPlaying = true;
                UpdateUIState(true);
            };
        }

        // ！！！仅在这里做了修改：隐藏 MP3/AAC 的 0bit ！！！
        public void UpdateTrackInfo(string title, int bitrate, double sampleRate, int bitDepth, double durationSeconds)
        {
            TrackNameText.Text = title.ToUpper();

            if (bitDepth > 0)
            {
                // 无损格式，正常显示位深
                AudioSpecs.Text = $"{bitrate}kbps | {sampleRate:F1}kHz | {bitDepth}bit";
            }
            else
            {
                // MP3等有损格式，直接隐藏位深参数
                AudioSpecs.Text = $"{bitrate}kbps | {sampleRate:F1}kHz";
            }

            _totalSeconds = durationSeconds;

            TimeSpan t = TimeSpan.FromSeconds(durationSeconds);
            TotalTimeText.Text = string.Format("{0:D2}:{1:D2}", (int)t.TotalMinutes, t.Seconds);

            TimelineSlider.Value = 0;
            TimelineSlider.Maximum = durationSeconds;

            _isPlaying = true;
            UpdateUIState(true);
        }

        public void UpdateProgress(double currentSeconds)
        {
            if (!_isUserDragging)
            {
                TimelineSlider.Value = currentSeconds;
            }
        }

        private void ToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _isPlaying = !_isPlaying;
            UpdateUIState(_isPlaying);

            if (_isPlaying) AudioEngine.Instance.Resume();
            else AudioEngine.Instance.Pause();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VolumeText != null) VolumeText.Text = $"{(int)VolumeSlider.Value}";

            AudioEngine.Instance.Volume = (float)(VolumeSlider.Value / 100.0);

            if (_isVolumeExpanded && _volumeTimer != null)
            {
                _volumeTimer.Stop();
                _volumeTimer.Start();
            }
        }

        private double ConvertDurationToSeconds(string duration)
        {
            if (string.IsNullOrEmpty(duration)) return 0;
            try
            {
                var parts = duration.Split(':');
                if (parts.Length == 2)
                {
                    return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
                }
            }
            catch { }
            return 0;
        }

        private void CreateFlowAnimation()
        {
            DoubleAnimation anim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(2.5),
                RepeatBehavior = RepeatBehavior.Forever
            };
            _flowAnimation = new Storyboard();
            var brush = (LinearGradientBrush)this.Resources["AuroraFlowBrush"];
            Storyboard.SetTarget(anim, brush);
            Storyboard.SetTargetProperty(anim, new PropertyPath("RelativeTransform.(TranslateTransform.X)"));
            brush.RelativeTransform = new TranslateTransform();
        }

        private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (CurrentTimeText == null) return;
            TimeSpan time = TimeSpan.FromSeconds(TimelineSlider.Value);
            CurrentTimeText.Text = string.Format("{0:D2}:{1:D2}", (int)time.TotalMinutes, time.Seconds);
        }

        private void TimelineSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isUserDragging = true;
        }

        private void TimelineSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isUserDragging = false;
            AudioEngine.Instance.Seek(TimelineSlider.Value);
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e) => LibraryManager.PlayNext();
        private void PrevBtn_Click(object sender, RoutedEventArgs e) => LibraryManager.PlayPrevious();

        private void LoopMode_Click(object sender, RoutedEventArgs e)
        {
            if (LoopModeBtn.Content.ToString().Equals("all"))
            {
                LoopModeBtn.Content = "uni";
                LoopModeBtn.Foreground = (Brush)this.Resources["AuroraFaintBrush"];
                LoopModeBtn.BorderBrush = (Brush)this.Resources["AuroraFaintBrush"];
                AudioEngine.Instance.IsLooping = true;
            }
            else
            {
                LoopModeBtn.Content = "all";
                LoopModeBtn.ClearValue(Button.ForegroundProperty);
                LoopModeBtn.ClearValue(Button.BorderBrushProperty);
                AudioEngine.Instance.IsLooping = false;
            }
        }

        private void AnimateButtonColors(Button btn, Color targetFgColor, Color targetBorderColor, TimeSpan duration)
        {
            if (!(btn.Foreground is SolidColorBrush fgBrush) || fgBrush.IsFrozen)
            {
                var baseColor = btn.Foreground is SolidColorBrush sb ? sb.Color : Color.FromRgb(160, 160, 160);
                fgBrush = new SolidColorBrush(baseColor);
                btn.Foreground = fgBrush;
            }
            ColorAnimation fgAnim = new ColorAnimation { To = targetFgColor, Duration = duration, EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            fgBrush.BeginAnimation(SolidColorBrush.ColorProperty, fgAnim);

            if (!(btn.BorderBrush is SolidColorBrush borderBrush) || borderBrush.IsFrozen)
            {
                var baseColor = btn.BorderBrush is SolidColorBrush sb ? sb.Color : Color.FromRgb(63, 63, 63);
                borderBrush = new SolidColorBrush(baseColor);
                btn.BorderBrush = borderBrush;
            }
            ColorAnimation borderAnim = new ColorAnimation { To = targetBorderColor, Duration = duration, EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
        }

        // ==========================================
        // 【正式版】：真机接入的热切换逻辑 (NORM -> WAIT -> ASIO/FAIL)
        // ==========================================
        private async void OutputModeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!OutputModeBtn.IsEnabled) return;

            var auroraColor = Color.FromRgb(0, 255, 171);      // 极光青 (ASIO)
            var waitBlueColor = Color.FromRgb(0, 209, 255);    // 科技蓝 (WAIT)
            var redColor = Colors.Red;                         // 警告红 (FAIL)
            var grayFg = Color.FromRgb(160, 160, 160);         // 默认灰
            var grayBorder = Color.FromRgb(63, 63, 63);        // 边框灰

            if (OutputModeBtn.Content.ToString().Equals("NORM"))
            {
                OutputModeBtn.IsEnabled = false;

                // 1. 握手阶段：蓝色 WAIT
                OutputModeBtn.Content = "WAIT";
                AnimateButtonColors(OutputModeBtn, waitBlueColor, waitBlueColor, TimeSpan.FromMilliseconds(200));

                // 给出 600ms 让旧引擎停转，并让用户感知到切换过程
                await System.Threading.Tasks.Task.Delay(600);

                // 2. 真正调用底层 AudioEngine 抢占声卡独占权
                bool success = AudioEngine.Instance.SwitchMode(OutputMode.Asio);

                if (success)
                {
                    // 3a. 握手成功：青色 ASIO
                    OutputModeBtn.Content = "ASIO";
                    AnimateButtonColors(OutputModeBtn, auroraColor, auroraColor, TimeSpan.FromMilliseconds(300));

                    await System.Threading.Tasks.Task.Delay(300);
                    OutputModeBtn.IsEnabled = true;
                }
                else
                {
                    // 3b. 握手失败（硬件被占用/不存在）：红色 FAIL
                    OutputModeBtn.Content = "FAIL";
                    AnimateButtonColors(OutputModeBtn, redColor, redColor, TimeSpan.FromMilliseconds(200));

                    await System.Threading.Tasks.Task.Delay(2000);

                    // 4. 自动撤退：退回 NORM
                    OutputModeBtn.Content = "NORM";
                    AnimateButtonColors(OutputModeBtn, grayFg, grayBorder, TimeSpan.FromMilliseconds(400));

                    await System.Threading.Tasks.Task.Delay(400);

                    OutputModeBtn.ClearValue(Button.ForegroundProperty);
                    OutputModeBtn.ClearValue(Button.BorderBrushProperty);
                    OutputModeBtn.IsEnabled = true;
                }
            }
            else
            {
                // 切回标准模式
                OutputModeBtn.IsEnabled = false;

                // 退回时同样给出短暂蓝色握手反馈
                OutputModeBtn.Content = "WAIT";
                AnimateButtonColors(OutputModeBtn, waitBlueColor, waitBlueColor, TimeSpan.FromMilliseconds(200));

                await System.Threading.Tasks.Task.Delay(400);

                // 真正调用底层切回 Standard 混音器
                AudioEngine.Instance.SwitchMode(OutputMode.Standard);

                OutputModeBtn.Content = "NORM";
                AnimateButtonColors(OutputModeBtn, grayFg, grayBorder, TimeSpan.FromMilliseconds(300));

                await System.Threading.Tasks.Task.Delay(300);
                OutputModeBtn.ClearValue(Button.ForegroundProperty);
                OutputModeBtn.ClearValue(Button.BorderBrushProperty);
                OutputModeBtn.IsEnabled = true;
            }
        }

        private void VolumeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_isVolumeExpanded)
            {
                _isVolumeExpanded = true;
                _volumeTimer.Stop();
                DoubleAnimation widthAnim = new DoubleAnimation { To = 150, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
                VolumeBtn.BeginAnimation(WidthProperty, widthAnim);
                _volumeTimer.Start();
            }
            else
            {
                _volumeTimer.Stop();
                _volumeTimer.Start();
            }
        }

        private void VolumeTimer_Tick(object sender, EventArgs e)
        {
            _isVolumeExpanded = false;
            _volumeTimer.Stop();
            DoubleAnimation widthAnim = new DoubleAnimation { To = 34, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            VolumeBtn.BeginAnimation(WidthProperty, widthAnim);
        }

        private void VolumeSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => _volumeTimer.Stop();
        private void VolumeSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (_isVolumeExpanded) _volumeTimer.Start(); }

        private void UpdateUIState(bool playing)
        {
            var aurora = (Brush)FindResource("AuroraStaticBrush");
            var auroraFlow = (Brush)FindResource("AuroraFlowBrush");
            var grayBorder = new SolidColorBrush(Color.FromRgb(63, 63, 63));
            var grayText = (Brush)FindResource("GrayTextBrush");
            var grayInactive = (Brush)FindResource("GrayInactiveBrush");
            var white = Brushes.White;

            DoubleAnimation fadeAnim = new DoubleAnimation { To = playing ? 1.0 : 0.0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            AuroraBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

            Border elapsedTrack = null;
            if (TimelineSlider.Template != null) elapsedTrack = TimelineSlider.Template.FindName("ElapsedTrack", TimelineSlider) as Border;

            AudioSpecs.Foreground = grayBorder;

            if (playing)
            {
                _flowAnimation.Begin();
                TimeDot.Fill = aurora;
                CurrentTimeText.Foreground = aurora;
                TrackNameText.Foreground = white;
                if (elapsedTrack != null) elapsedTrack.Background = auroraFlow;
                ToggleIconPath.Data = _pauseGeometry;
                ToggleBtn.Foreground = aurora;
                ToggleBtn.BorderBrush = aurora;
            }
            else
            {
                _flowAnimation.Stop();
                TimeDot.Fill = grayBorder;
                CurrentTimeText.Foreground = grayText;
                TrackNameText.Foreground = grayText;
                if (elapsedTrack != null) elapsedTrack.Background = grayInactive;
                ToggleIconPath.Data = _playGeometry;
                ToggleBtn.ClearValue(Button.ForegroundProperty);
                ToggleBtn.ClearValue(Button.BorderBrushProperty);
            }

            if (LoopModeBtn.Content.ToString().Equals("uni"))
            {
                LoopModeBtn.Foreground = playing ? aurora : (Brush)FindResource("AuroraFaintBrush");
                LoopModeBtn.BorderBrush = playing ? aurora : (Brush)FindResource("AuroraFaintBrush");
            }
            else
            {
                LoopModeBtn.Foreground = grayText;
                LoopModeBtn.ClearValue(Button.BorderBrushProperty);
            }
        }
    }
}