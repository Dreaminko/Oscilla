using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

// =================================================================
// 【核心修复】：砍掉旧的 UI.core 引用，换上剥离 UI. 前缀后的标准三层架构引用
// =================================================================
using Oscilla.Core;   // 【新增】引入音频核心层，认出 Track 类
using Oscilla.Logic;  // 【新增】引入业务逻辑层，认出 LibraryManager 和 Logger

namespace Oscilla.UI // 保持干净的 UI 命名空间，完美匹配你的物理文件夹
{
    public partial class LibraryView : UserControl
    {
        public static readonly DependencyProperty IsCustomPlaylistProperty =
            DependencyProperty.Register("IsCustomPlaylist", typeof(bool), typeof(LibraryView), new PropertyMetadata(false));

        public bool IsCustomPlaylist
        {
            get { return (bool)GetValue(IsCustomPlaylistProperty); }
            set { SetValue(IsCustomPlaylistProperty, value); }
        }

        private string _currentLoadedPlaylist = "All Songs";
        private List<Track> _originalTracks = new List<Track>();
        private Track? _globalPlayingTrack;
        private int _filterToken = 0;
        private Track? _pendingDeleteTrack;

        // 用于标识数据是否已加载
        private bool _dataLoaded = false;
        // 用于存储在数据加载前收到的高亮请求
        private Track? _pendingTrackToHighlight = null;

        private Point _dragStartPoint;
        private bool _isDragging = false;

        public LibraryView()
        {
            InitializeComponent();

            this.Loaded += (s, e) =>
            {
                PlayRefreshAnimation();
                LibraryManager.TrackChanged += OnGlobalTrackChanged;
            };

            this.Unloaded += (s, e) =>
            {
                LibraryManager.TrackChanged -= OnGlobalTrackChanged;
            };
        }

        // ==========================================
        // 【核心联动】：供 MainWindow 调用，滚动到指定曲目并高亮
        // ==========================================
        public void ScrollToAndHighlightTrack(Track track)
        {
            if (track == null) return;

            // 如果数据还没加载完，先缓存起来，等加载完再执行
            if (!_dataLoaded)
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryView] Data not loaded yet, pending highlight for: {track.Title}");
                _pendingTrackToHighlight = track;
                return;
            }

