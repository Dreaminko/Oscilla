using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oscilla.Logic; // 引入底层物理扫描引擎

namespace Oscilla.Core
{
    public static class LibraryManager
    {
        private static List<Track>? _cachedTracks;

        public static List<Track> CurrentPlaybackQueue { get; private set; } = new List<Track>();
        public static int CurrentTrackIndex { get; private set; } = -1;

        public static event Action<Track>? TrackChanged;
        public static event Action? LibraryUpdated;

        public static List<Track> AllTracks
        {
            get
            {
                if (_cachedTracks == null)
                {
                    RefreshLibrary();
                }
                return _cachedTracks ?? new List<Track>();
            }
        }

        /// <summary>
        /// 【核心刷新逻辑】：实现静态数据库与映射式日志的“缝合”
        /// </summary>
        public static void RefreshLibrary()
        {
            // --- 屏障：启动加载时不要 Compact，只有手动保存或操作时才 Compact ---

            // 1. 从 SourceManager 获取元数据（来自 oscillasongs）
            var rawTracks = SourceManager.GetActiveTracks();

            // 2. 初始化标签容器
            foreach (var t in rawTracks)
            {
                t.Playlists = new List<string>();
            }

            // 3. 【补丁缝合】：从各分区的状态日志（oscilla.log）中读取歌单映射
            var sources = SourceManager.RegisteredSources;
            if (sources != null)
            {
                foreach (var source in sources.Where(s => s.IsEnabled))
                {
                    string localLogPath = Path.Combine(source.FolderPath, "oscilla.log");

                    if (File.Exists(localLogPath))
                    {
                        var tracksInThisSource = rawTracks
                            .Where(t => t.FilePath != null && t.FilePath.StartsWith(source.FolderPath, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        ApplyScopedPlaylistsFromLog(localLogPath, tracksInThisSource);
                    }
                }
            }

            _cachedTracks = rawTracks;

            // 4. 通知 UI 刷新
            LibraryUpdated?.Invoke();
        }

        /// <summary>
        /// 【解析重构】：支持状态数据库格式 (>>) 和 兼容流水账格式
        /// </summary>
        private static void ApplyScopedPlaylistsFromLog(string logPath, List<Track> tracks)
        {
            if (tracks.Count == 0) return;

            // 建立快速索引：歌曲名 - 歌手
            var trackIndex = tracks
                .GroupBy(t => $"{t.Title ?? "Unknown Title"} - {t.Artist ?? "Unknown Artist"}")
                .ToDictionary(g => g.Key, g => g.First());

            try
            {
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                        // 格式 A：新版状态映射行 (歌曲名 - 歌手 >> 歌单1 § 歌单2)
                        if (line.Contains(" >> "))
                        {
                            var mainParts = line.Split(new[] { " >> " }, StringSplitOptions.None);
                            if (mainParts.Length >= 2)
                            {
                                string trackKey = mainParts[0].Trim();
                                string playlistsPart = mainParts[1].Trim();

                                if (trackIndex.TryGetValue(trackKey, out var matchedTrack))
                                {
                                    var plNames = playlistsPart.Split(new[] { " § " }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var pl in plNames)
                                    {
                                        string cleanPl = pl.Trim();
                                        if (!matchedTrack.Playlists.Contains(cleanPl))
                                            matchedTrack.Playlists.Add(cleanPl);
                                    }
                                }
                            }
                        }
                        // 格式 B：兼容旧版流水账动作行 (用于平滑迁移)
                        else if (line.Contains("[PLAYLIST]"))
                        {
                            bool isAdd = line.Contains("Action: AddToPlaylist");
                            int targetStart = line.IndexOf("Target: [") + 9;
                            int targetEnd = line.IndexOf("]", targetStart);
                            int trackStart = line.IndexOf("Track: [") + 8;
                            int trackEnd = line.IndexOf("]", trackStart);

                            if (targetStart > 8 && targetEnd > targetStart && trackStart > 7 && trackEnd > trackStart)
                            {
                                string playlistName = line.Substring(targetStart, targetEnd - targetStart);
                                string trackInfo = line.Substring(trackStart, trackEnd - trackStart);

                                if (trackIndex.TryGetValue(trackInfo, out var matchedTrack))
                                {
                                    if (isAdd)
                                    {
                                        if (!matchedTrack.Playlists.Contains(playlistName))
                                            matchedTrack.Playlists.Add(playlistName);
                                    }
                                    else
                                    {
                                        matchedTrack.Playlists.Remove(playlistName);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Log Read Error: {ex.Message}"); }
        }

        /// <summary>
        /// 【核心写入】：将内存状态以“映射数据库”格式强制覆盖到磁盘
        /// </summary>
        public static void CompactAllLogs()
        {
            if (_cachedTracks == null || SourceManager.RegisteredSources == null) return;

            foreach (var source in SourceManager.RegisteredSources.Where(s => s.IsEnabled))
            {
                var tracksInThisSource = _cachedTracks
                    .Where(t => t.FilePath != null && t.FilePath.StartsWith(source.FolderPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                List<string> mapLines = new List<string>();
                mapLines.Add($"# LAST_EDITED: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Playlist Mapping Schema");

                foreach (var track in tracksInThisSource)
                {
                    if (track.Playlists != null && track.Playlists.Count > 0)
                    {
                        // 目标格式：歌曲名 - 歌手 >> 歌单1 § 歌单2
                        string plJoined = string.Join(" § ", track.Playlists);
                        mapLines.Add($"{track.Title} - {track.Artist} >> {plJoined}");
                    }
                }

                try
                {
                    string logPath = Path.Combine(source.FolderPath, "oscilla.log");
                    // 【物理覆盖】：只有当内存中有歌单数据，或文件本就存在时才执行写入
                    if (mapLines.Count > 1 || File.Exists(logPath))
                    {
                        File.WriteAllLines(logPath, mapLines, Encoding.UTF8);
                    }
                }
                catch { /* 忽略写入冲突 */ }
            }
        }

        // ==========================================
        // 播放控制逻辑 (保持不变)
        // ==========================================
        public static void SetPlaybackQueue(List<Track> queue, Track targetTrack)
        {
            CurrentPlaybackQueue = queue ?? new List<Track>();
            CurrentTrackIndex = CurrentPlaybackQueue.IndexOf(targetTrack);
            TrackChanged?.Invoke(targetTrack);
        }

        public static void PlayNext()
        {
            if (CurrentPlaybackQueue == null || CurrentPlaybackQueue.Count == 0) return;
            CurrentTrackIndex = (CurrentPlaybackQueue.Count > 0) ? (CurrentTrackIndex + 1) % CurrentPlaybackQueue.Count : -1;
            if (CurrentTrackIndex != -1) TrackChanged?.Invoke(CurrentPlaybackQueue[CurrentTrackIndex]);
        }

        public static void PlayPrevious()
        {
            if (CurrentPlaybackQueue == null || CurrentPlaybackQueue.Count == 0) return;
            CurrentTrackIndex = (CurrentPlaybackQueue.Count > 0) ? (CurrentTrackIndex - 1 + CurrentPlaybackQueue.Count) % CurrentPlaybackQueue.Count : -1;
            if (CurrentTrackIndex != -1) TrackChanged?.Invoke(CurrentPlaybackQueue[CurrentTrackIndex]);
        }

        public static List<string> GetUniquePlaylistNames()
        {
            // 使用 Distinct 确保唯一性，OrderBy 确保 UI 列表有序
            return AllTracks
                .SelectMany(t => t.Playlists)
                .Where(n => n != "All Songs" && !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        public static List<Track> GetTracksByPlaylist(string playlistName)
        {
            if (string.Equals(playlistName, "All Songs", StringComparison.OrdinalIgnoreCase))
                return AllTracks;

            return AllTracks.Where(t => t.Playlists != null && t.Playlists.Contains(playlistName)).ToList();
        }
    }
}