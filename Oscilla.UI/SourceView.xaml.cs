using System;
using System.Collections.Generic; // 【新增】用于 HashSet 和 List
using System.Diagnostics;
using System.Linq; // 【新增】用于强大的 Diff 集合比对计算
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading; // 【新增】用于时间轴定时器
using Oscilla.Logic;
using Oscilla.Models;
using Oscilla.Core; // 【新增】用于引用 Track 和 LibraryManager

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
                        string firstLine = reader.ReadLine();
                        if (firstLine != null && firstLine.StartsWith("# LAST_EDITED:"))
                        {
                            string timeStr = firstLine.Substring(14).Trim();
                            if (DateTime.TryParse(timeStr, out DateTime dt))
                            {
                                TimeSpan diff = DateTime.Now - dt;
                                if (diff.TotalSeconds < 60) return "(JUST NOW)";
                                if (diff.TotalMinutes < 60) return $"({(int)diff.TotalMinutes}M AGO)";
                                if (diff.TotalHours < 24) return $"({(int)diff.TotalHours}H AGO)";

                                // 【新增逻辑】：超过一年的改成 YEAR AGO
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
            return ""; // 如果没有 Log 或读取失败，不显示任何文字
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class SourceView : UserControl
    {
        // ==========================================
        // 【新增】：时间轴刷新引擎
        // ==========================================
        private DispatcherTimer _timeTrackerTimer;

        public SourceView()
        {
            InitializeComponent();
            LoadSources();

            // 每次进入和离开窗口时，管理列表刷新定时器
            this.Loaded += (s, e) => StartTimeTracker();
            this.Unloaded += (s, e) => StopTimeTracker();
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
            SourceListView.ItemsSource = SourceManager.RegisteredSources;
        }

        // ==========================================
        // 【智能添加逻辑】：嗅探 Log 状态并弹窗确认
        // ==========================================
        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a music folder to add to Oscilla."
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FolderName;

                try
                {
                    // 调用底层嗅探逻辑
                    var result = SourceManager.TryAddSource(selectedPath);

                    switch (result.status)
                    {
                        case AddSourceResult.AlreadyExists:
                            MessageBox.Show("该文件夹已在您的库中。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            break;

                        case AddSourceResult.CreatedNew:
                            SourceManager.ConfirmAdd(result.source);

                            // 【修改点】：传入 source.LogPath
                            Logger.LogSystemAction("AddSource", $"Added new folder: {result.source.SourceName}", result.source.LogPath);

                            // 强制刷新列表，让转换器显示新时间
                            SourceListView.Items.Refresh();

                            MessageBox.Show("新库源扫描并创建成功！", "添加成功", MessageBoxButton.OK, MessageBoxImage.Information);
                            OnSourceCollectionChanged(result.source);
                            break;

                        case AddSourceResult.DetectedExisting:
                            var choice = MessageBox.Show(
                                $"识别到已有配置！\n文件夹：{result.source.SourceName}\n包含歌曲：{result.source.Tracks.Count} 首\n\n是否直接添加现有记录？",
                                "识别到已有 Log",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (choice == MessageBoxResult.Yes)
                            {
                                SourceManager.ConfirmAdd(result.source);

                                // 【修改点】：传入 source.LogPath
                                Logger.LogSystemAction("AddExistingSource", $"Restored folder: {result.source.SourceName}", result.source.LogPath);

                                SourceListView.Items.Refresh();

                                OnSourceCollectionChanged(result.source);
                            }
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
        // 【核心升级】：智能差异嗅探与日志覆盖弹窗
        // ==========================================
        private void RefreshSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LibrarySource source)
            {
                // 1. 播放超帅的旋转动画
                DoubleAnimation spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(400))
                {
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                btn.RenderTransformOrigin = new Point(0.5, 0.5);
                btn.RenderTransform = new RotateTransform();
                btn.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, spin);

                // 2. 检查物理文件夹是否还在
                if (!System.IO.Directory.Exists(source.FolderPath))
                {
                    MessageBox.Show("错误：该文件夹已在硬盘上丢失或被重命名！", "找不到路径", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. 支持的音频格式后缀
                var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".flac", ".wav", ".mp3", ".aac", ".m4a", ".ape", ".ogg"
                };

                // 获取硬盘上此时此刻真实的音频文件
                var physicalFiles = System.IO.Directory.GetFiles(source.FolderPath, "*.*", System.IO.SearchOption.AllDirectories)
                                    .Where(f => supportedExts.Contains(System.IO.Path.GetExtension(f)))
                                    .ToList();

                // 【破案核心：超级路径清洗器】
                Func<string, string> cleanPath = p =>
                {
                    if (string.IsNullOrWhiteSpace(p)) return "";
                    try { return System.IO.Path.GetFullPath(p).TrimEnd('\\'); }
                    catch { return p.Replace('/', '\\').Trim(); }
                };

                // 用清洗器把现存路径和物理路径“洗”一遍再进行集合转换
                var existingPaths = source.Tracks.Select(t => cleanPath(t.FilePath)).Where(p => !string.IsNullOrEmpty(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var physicalPaths = physicalFiles.Select(p => cleanPath(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // 4. 开始计算差异 (Diff)
                var newFiles = physicalPaths.Except(existingPaths).ToList();    // 在硬盘上但没记录的 = 新歌
                var missingFiles = existingPaths.Except(physicalPaths).ToList();// 有记录但硬盘上找不到了 = 丢失

                // 没有任何变化，提前下班
                if (newFiles.Count == 0 && missingFiles.Count == 0)
                {
                    MessageBox.Show("该来源文件夹没有任何变动，记录已是最新的！", "扫描完毕", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 5. 生成精美的变动报告并弹窗询问
                string reportMsg = $"在库源 '{source.SourceName}' 中嗅探到物理文件的变动：\n\n" +
                                   $"✨ 发现了 {newFiles.Count} 首新歌\n" +
                                   $"🗑️ 丢失了 {missingFiles.Count} 首旧歌\n\n" +
                                   $"是否要将这些变化更新到本地的 Log 并覆盖配置？";

                var result = MessageBox.Show(reportMsg, "发现文件变动", MessageBoxButton.YesNo, MessageBoxImage.Question);

                // 6. 用户点击了确认：开始覆盖 Log
                if (result == MessageBoxResult.Yes)
                {
                    source.Tracks.RemoveAll(t => missingFiles.Contains(cleanPath(t.FilePath)));

                    foreach (string newPath in newFiles)
                    {
                        // 我在这里先放一个简单的默认 Track 确保功能跑通
                        source.Tracks.Add(new Track
                        {
                            FilePath = newPath,
                            Title = System.IO.Path.GetFileNameWithoutExtension(newPath),
                            Format = System.IO.Path.GetExtension(newPath).Replace(".", "").ToUpper()
                        });
                    }

                    // 【修改点】：传入 source.LogPath
                    Logger.LogSystemAction("SyncSource", $"Synced '{source.SourceName}'. Added: {newFiles.Count}, Removed: {missingFiles.Count}", source.LogPath);

                    // 强制保存状态
                    SourceManager.SaveSources();

                    // 通知全局库和 UI 刷新
                    Oscilla.Core.LibraryManager.RefreshLibrary();

                    // 强制刷新列表行，该行会显示 (JUST NOW)
                    SourceListView.Items.Refresh();

                    OnSourceCollectionChanged(source);

                    MessageBox.Show("配置已更新，库数据已被成功覆盖！", "同步完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        // 卸载库源逻辑 (带垃圾桶图标)
        private void RemoveSource_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is LibrarySource source)
            {
                var confirm = MessageBox.Show(
                    $"确定要移除库源 '{source.SourceName}' 吗？\n\n注意：这仅从软件中卸载，不会删除您的音乐文件。",
                    "卸载确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    // 【修改点】：传入 source.LogPath
                    Logger.LogSystemAction("RemoveSource", $"Removed folder: {source.SourceName}", source.LogPath);

                    SourceManager.RemoveSource(source);
                    OnSourceCollectionChanged();

                    // 刷新列表
                    SourceListView.Items.Refresh();

                    if (SourceListView.SelectedItem == null)
                    {
                        RightPanelContainer.Opacity = 0;
                    }
                }
            }
        }

        // ==========================================
        // 【修复补丁】：开关切换时立刻保存状态，并强行刷新缓存！
        // ==========================================
        private void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.DataContext is LibrarySource source)
            {
                UpdateRightPanel(source);

                // 【修改点】：传入 source.LogPath
                Logger.LogSystemAction("ToggleSource", $"Set '{source.SourceName}' status to: {source.IsEnabled}", source.LogPath);

                // 1. 保存到本地配置
                SourceManager.SaveSources();

                // 2. 强行叫醒大管家更新缓存
                Oscilla.Core.LibraryManager.RefreshLibrary();

                // 3. 强制刷新列表行内时间显示
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
            PreviewName.Text = source.SourceName.ToUpper();

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

            PreviewTrackCount.Text = source.Tracks.Count.ToString();
            PreviewLogStatus.Text = System.IO.File.Exists(source.LogPath) ? "LOCAL.LOG" : "PENDING";

            FolderInitial.Text = !string.IsNullOrEmpty(source.SourceName) ? source.SourceName.Substring(0, 1).ToUpper() : "";

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
            RightPanelContainer.RenderTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }
    }
}