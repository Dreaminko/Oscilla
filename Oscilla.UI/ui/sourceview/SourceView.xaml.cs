using System;
using System.Collections.Generic; // 用于 HashSet 和 List
using System.Diagnostics;
using System.Linq; // 用于强大的 Diff 集合比对计算
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading; // 用于时间轴定时器

// =================================================================
// 【核心修复】：切断旧的 UI.core 引用，拉通全新的、剥离了 UI. 的标准三层架构
// =================================================================
using Oscilla.Core;         // 引入核心音频层，认出 Track 类
using Oscilla.Models;       // 引入数据模型层，认出 LibrarySource 类
using Oscilla.Logic;        // 引入业务逻辑层，认出 SourceManager 和 LibraryManager

namespace Oscilla.UI
{
    // ==========================================
    // 【核心转换器】：负责读取每一行对应的 Log 文件并转换为相对时间
    // ==========================================
    public class LogTimeToRelativeConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string logPath && System.IO.File.Exists(logPath))
            {
                try
                {
                    // 使用 ReadWrite 共享权限读取，避免文件冲突
                    using (var fs = new System.IO.FileStream(logPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    using (var reader = new System.IO.StreamReader(fs))
                    {
                        string? firstLine = reader.ReadLine();
                        if (firstLine != null && firstLine.StartsWith("# LAST_EDITED:"))
                        {
                            string timeStr = firstLine.Substring(14).Trim();
                            if (DateTime.TryParse(timeStr, out DateTime dt))
                            {
                                TimeSpan diff = DateTime.Now - dt;
                                if (diff.TotalSeconds < 60) return "(JUST NOW)";
                                if (diff.TotalMinutes < 60) return $"({(int)diff.TotalMinutes}M AGO)";
                                if (diff.TotalHours < 24) return $"({(int)diff.TotalHours}H AGO)";

                                // 超过一年的改成 YEAR AGO
                                if (diff.TotalDays >= 365)
                                {
                                    int years = (int)(diff.TotalDays / 365);
                                    return $"({years} YEAR AGO)";
                                }

                                return $"({dt:MM/dd})";
                            }
                        }
                    }
                }
                catch { }
            }
            return ""; 
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class SourceView : UserControl
    {
        // 定时器声明为可空类型（加?），彻底解决生命周期导致的 CS8618 构造函数未初始化警告
        private DispatcherTimer? _timeTrackerTimer;

        public SourceView()
        {
            InitializeComponent();
            LoadSources();

            // 每次进入和离开窗口时，管理列表刷新定时器
            this.Loaded += (s, e) => StartTimeTracker();
            this.Unloaded += (s, e) => StopTimeTracker();
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }

        // ==========================================
        // 【刷新引擎】：让每一行的时间每隔几秒自动更新
        // ==========================================
        private void StartTimeTracker()
        {
            if (_timeTrackerTimer == null)
            {
                _timeTrackerTimer = new DispatcherTimer();
                _timeTrackerTimer.Interval = TimeSpan.FromSeconds(10); // 每10秒批量刷新一次时间
                _timeTrackerTimer.Tick += (s, e) =>
                {
                    if (SourceListView.Items.Count > 0)
                    {
                        SourceListView.Items.Refresh();
                    }
                };
            }
            _timeTrackerTimer.Start();
        }

        private void StopTimeTracker()
        {
            _timeTrackerTimer?.Stop();
        }

        private void LoadSources()
        {
            SourceListView.ItemsSource = null;
            // 安全增强：加入的时候默认把库开关关闭
            if (SourceManager.RegisteredSources != null)
            {
                foreach (var source in SourceManager.RegisteredSources)
                {
                    if (source != null) source.IsEnabled = false;
                }
            }
            SourceListView.ItemsSource = SourceManager.RegisteredSources;
        }

        // ==========================================
        // 【智能添加逻辑】：移除 Toast 提示，改用系统级别或控制台静默处理
        // ==========================================
        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a music folder to add to Oscilla."
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FolderName ?? "";
                if (string.IsNullOrEmpty(selectedPath)) return;

                try
                {
                    // 调用底层嗅探逻辑
                    var result = SourceManager.TryAddSource(selectedPath);
                    if (result.source == null) return;

                    switch (result.status)
                    {
                        case AddSourceResult.AlreadyExists:
                            MessageBox.Show("该文件夹已在您的音乐库中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            break;

                        case AddSourceResult.CreatedNew:
                            SourceManager.ConfirmAdd(result.source);
                            Logger.LogSystemAction("AddSource", $"Added new folder: {result.source.SourceName ?? "UNKNOWN"}", result.source.LogPath ?? "");
                            SourceListView.Items.Refresh();
                            OnSourceCollectionChanged(result.source);
                            break;

                        case AddSourceResult.DetectedExisting:
                            SourceManager.ConfirmAdd(result.source);
                            Logger.LogSystemAction("AddExistingSource", $"Restored folder: {result.source.SourceName ?? "UNKNOWN"}", result.source.LogPath ?? "");
                            SourceListView.Items.Refresh();
                            OnSourceCollectionChanged(result.source);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"添加失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // 添加/删除后的统一刷新动作
        private void OnSourceCollectionChanged(LibrarySource? focusSource = null)
        {
            LoadSources();
            if (focusSource != null)
            {
                SourceListView.SelectedItem = focusSource;
                SourceListView.ScrollIntoView(focusSource);
            }
        }

        // 点击路径打开系统资源管理器
        private void Path_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                try { Process.Start("explorer.exe", tb.Text); }
                catch (Exception) { /* 忽略错误 */ }
            }
        }

        // ==========================================
        // 【智能差异嗅探】：物理覆写，后续可在这里对接你的独立新通知 UI
        // ==========================================
        private void RefreshSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LibrarySource source)
            {
                string folderPath = source.FolderPath ?? "";
                if (string.IsNullOrEmpty(folderPath) || !System.IO.Directory.Exists(folderPath))
                {
                    MessageBox.Show("刷新失败：源文件夹在硬盘上丢失！", "同步错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (source.Tracks == null) source.Tracks = new List<Track>();

                // 1. 播放平滑旋转动画
                DoubleAnimation spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                btn.RenderTransformOrigin = new Point(0.5, 0.5);
                btn.RenderTransform = new RotateTransform();
                var rotateTransform = btn.RenderTransform as RotateTransform;
                rotateTransform?.BeginAnimation(RotateTransform.AngleProperty, spin);

                // 2. 支持的音频格式后缀
                var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".flac", ".wav", ".mp3", ".aac", ".m4a", ".ape", ".ogg"
                };

                // 获取硬盘上真实的音频文件
                var physicalFiles = System.IO.Directory.GetFiles(folderPath, "*.*", System.IO.SearchOption.AllDirectories)
                                    .Where(f => supportedExts.Contains(System.IO.Path.GetExtension(f) ?? ""))
                                    .ToList();

                // 超级路径清洗器
                Func<string?, string> cleanPath = p =>
                {
                    if (string.IsNullOrWhiteSpace(p)) return "";
                    try { return System.IO.Path.GetFullPath(p).TrimEnd('\\'); }
                    catch { return p.Replace('/', '\\').Trim(); }
                };

                var existingPaths = source.Tracks
                                    .Where(t => t != null && !string.IsNullOrEmpty(t.FilePath))
                                    .Select(t => cleanPath(t.FilePath))
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var physicalPaths = physicalFiles.Select(p => cleanPath(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // 3. 开始计算差异 (Diff)
                var newFiles = physicalPaths.Except(existingPaths).ToList();    
                var missingFiles = existingPaths.Except(physicalPaths).ToList();

                if (newFiles.Count == 0 && missingFiles.Count == 0)
                {
                    return; // 库源无任何变化时静默退出或后续挂载轻量状态栏
                }

                // 4. 生成变动报告并弹窗询问
                string reportMsg = $"在库源 '{source.SourceName ?? "UNKNOWN"}' 中嗅探到物理文件的变动：\n\n" +
                                   $"✨ 发现了 {newFiles.Count} 首新歌\n" +
                                   $"🗑️ 丢失了 {missingFiles.Count} 首旧歌\n\n" +
                                   $"是否要将这些变化更新到本地的 Log 并覆盖配置？";

                var result = MessageBox.Show(reportMsg, "发现文件变动", MessageBoxButton.YesNo, MessageBoxImage.Question);

                // 5. 用户点击了确认：开始覆盖 Log
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        source.Tracks.RemoveAll(t => t != null && missingFiles.Contains(cleanPath(t.FilePath)));

                        foreach (string newPath in newFiles)
                        {
                            string rawTitle = System.IO.Path.GetFileNameWithoutExtension(newPath) ?? "UNKNOWN";
                            string rawExt = (System.IO.Path.GetExtension(newPath) ?? "").Replace(".", "").ToUpper();

                            source.Tracks.Add(new Track
                            {
                                FilePath = newPath,
                                Title = rawTitle,
                                Artist = "UNKNOWN",
                                Duration = "--:--",
                                Format = rawExt
                            });
                        }

                        SourceManager.WriteSourceLog(source);

                        var reloadedTracks = SourceManager.LoadFromSongDatabase(folderPath);
                        if (reloadedTracks != null)
                        {
                            source.Tracks = reloadedTracks;
                        }

                        Logger.LogSystemAction("SyncSource", $"Synced '{source.SourceName ?? "UNKNOWN"}'. Added: {newFiles.Count}, Removed: {missingFiles.Count}", source.LogPath ?? "");

                        SourceManager.SaveSources();
                        LibraryManager.RefreshLibrary();
                        SourceListView.Items.Refresh();
                        OnSourceCollectionChanged(source);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"数据同步写入失败: {ex.Message}", "同步失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // 卸载库源逻辑 (带垃圾桶图标)
        private void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LibrarySource source)
            {
                var confirm = MessageBox.Show(
                    $"确定要移除库源 '{source.SourceName ?? "UNKNOWN"}' 吗？\n\n...注意：这仅从软件中卸载，不会删除您的音乐文件。",
                    "卸载确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    if (source.IsEnabled)
                    {
                        source.IsEnabled = false;
                        LibraryManager.RefreshLibrary(); 
                    }

                    Logger.LogSystemAction("RemoveSource", $"Removed folder: {source.SourceName ?? "UNKNOWN"}", source.LogPath ?? "");

                    SourceManager.RemoveSource(source);
                    OnSourceCollectionChanged();
                    SourceListView.Items.Refresh();

                    if (SourceListView.SelectedItem == null)
                    {
                        RightPanelContainer.Opacity = 0;
                    }
                }
            }
        }

        // 开关切换
        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.DataContext is LibrarySource source)
            {
                UpdateRightPanel(source);

                Logger.LogSystemAction("ToggleSource", $"Set '{source.SourceName ?? "UNKNOWN"}' status to: {source.IsEnabled}", source.LogPath ?? "");

                SourceManager.SaveSources();
                LibraryManager.RefreshLibrary();
                SourceListView.Items.Refresh();
            }
        }

        private void SourceListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceListView.SelectedItem is LibrarySource source)
            {
                UpdateRightPanel(source);
            }
            else
            {
                RightPanelContainer.Opacity = 0;
            }
        }

        private void UpdateRightPanel(LibrarySource source)
        {
            string sourceName = source.SourceName ?? "UNKNOWN";
            PreviewName.Text = sourceName.ToUpper();

            if (source.IsEnabled)
            {
                PreviewStatus.Text = "ACTIVE";
                PreviewStatus.Foreground = (Brush)FindResource("AuroraBrush");
            }
            else
            {
                PreviewStatus.Text = "DISABLED";
                PreviewStatus.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85));
            }

            PreviewTrackCount.Text = (source.Tracks?.Count ?? 0).ToString();
            PreviewLogStatus.Text = System.IO.File.Exists(source.LogPath ?? "") ? "LOCAL.LOG" : "PENDING";

            FolderInitial.Text = !string.IsNullOrEmpty(sourceName) ? sourceName.Substring(0, 1).ToUpper() : "";

            DoubleAnimation fadeAnim = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(150));
            DoubleAnimation slideAnim = new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            if (!(RightPanelContainer.RenderTransform is TranslateTransform))
            {
                RightPanelContainer.RenderTransform = new TranslateTransform();
            }

            RightPanelContainer.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            var translateTransform = RightPanelContainer.RenderTransform as TranslateTransform;
            translateTransform?.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }
    }
}