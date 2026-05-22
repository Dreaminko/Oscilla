using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Oscilla.Core; // 引入核心库引擎
using Oscilla.Logic; // 必须引入逻辑层以调用 SourceManager

namespace Oscilla.UI
{
    public partial class MainWindow : Window
    {
        // 记录正在等待用户确认删除的歌单
        private RadioButton? _pendingDeletePlaylist;

        // ==========================================
        // 记录全局正在播放的歌曲状态
        // ==========================================
        public Track? CurrentPlayingTrack { get; private set; }

        public MainWindow()
        {
            InitializeComponent();

            // 订阅 Sidebar 抛出的事件
            Sidebar.PlaylistRenamed += Sidebar_OnPlaylistRenamed;
            Sidebar.PlaylistDeleteRequested += Sidebar_OnPlaylistDeleteRequested;
            Sidebar.NavSelectionChanged += Sidebar_NavSelectionChanged;

            // 启动时初始化本地库
            InitializeLibrary();
        }

        // ==========================================
        // 【核心补丁】：启动即恢复记忆
        // ==========================================
        private void InitializeLibrary()
        {
            // 1. 核心指令：让引擎从 sources.cfg 恢复之前保存的库和开关状态
            SourceManager.LoadSavedSources();

            // 2. 既然库已经加载好了，现在可以安全地获取所有歌单名并挂载到 Sidebar
            foreach (var plName in LibraryManager.GetUniquePlaylistNames())
            {
                Sidebar.AddPlaylistExternally(plName);
            }

            System.Diagnostics.Debug.WriteLine($"[Main Init] 已自动恢复 {SourceManager.RegisteredSources.Count} 个库源。");
        }

        // ==========================================
        // 【核心联动】：点击侧边栏 -> 更新中间大屏列表
        // ==========================================
        private void Sidebar_NavSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is RadioButton rb)
            {
                string targetTitle = string.Empty;
                bool isTargetValid = false;

                string? tag = rb.Tag?.ToString();

                // 路由拦截：如果是管理视图
                if (tag == "Library" || tag == "Sources")
                {
                    var sourceView = new SourceView();
                    MainContentArea.Content = sourceView;
                    return;
                }
                else if (tag == "AllSongs")
                {
                    targetTitle = "All Songs";
                    isTargetValid = true;
                }
                else if (rb.Content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
                {
                    targetTitle = tb.Text;
                    isTargetValid = true;
                }

                if (isTargetValid)
                {
                    var libraryView = new LibraryView();
                    libraryView.SetTitle(targetTitle);

                    var filteredTracks = LibraryManager.GetTracksByPlaylist(targetTitle);

                    // ==========================================
                    // 【致命修复】：一定要把 targetTitle 传进去，垃圾桶才能解封！
                    // ==========================================
                    libraryView.LoadData(filteredTracks, targetTitle);

                    libraryView.SetGlobalPlayingTrack(this.CurrentPlayingTrack);

                    MainContentArea.Content = libraryView;
                }
            }
        }

        // ==========================================
        // 播放与 UI 控制逻辑 (保持不变)
        // ==========================================
        public void OnTrackPlayRequested(Track track)
        {
            this.CurrentPlayingTrack = track;
            double durationSeconds = 0;
            if (TimeSpan.TryParseExact(track.Duration, @"m\:ss", null, out TimeSpan ts))
                durationSeconds = ts.TotalSeconds;

            BottomBar.UpdateTrackInfo(track.Title, track.Bitrate, track.SampleRate, track.BitDepth, durationSeconds);
        }

        private void Sidebar_OnPlaylistRenamed(object? sender, PlaylistRenamedEventArgs e) =>
            System.Diagnostics.Debug.WriteLine($"[Main UI Alert] Playlist changed to {e.NewName}");

        private void Sidebar_OnPlaylistDeleteRequested(object? sender, PlaylistDeleteEventArgs e)
        {
            _pendingDeletePlaylist = e.PlaylistElement;
            DeleteTargetText.Text = $"Delete '{e.PlaylistName}'?";
            DeleteOverlay.Visibility = Visibility.Visible;
        }

        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingDeletePlaylist != null) Sidebar.RemovePlaylist(_pendingDeletePlaylist);
            DeleteOverlay.Visibility = Visibility.Collapsed;
            _pendingDeletePlaylist = null;
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteOverlay.Visibility = Visibility.Collapsed;
            _pendingDeletePlaylist = null;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            // 【安全退出】：关闭前最后存一次配置，确保所有开关状态都被锁死在 cfg 里
            SourceManager.SaveSources();
            Application.Current.Shutdown();
        }
    }
}