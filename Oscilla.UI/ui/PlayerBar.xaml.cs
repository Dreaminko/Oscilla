using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // 这里引入了 WPF 自带的 Primitives.Track 控件
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

// ==========================================================================================
// 【核心修复】：切断错乱的旧引用，接通全新的音频核心层与业务逻辑层
// ==========================================================================================
using Oscilla.Core;   // 引入音频核心层
using Oscilla.Logic;  // 引入业务逻辑层，认出 LibraryManager
using Oscilla.Models; // 引入数据模型层

// ==========================================================================================
// 【终极必杀技】：显式定义别名！
// 明确告诉编译器，本文件中的 "Track" 一律指代音频数据模型的 Track，死死压制住 WPF 的内置控件冲突！
// ==========================================================================================
using Track = Oscilla.Core.Track;

namespace Oscilla.UI
{
    public partial class PlayerBar : UserControl
    {
        private bool _isPlaying = false;
        private Storyboard? _flowAnimation;
        private double _totalSeconds = 0;

        private DispatcherTimer? _volumeTimer;
        private bool _isVolumeExpanded = false;

        private bool _isUserDragging = false;

        private Track? _currentTrack; // 【已修复】得益于别名，现在这里被精准识别为音频音轨模型

        // 存储当前播放列表的上下文信息
        private string _currentPlaylistId = "";

        private readonly Geometry _playGeometry = Geometry.Parse("M 2,0 L 6,0 L 6,14 L 2,14 Z M 10,0 L 14,0 L 14,14 L 10,14 Z");
        private readonly Geometry _pauseGeometry = Geometry.Parse("M 2,0 L 14,7 L 2,14 Z");

