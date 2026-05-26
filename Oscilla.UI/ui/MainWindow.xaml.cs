using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

// =================================================================
// 【核心修复】：切断旧的错乱引用，接通全新的音频核心层与业务逻辑层
// =================================================================
using Oscilla.Core;   // 【新增】引入核心音频层，认出 Track 类
using Oscilla.Logic;  // 【新增】引入业务逻辑层，认出 SourceManager 和 LibraryManager

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

        // 【新增】：记录当前播放歌曲所属的歌单
        public string CurrentPlayingPlaylist { get; private set; } = string.Empty;

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
                    // 【新增】：切换到管理视图时，清空 PlayerBar 的上下文
                    if (BottomBar != null) BottomBar.SetCurrentPlaylistContext("");
                    return;
                }
                else if (tag == "AllSongs")
                {
                    targetTitle = "All Songs";
                    isTargetValid = true;
                }
                else if (rb.Content is Grid grid && grid.Children.Count > 0 && grid.Children[0] is TextBlock tb)
                {
                    targetTitle = tb.Text ?? string.Empty;
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

                    // 【核心修复】：在这里告诉 PlayerBar 当前所在的歌单上下文！
                    if (BottomBar != null) BottomBar.SetCurrentPlaylistContext(targetTitle);

                    // 【新增】：同时更新当前播放歌单
                    this.CurrentPlayingPlaylist = targetTitle;
                }
            }
        }

        // ==========================================
        // 【新增/修正】：供 PlayerBar 调用，跳转到指定曲目
        // ==========================================
        public async void NavigateToTrack(string viewId, Track track)
        {
            if (track == null) return;

            // 【核心修改】：通过LibraryManager确定歌曲实际所属的歌单
            string actualPlaylist = FindPlaylistForTrack(track);

            // 如果能找到歌曲实际所属的歌单，则跳转到该歌单
            if (!string.IsNullOrEmpty(actualPlaylist))
            {
                LoadPlaylistIntoView(actualPlaylist);
            }
            else
            {
                // 如果找不到，则按原逻辑处理
                if (viewId == "Library" || viewId == "Sources")
                {
                    var sourceView = new SourceView();
                    MainContentArea.Content = sourceView;
                    if (BottomBar != null) BottomBar.SetCurrentPlaylistContext("");
                }
                else
                {
                    LoadPlaylistIntoView(viewId);
                }
            }

            // 关键：等待 UI 渲染完成，确保 MainContentArea.Content 已经更新
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            // 在切换后的视图中查找并高亮歌曲
            if (MainContentArea.Content is LibraryView currentLibraryView)
            {
                currentLibraryView.ScrollToAndHighlightTrack(track);
            }
            else
            {
                // 如果切换到了 SourceView 或其他非 LibraryView，则无法高亮
                System.Diagnostics.Debug.WriteLine($"[Navigation] Cannot highlight track '{track.Title}' in view. View is not a LibraryView.");
            }
        }

        // 新增辅助方法：根据歌曲查找它所属的歌单
        private string FindPlaylistForTrack(Track track)
        {
            if (track == null) return string.Empty;

            // 遍历所有歌单，查找包含此歌曲的歌单
            foreach (var playlistName in LibraryManager.GetUniquePlaylistNames())
            {
                var tracksInPlaylist = LibraryManager.GetTracksByPlaylist(playlistName);
                if (tracksInPlaylist.Contains(track))
                {
                    return playlistName;
                }
            }

            // 如果在具体歌单中没找到，检查"All Songs"
            var allSongs = LibraryManager.GetTracksByPlaylist("All Songs");
            if (allSongs.Contains(track))
            {
                return "All Songs";
            }

            return string.Empty;
        }

        // 新增辅助方法：根据 ID 切换视图
        private void SwitchToView(string viewId)
        {
            // 根据 viewId 决定要显示哪个视图
            switch (viewId)
            {
                case "Library":
                case "Sources":
                    // 如果是 Library 或 Sources 视图，则加载 SourceView
                    var sourceView = new SourceView();
                    MainContentArea.Content = sourceView;
                    // 切换视图时同步清空上下文
                    if (BottomBar != null) BottomBar.SetCurrentPlaylistContext("");
                    break;

                case "AllSongs":
                    // 如果是 AllSongs 视图
                    LoadPlaylistIntoView("All Songs");
                    break;

                default:
                    // 默认认为 viewId 是一个具体的歌单名
                    LoadPlaylistIntoView(viewId);
                    break;
            }
        }

        // 新增辅助方法：加载指定歌单到视图
        private void LoadPlaylistIntoView(string playlistName)
        {
            var libraryView = new LibraryView();
            libraryView.SetTitle(playlistName);

            var filteredTracks = LibraryManager.GetTracksByPlaylist(playlistName);
            libraryView.LoadData(filteredTracks, playlistName);
            libraryView.SetGlobalPlayingTrack(this.CurrentPlayingTrack);

            MainContentArea.Content = libraryView;

            // 【核心修复】：在加载歌单视图时，也同步告诉 PlayerBar 当前上下文
            if (BottomBar != null) BottomBar.SetCurrentPlaylistContext(playlistName);

            // 【新增】：同时更新当前播放歌单
            this.CurrentPlayingPlaylist = playlistName;
        }

        // ==========================================
        // 【新增】：供 PlayerBar 调用，记录当前播放歌曲及其所属歌单
        // ==========================================
        public void SetCurrentPlayingContext(Track track, string playlistName)
        {
            this.CurrentPlayingTrack = track;
            this.CurrentPlayingPlaylist = playlistName ?? string.Empty;

            double durationSeconds = 0;
            if (TimeSpan.TryParseExact(track?.Duration ?? string.Empty, @"m\:ss", null, out TimeSpan ts))
                durationSeconds = ts.TotalSeconds;

            BottomBar?.UpdateTrackInfo(track?.Title ?? string.Empty,
                                     track?.Bitrate ?? 0,
                                     track?.SampleRate ?? 0,
                                     track?.BitDepth ?? 0,
                                     durationSeconds);
        }

        // ==========================================
        // 播放与 UI 控制逻辑 (已修改)
        // ==========================================
        public void OnTrackPlayRequested(Track track)
        {
            // 这个方法现在应该调用新的上下文设置方法
            // 需要知道当前在哪个歌单中播放这首歌
            // 我们需要从当前的 LibraryView 获取当前歌单名
            string currentPlaylist = GetCurrentPlaylistFromView();
            SetCurrentPlayingContext(track, currentPlaylist);
        }

        // 新增辅助方法：从当前视图获取歌单名
        private string GetCurrentPlaylistFromView()
        {
            if (MainContentArea.Content is LibraryView libraryView)
            {
                // 如果我们有当前播放歌单的记录，就使用它
                return this.CurrentPlayingPlaylist;
            }
            return string.Empty;
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