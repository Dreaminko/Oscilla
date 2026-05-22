using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oscilla.Logic; // 必须引入逻辑层以访问 SourceManager

namespace Oscilla.Core
{
    public static class Logger
    {
        // 系统日志专用文件，确保业务数据（歌单）与运行日志分离
        private static readonly string SystemLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system.log");
        private static readonly object _lockObj = new object();

        // ==========================================
        // 【歌单操作】：全面转向状态覆盖模式
        // ==========================================

        public static void LogPlaylistAction(Track track, string playlistName)
        {
            if (track == null || string.IsNullOrEmpty(playlistName)) return;

            // 1. 实时同步内存真相（肉体先行）
            if (track.Playlists == null) track.Playlists = new List<string>();
            if (!track.Playlists.Contains(playlistName))
            {
                track.Playlists.Add(playlistName);
            }

            // 2. 彻底放弃 WriteLog 追加行为。
            // 直接触发全量结算，强制磁盘文件变更为“歌曲 >> 歌单1 § 歌单2”格式
            LibraryManager.CompactAllLogs();
        }

        public static void LogRemoveFromPlaylist(Track track, string playlistName)
        {
            if (track == null || string.IsNullOrEmpty(playlistName)) return;

            // 1. 内存层面物理移除
            if (track.Playlists != null && track.Playlists.Contains(playlistName))
            {
                track.Playlists.Remove(playlistName);
            }

            // 2. 触发全量结算，同步抹除磁盘上的旧数据映射
            LibraryManager.CompactAllLogs();
        }

        // ==========================================
        // 【系统日志】：剥离至 system.log，保持流水账格式
        // ==========================================

        public static void LogSystemAction(string action, string detail, string? targetPath = null)
        {
            // 系统操作（如添加库、切换库）不再写入 oscilla.log，避免干扰数据对齐
            string path = targetPath ?? SystemLogPath;
            WriteLog(path, "SYSTEM", $"Action: {action} | Detail: [{detail}]");
        }

        /// <summary>
        /// 仅用于系统运行日志的追加写入。
        /// </summary>
        private static void WriteLog(string filePath, string level, string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";

            lock (_lockObj)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    // 使用 AppendAllLines 保证原子性追加
                    File.AppendAllLines(filePath, new[] { logEntry });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[System Logger Failed] {ex.Message}");
                }
            }
        }
    }
}