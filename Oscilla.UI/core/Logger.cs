using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// =================================================================
// 【核心修复】：引入搬家后的音频层和模型层，让 Logger 重新看懂 Track 和库源
// =================================================================
using Oscilla.Core;   // 【新增】认出 Track 类
using Oscilla.Models; // 【新增】认出 LibrarySource 类

namespace Oscilla.Logic // 【已修改】由 Oscilla.UI.core 改为 Oscilla.Logic，完美归位到你的 Logic 文件夹
{
    public static class Logger
    {
        // 系统日志专用文件，确保业务数据（歌单）与运行日志分离
        private static readonly string SystemLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system.log");

        // 用于保护并发写入系统日志
        private static readonly object _systemLogLock = new object();

        // 用于保护单源 Log 文件的并发写入，防止用户快速点击刷新时冲突
        private static readonly Dictionary<string, object> _sourceLogLocks = new Dictionary<string, object>();
        private static readonly object _lockDictLock = new object(); // 保护字典本身的并发访问

        // ==========================================
        // 【歌单操作】：全面转向状态覆盖模式（全局覆盖，保留用于跨源场景）
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

            // 2. 触发全量结算，强制磁盘文件变更为“歌曲 >> 歌单1 § 歌单2”格式
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
        // 【新增】：单源异步安全覆盖方法
        // 用于 RefreshSource 时的精准覆盖，避免全量重刷性能开销
        // ==========================================

        public static async Task<bool> CompactSourceLogAsync(LibrarySource source)
        {
            if (source == null || string.IsNullOrEmpty(source.LogPath))
            {
                System.Diagnostics.Debug.WriteLine("[Logger] 无法覆盖空源或无日志路径的源。");
                return false;
            }

            // 获取或创建该 Log 文件的独占锁，防止并发写入冲突
            object fileLock = GetOrCreateFileLock(source.LogPath);

            bool success = false;
            await Task.Run(() =>
            {
                lock (fileLock)
                {
                    try
                    {
                        // 1. 创建内存快照，防止写入过程中 Track 集合被修改
                        var tracksSnapshot = source.Tracks.ToList();

                        // 2. 准备写入内容，格式与 CompactAllLogs 保持一致
                        var linesToWrite = new List<string>();
                        linesToWrite.Add($"# LAST_EDITED: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                        foreach (var track in tracksSnapshot)
                        {
                            if (track == null) continue;

                            string playlistsStr = track.Playlists != null && track.Playlists.Any()
                                ? string.Join(" § ", track.Playlists)
                                : "";

                            linesToWrite.Add($"{track.FilePath} >> {playlistsStr}");
                        }

                        // 3. 写入临时文件
                        string tempPath = source.LogPath + ".tmp";
                        string? dir = Path.GetDirectoryName(tempPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        File.WriteAllLines(tempPath, linesToWrite);

                        // 4. 原子性替换原文件
                        File.Move(tempPath, source.LogPath, overwrite: true);

                        System.Diagnostics.Debug.WriteLine($"[Logger] 成功覆盖源日志: {source.LogPath}");
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Logger] 覆盖源日志失败: {ex.Message}");
                        // 尝试清理临时文件，防止残留
                        try { if (File.Exists(source.LogPath + ".tmp")) File.Delete(source.LogPath + ".tmp"); } catch { }
                    }
                }
            });

            return success;
        }

        // 获取或创建针对特定 file 路径的锁对象
        private static object GetOrCreateFileLock(string logPath)
        {
            lock (_lockDictLock)
            {
                if (!_sourceLogLocks.TryGetValue(logPath, out object? existingLock))
                {
                    existingLock = new object();
                    _sourceLogLocks[logPath] = existingLock;
                }
                return existingLock;
            }
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

            lock (_systemLogLock)
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