            // 数据已加载，执行查找和高亮逻辑
            var currentList = TrackListView.ItemsSource as List<Track>;
            if (currentList != null && currentList.Contains(track))
            {
                // 选中这首歌
                TrackListView.SelectedItem = track;

                // 滚动到这首歌的位置
                TrackListView.ScrollIntoView(track);

                // 更新右侧面板显示这首歌的信息
                UpdateRightPanel(track, "SELECTED");

                System.Diagnostics.Debug.WriteLine($"[LibraryView] Highlighted and scrolled to: {track.Title}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryView] Could not find track '{track.Title}' in current list for playlist '{_currentLoadedPlaylist}'. Available tracks: {currentList?.Count ?? 0}");
            }
        }

        private void OnGlobalTrackChanged(Track newTrack)
        {
            if (newTrack == null) return;

            _globalPlayingTrack = newTrack;
            var currentList = TrackListView.ItemsSource as List<Track>;

            if (currentList != null && currentList.Contains(newTrack))
            {
                TrackListView.SelectedItem = newTrack;
                TrackListView.ScrollIntoView(newTrack);
            }

            RefreshRightPanelState();
        }

        public void SetTitle(string title) { ViewTitle.Text = title.ToUpper(); }

        public void LoadData(List<Track> tracks) => LoadData(tracks, "All Songs");

        public void LoadData(List<Track> tracks, string playlistName)
        {
            _currentLoadedPlaylist = playlistName;
            IsCustomPlaylist = (!string.Equals(playlistName, "All Songs", StringComparison.OrdinalIgnoreCase));
            _originalTracks = tracks;

            // 标记数据加载完成前，清空旧数据
            TrackListView.ItemsSource = null;
            // 标记数据尚未加载
            _dataLoaded = false;
            // 触发 ApplyFilters，它会异步加载数据
            PlayRefreshAnimation();
        }

        public void SetGlobalPlayingTrack(Track? track)
        {
            _globalPlayingTrack = track;
            RefreshRightPanelState();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilters();
        }

        // ==========================================
        // 【新增】：点击歌手名字自动复制到搜索栏过滤
        // ==========================================
        private void ArtistText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
            {
                // 1. 拦截点击事件，防止它触发 ListView 本身的选中或双击操作
                e.Handled = true;

                // 2. 将歌手名字直接丢进搜索框 (这会自动触发 SearchBox_TextChanged 逻辑)
                SearchBox.Text = textBlock.Text;

                // 3. 把输入光标焦点切到搜索框，并将光标移到文字末尾，方便继续输入
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilters();

        public void PlayRefreshAnimation()
        {
            TrackListView.ItemsSource = null;
            ApplyFilters();
        }

        // ==========================================
        // 【完全体】：无感 Diff 拦截 + 完美进退场动画
        // ==========================================
        private async void ApplyFilters()
        {
            if (_originalTracks == null) return;

            _filterToken++;
            int currentToken = _filterToken;

            // 1. 预计算最终筛选结果
            string query = SearchBox.Text.ToLower().Trim();
            bool flac = ChkFlac?.IsChecked == true;
            bool wav = ChkWav?.IsChecked == true;
            bool mp3 = ChkMp3?.IsChecked == true;
            bool aac = ChkAac?.IsChecked == true;
            bool noFormatFilter = !flac && !wav && !mp3 && !aac;

            var finalList = _originalTracks.Where(t => {
                bool match = string.IsNullOrEmpty(query) || (t.Title?.ToLower().Contains(query) == true) || (t.Artist?.ToLower().Contains(query) == true);
                if (!match) return false;
                if (noFormatFilter) return true;
                string fmt = t.Format?.ToUpper() ?? "";
                return (flac && fmt == "FLAC") || (wav && fmt == "WAV") || (mp3 && fmt == "MP3") || (aac && fmt == "AAC");
            }).ToList();

            var currentList = TrackListView.ItemsSource as List<Track> ?? new List<Track>();

            if (currentList.SequenceEqual(finalList)) return;

            var toRemove = currentList.Except(finalList).ToList();
            var toAdd = finalList.Except(currentList).ToList();

            // ----------------------------------------------------
            // 阶段 A：不要的项向右撤离 + 高度塌陷
            // ----------------------------------------------------
            int exitCounter = 0;
            int maxExitDelay = 0;

            foreach (var track in toRemove)
            {
                if (TrackListView.ItemContainerGenerator.ContainerFromItem(track) is ListViewItem container)
                {
                    ClearContainer(container);

                    int delay = exitCounter * 25;
                    maxExitDelay = delay + 250;

                    AnimateBlockExit(container, delay);
                    exitCounter++;

                    if (exitCounter >= 25) break;
                }
            }

            // 等待坍缩完成
            if (exitCounter > 0)
            {
                await Task.Delay(maxExitDelay);
                if (currentToken != _filterToken) return;
            }

            // ----------------------------------------------------
            // 阶段 B：切换数据源并强制渲染
            // ----------------------------------------------------
            TrackListView.ItemsSource = finalList;

            if (_globalPlayingTrack != null && finalList.Contains(_globalPlayingTrack))
            {
                TrackListView.SelectedItem = _globalPlayingTrack;
            }

            RefreshRightPanelState();

            // 强制虚拟化列表更新布局生成新容器
            TrackListView.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);

            if (currentToken != _filterToken) return;

            // 标记数据加载完成
            _dataLoaded = true;

            // 检查是否有等待高亮的歌曲，如果有则执行
            if (_pendingTrackToHighlight != null)
            {
                System.Diagnostics.Debug.WriteLine($"[LibraryView] Processing pending highlight for: {_pendingTrackToHighlight.Title}");
                ScrollToAndHighlightTrack(_pendingTrackToHighlight);
                _pendingTrackToHighlight = null; // 清除等待项
            }

            // ----------------------------------------------------
            // 阶段 C：让新项瀑布般滑入
            // ----------------------------------------------------
            int entryCounter = 0;
            foreach (var track in finalList)
            {
                if (TrackListView.ItemContainerGenerator.ContainerFromItem(track) is ListViewItem container)
                {
                    ClearContainer(container);

                    // 如果是新加回来的项，触发进场动画
                    if (toAdd.Contains(track))
                    {
                        container.Opacity = 0;
                        if (!(container.RenderTransform is TranslateTransform))
                            container.RenderTransform = new TranslateTransform(-30, 0);
                        else
                            ((TranslateTransform)container.RenderTransform).X = -30;

                        AnimateBlockEntry(container, entryCounter * 20);
                        entryCounter++;

                        if (entryCounter >= 25) break;
                    }
                }
            }
        }

        private void ClearContainer(ListViewItem container)
        {
            if (container == null) return;

            container.BeginAnimation(FrameworkElement.HeightProperty, null);
            container.BeginAnimation(FrameworkElement.MinHeightProperty, null);
            container.BeginAnimation(UIElement.OpacityProperty, null);

            if (container.RenderTransform is TranslateTransform tt)
            {
                if (!tt.IsFrozen)
                {
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                }
                container.RenderTransform = new TranslateTransform(0, 0);
            }
            else
            {
                container.RenderTransform = new TranslateTransform(0, 0);
            }

            container.ClearValue(FrameworkElement.HeightProperty);
            container.ClearValue(FrameworkElement.MinHeightProperty);
            container.ClearValue(UIElement.OpacityProperty);

            // 强制锁定基准行高
            container.MinHeight = 46;
            container.Height = 46;
            container.Opacity = 1;
        }

        private void AnimateBlockEntry(ListViewItem item, int delay)
        {
            var easeOut = new QuarticEase { EasingMode = EasingMode.EaseOut };

            // 从左侧滑入原位
            var slideIn = new DoubleAnimation(-30, 0, TimeSpan.FromMilliseconds(300))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = easeOut
            };

            // 透明度淡入
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = easeOut
            };

            if (item.RenderTransform is TranslateTransform tt && tt.IsFrozen)
            {
                item.RenderTransform = new TranslateTransform(tt.X, tt.Y);
            }

            item.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            item.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void AnimateBlockExit(ListViewItem item, int delay)
        {
            item.MinHeight = 0;

            var slide = new DoubleAnimation(0, 300, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay)
            };

            var collapse = new DoubleAnimation(46, 0, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay + 50),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            if (!(item.RenderTransform is TranslateTransform))
            {
                item.RenderTransform = new TranslateTransform(0, 0);
            }
            else if (((TranslateTransform)item.RenderTransform).IsFrozen)
            {
                item.RenderTransform = new TranslateTransform(0, 0);
            }

            item.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slide);
            item.BeginAnimation(UIElement.OpacityProperty, fade);
            item.BeginAnimation(FrameworkElement.HeightProperty, collapse);
        }

        private void RemoveFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is Track track)
            {
                if (!IsCustomPlaylist) return;

                _pendingDeleteTrack = track;
                DeleteTrackPromptText.Text = $"Are you sure you want to remove '{track.Title}' from '{_currentLoadedPlaylist}'?";

                TrackDeleteOverlay.Visibility = Visibility.Visible;
                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                TrackDeleteOverlay.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }

        private void CancelTrackDelete_Click(object sender, MouseButtonEventArgs e)
        {
            HideDeleteOverlay();
            e.Handled = true;
        }

        private void ConfirmTrackDelete_Click(object sender, MouseButtonEventArgs e)
        {
            if (_pendingDeleteTrack != null)
            {
                Logger.LogRemoveFromPlaylist(_pendingDeleteTrack, _currentLoadedPlaylist);
                LibraryManager.RefreshLibrary();

                var updatedTracks = LibraryManager.GetTracksByPlaylist(_currentLoadedPlaylist);
                LoadData(updatedTracks, _currentLoadedPlaylist);
            }

            HideDeleteOverlay();
            e.Handled = true;
        }

        private void HideDeleteOverlay()
        {
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (s, ev) =>
            {
                TrackDeleteOverlay.Visibility = Visibility.Collapsed;
                _pendingDeleteTrack = null;
            };
            TrackDeleteOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        private void ListViewItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is Track track)
                if (TrackListView.SelectedItem != track)
                    UpdateRightPanel(track, "PREVIEW");
        }

        private void ListViewItem_MouseLeave(object sender, MouseEventArgs e) => RefreshRightPanelState();
        private void TrackListView_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshRightPanelState();

        private void ListViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListViewItem item && item.DataContext is Track track)
            {
                var currentQueue = TrackListView.ItemsSource as List<Track>;
                if (currentQueue != null)
                {
                    LibraryManager.SetPlaybackQueue(currentQueue, track);
                }

                if (Window.GetWindow(this) is MainWindow mainWindow)
                {
                    mainWindow.OnTrackPlayRequested(track);
                    _globalPlayingTrack = track;
                    UpdateRightPanel(track, "NOW PLAYING");
                }
            }
        }

        private void TrackListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDragging = false;
        }

        private void TrackListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    ListViewItem listViewItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);

                    if (listViewItem == null) return;

                    if (listViewItem.DataContext is Track track)
                    {
                        _isDragging = true;
                        DataObject dragData = new DataObject("OscillaTrack", track);
                        DragDrop.DoDragDrop(listViewItem, dragData, DragDropEffects.Copy);
                        _isDragging = false;
                    }
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T) return (T)current;
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        private void RefreshRightPanelState()
        {
            if (TrackListView.SelectedItem is Track selectedTrack) UpdateRightPanel(selectedTrack, "SELECTED");
            else if (_globalPlayingTrack != null) UpdateRightPanel(_globalPlayingTrack, "NOW PLAYING");
            else ClearRightPanel();
        }

        private void UpdateRightPanel(Track track, string state)
        {
            PreviewHeaderTitle.Text = state;
            PreviewHeaderTitle.Foreground = (state == "NOW PLAYING" || state == "SELECTED") ? (Brush)FindResource("AuroraBrush") : new SolidColorBrush(Color.FromRgb(85, 85, 85));
            PreviewTitle.Text = track.Title.ToUpper();
            PreviewArtist.Text = track.Artist;
            PreviewDuration.Text = track.Duration;
            PreviewSampleRate.Text = $"{track.SampleRate:F1} kHz";
            PreviewBitDepth.Text = $"{track.BitDepth} bit";
            CoverInitial.Text = !string.IsNullOrEmpty(track.Title) ? track.Title.Substring(0, 1).ToUpper() : "";

            DoubleAnimation fadeAnim = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(150));
            DoubleAnimation slideAnim = new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } };
            RightPanelContainer.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            if (RightPanelContainer.RenderTransform is TranslateTransform tt) tt.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }

        private void ClearRightPanel()
        {
            PreviewHeaderTitle.Text = "INFO";
            PreviewHeaderTitle.Foreground = new SolidColorBrush(Color.FromRgb(85, 85, 85));
            PreviewTitle.Text = "NO TRACK";
            PreviewArtist.Text = "SELECTED";
            PreviewDuration.Text = "--:--";
            PreviewSampleRate.Text = "--.- kHz";
            PreviewBitDepth.Text = "-- bit";
            CoverInitial.Text = "";
        }
    }
}