        public PlayerBar()
        {
            InitializeComponent();
            CreateFlowAnimation();
            _volumeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _volumeTimer!.Tick += VolumeTimer_Tick;

            // 订阅 AudioEngine 实时反馈
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
                    LibraryManager.PlayNext();
                });
            };

            // 订阅全局切歌事件
            LibraryManager.TrackChanged += (track) =>
            {
                if (track == null) return;

                // 记住这首正在播放的歌
                _currentTrack = track;

                // 调用 NAudio 内核播放
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

        // ==========================================================================================
        // 设置当前播放列表上下文
        // ==========================================================================================
        public void SetCurrentPlaylistContext(string playlistId)
        {
            _currentPlaylistId = playlistId;
        }

        // ==========================================================================================
        // 获取当前播放列表上下文
        // ==========================================================================================
        public string GetCurrentPlaylistContext()
        {
            return _currentPlaylistId;
        }

        // ==========================================================================================
        // 点击歌名跳转逻辑 - 使用当前播放列表上下文
        // ==========================================================================================
        private void TrackNameText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentTrack != null)
            {
                // 获取上层承载 PlayerBar 的 MainWindow
                var window = Window.GetWindow(this);
                if (window is MainWindow mainWindow)
                {
                    string targetView = _currentPlaylistId;

                    // 如果当前播放列表上下文为空，则跳转到"AllSongs"
                    if (string.IsNullOrEmpty(targetView))
                    {
                        targetView = "AllSongs";
                    }

                    // 调用 MainWindow 的跳转方法，传入视图ID和歌曲
                    mainWindow.NavigateToTrack(targetView, _currentTrack);
                }
            }
        }

        public void UpdateTrackInfo(string title, int bitrate, double sampleRate, int bitDepth, double durationSeconds)
        {
            if (TrackNameText != null) TrackNameText.Text = title?.ToUpper() ?? "未知歌曲";
            if (bitDepth > 0)
            {
                // 无损格式，正常显示位深
                if (AudioSpecs != null) AudioSpecs.Text = $"{bitrate}kbps | {sampleRate:F1}kHz | {bitDepth}bit";
            }
            else
            {
                // MP3等有损格式，直接隐藏位深参数
                if (AudioSpecs != null) AudioSpecs.Text = $"{bitrate}kbps | {sampleRate:F1}kHz";
            }
            _totalSeconds = durationSeconds;
            TimeSpan t = TimeSpan.FromSeconds(durationSeconds);
            if (TotalTimeText != null) TotalTimeText.Text = string.Format("{0:D2}:{1:D2}", (int)t.TotalMinutes, t.Seconds);
            if (TimelineSlider != null)
            {
                TimelineSlider.Value = 0;
                TimelineSlider.Maximum = durationSeconds;
            }
            _isPlaying = true;
            UpdateUIState(true);
        }

        public void UpdateProgress(double currentSeconds)
        {
            if (!_isUserDragging && TimelineSlider != null)
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
            var brush = TryFindResource("AuroraFlowBrush") as LinearGradientBrush;
            if (brush != null)
            {
                _flowAnimation = new Storyboard();
                Storyboard.SetTarget(anim, brush);
                Storyboard.SetTargetProperty(anim, new PropertyPath("RelativeTransform.(TranslateTransform.X)"));
                brush.RelativeTransform = new TranslateTransform();
            }
            else
            {
                _flowAnimation = null;
            }
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
            if (LoopModeBtn == null) return;
            if (LoopModeBtn.Content?.ToString()?.Equals("all") == true)
            {
                LoopModeBtn.Content = "uni";
                var auroraFaintBrush = TryFindResource("AuroraFaintBrush") as Brush;
                if (auroraFaintBrush != null)
                {
                    LoopModeBtn.Foreground = auroraFaintBrush;
                    LoopModeBtn.BorderBrush = auroraFaintBrush;
                }
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
            if (btn == null) return;
            if (!(btn.Foreground is SolidColorBrush fgBrush) || fgBrush.IsFrozen)
            {
                var baseColor = btn.Foreground is SolidColorBrush sb ? sb.Color : Color.FromRgb(160, 160, 160);
                fgBrush = new SolidColorBrush(baseColor);
                btn.Foreground = fgBrush;
            }
            ColorAnimation fgAnim = new ColorAnimation
            {
                To = targetFgColor,
                Duration = duration,
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            fgBrush?.BeginAnimation(SolidColorBrush.ColorProperty, fgAnim);

            if (!(btn.BorderBrush is SolidColorBrush borderBrush) || borderBrush.IsFrozen)
            {
                var baseColor = btn.BorderBrush is SolidColorBrush sb ? sb.Color : Color.FromRgb(63, 63, 63);
                borderBrush = new SolidColorBrush(baseColor);
                btn.BorderBrush = borderBrush;
            }
            ColorAnimation borderAnim = new ColorAnimation
            {
                To = targetBorderColor,
                Duration = duration,
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            borderBrush?.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
        }

        private async void OutputModeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (OutputModeBtn == null || !OutputModeBtn.IsEnabled) return;

            var auroraColor = Color.FromRgb(0, 255, 171);
            var waitBlueColor = Color.FromRgb(0, 209, 255);
            var redColor = Colors.Red;
            var grayFg = Color.FromRgb(160, 160, 160);
            var grayBorder = Color.FromRgb(63, 63, 63);

            if (OutputModeBtn.Content?.ToString()?.Equals("NORM") == true)
            {
                OutputModeBtn.IsEnabled = false;
                OutputModeBtn.Content = "WAIT";
                AnimateButtonColors(OutputModeBtn, waitBlueColor, waitBlueColor, TimeSpan.FromMilliseconds(200));
                await System.Threading.Tasks.Task.Delay(600);

                bool success = AudioEngine.Instance.SwitchMode(OutputMode.Asio);
                if (success)
                {
                    OutputModeBtn.Content = "ASIO";
                    AnimateButtonColors(OutputModeBtn, auroraColor, auroraColor, TimeSpan.FromMilliseconds(300));
                    await System.Threading.Tasks.Task.Delay(300);
                    OutputModeBtn.IsEnabled = true;
                }
                else
                {
                    OutputModeBtn.Content = "FAIL";
                    AnimateButtonColors(OutputModeBtn, redColor, redColor, TimeSpan.FromMilliseconds(200));
                    await System.Threading.Tasks.Task.Delay(2000);

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
                OutputModeBtn.IsEnabled = false;
                OutputModeBtn.Content = "WAIT";
                AnimateButtonColors(OutputModeBtn, waitBlueColor, waitBlueColor, TimeSpan.FromMilliseconds(200));
                await System.Threading.Tasks.Task.Delay(400);

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
            if (VolumeBtn == null || !_isVolumeExpanded)
            {
                _isVolumeExpanded = true;
                _volumeTimer?.Stop();
                if (VolumeBtn != null)
                {
                    DoubleAnimation widthAnim = new DoubleAnimation
                    {
                        To = 150,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                    };
                    VolumeBtn.BeginAnimation(WidthProperty, widthAnim);
                }
                _volumeTimer?.Start();
            }
            else
            {
                _volumeTimer?.Stop();
                _volumeTimer?.Start();
            }
        }

        private void VolumeTimer_Tick(object? sender, EventArgs e)
        {
            _isVolumeExpanded = false;
            _volumeTimer?.Stop();
            if (VolumeBtn != null)
            {
                DoubleAnimation widthAnim = new DoubleAnimation
                {
                    To = 34,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                VolumeBtn.BeginAnimation(WidthProperty, widthAnim);
            }
        }

        private void VolumeSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _volumeTimer?.Stop();
        private void VolumeSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isVolumeExpanded) _volumeTimer?.Start();
        }

        private void UpdateUIState(bool playing)
        {
            if (AuroraBorder == null || TimelineSlider == null || TimeDot == null || CurrentTimeText == null || TrackNameText == null || ToggleIconPath == null || ToggleBtn == null || AudioSpecs == null) return;

            var aurora = TryFindResource("AuroraStaticBrush") as Brush;
            var auroraFlow = TryFindResource("AuroraFlowBrush") as Brush;
            var grayBorder = new SolidColorBrush(Color.FromRgb(63, 63, 63));
            var grayText = TryFindResource("GrayTextBrush") as Brush;
            var grayInactive = TryFindResource("GrayInactiveBrush") as Brush;
            var white = Brushes.White;

            if (aurora == null || auroraFlow == null || grayText == null || grayInactive == null) return;

            DoubleAnimation fadeAnim = new DoubleAnimation
            {
                To = playing ? 1.0 : 0.0,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            AuroraBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

            Border? elapsedTrack = null;
            if (TimelineSlider.Template != null)
                elapsedTrack = TimelineSlider.Template.FindName("ElapsedTrack", TimelineSlider) as Border;

            AudioSpecs.Foreground = grayBorder;

            if (playing)
            {
                _flowAnimation?.Begin();
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
                _flowAnimation?.Stop();
                TimeDot.Fill = grayBorder;
                CurrentTimeText.Foreground = grayText;
                TrackNameText.Foreground = grayText;
                if (elapsedTrack != null) elapsedTrack.Background = grayInactive;
                ToggleIconPath.Data = _playGeometry;
                ToggleBtn.ClearValue(Button.ForegroundProperty);
                ToggleBtn.ClearValue(Button.BorderBrushProperty);
            }

            if (LoopModeBtn != null)
            {
                if (LoopModeBtn.Content?.ToString()?.Equals("uni") == true)
                {
                    var auroraFaintBrush = TryFindResource("AuroraFaintBrush") as Brush;
                    if (auroraFaintBrush != null)
                    {
                        LoopModeBtn.Foreground = playing ? aurora : auroraFaintBrush;
                        LoopModeBtn.BorderBrush = playing ? aurora : auroraFaintBrush;
                    }
                }
                else
                {
                    if (grayText != null)
                    {
                        LoopModeBtn.Foreground = grayText;
                        LoopModeBtn.ClearValue(Button.BorderBrushProperty);
                    }
                }
            }
        }
    }
}