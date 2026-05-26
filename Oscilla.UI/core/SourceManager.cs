using System;
using System.Collections.Generic; // 用于 HashSet 和 List
using System.IO;
using System.Linq; // 用于强大的 Diff 集合比对计算
using System.Text;
using Oscilla.Core;   // 【新增】引入音频核心层，认出 Track 类
using Oscilla.Models; // 【新增】引入数据模型层，认出 LibrarySource 类

namespace Oscilla.Logic // 【已修改】由 Oscilla.UI.core 改为 Oscilla.Logic，完美对齐你的 Logic 文件夹路径
{
    public enum AddSourceResult
    {
        AlreadyExists,    // 库已在列表中
        CreatedNew,       // 全新扫描并创建数据库
        DetectedExisting  // 已存在 oscillasongs，直接加载
    }

    public static class SourceManager
    {
        public static List<LibrarySource> RegisteredSources { get; private set; } = new List<LibrarySource>();
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sources.cfg");

        // ==========================================
        // 核心记忆：还原库列表
        // ==========================================
        public static void LoadSavedSources()
        {
            if (!File.Exists(ConfigPath)) return;
            try
            {
                var lines = File.ReadAllLines(ConfigPath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split('|');
                    string path = parts[0];
                    bool isEnabled = true;
                    if (parts.Length > 1) bool.TryParse(parts[1], out isEnabled);

                    if (Directory.Exists(path))
                    {
                        var source = AddSourceInternal(path);
                        source.IsEnabled = isEnabled;
                    }
                }
            }
            catch { }
        }

        public static void SaveSources()
        {
            try
            {
                var lines = RegisteredSources.Select(s => $"{s.FolderPath}|{s.IsEnabled}").ToArray();
                File.WriteAllLines(ConfigPath, lines);
            }
            catch { }
        }

        public static void RemoveSource(LibrarySource source)
        {
            if (RegisteredSources.Contains(source))
            {
                RegisteredSources.Remove(source);
                SaveSources();
            }
        }

        // ==========================================
        // 尝试添加库源
        // ==========================================
        public static (AddSourceResult status, LibrarySource source) TryAddSource(string folderPath)
        {
            var existing = RegisteredSources.FirstOrDefault(s => s.FolderPath == folderPath);
            if (existing != null) return (AddSourceResult.AlreadyExists, existing);

            var newSource = new LibrarySource { FolderPath = folderPath };
            string dbPath = Path.Combine(folderPath, "oscillasongs");

            // 检查是否存在静态数据库
            if (File.Exists(dbPath))
            {
                newSource.Tracks = LoadFromSongDatabase(folderPath);
                return (AddSourceResult.DetectedExisting, newSource);
            }

            // 全新文件夹：生成静态数据库
            CreateSongDatabase(newSource);
            newSource.Tracks = LoadFromSongDatabase(folderPath);
            return (AddSourceResult.CreatedNew, newSource);
        }

        public static void ConfirmAdd(LibrarySource source)
        {
            if (!RegisteredSources.Any(s => s.FolderPath == source.FolderPath))
            {
                RegisteredSources.Add(source);
                SaveSources();
            }
        }

        private static LibrarySource AddSourceInternal(string folderPath)
        {
            var newSource = new LibrarySource { FolderPath = folderPath };
            string dbPath = Path.Combine(folderPath, "oscillasongs");

            if (!File.Exists(dbPath)) CreateSongDatabase(newSource);

            newSource.Tracks = LoadFromSongDatabase(folderPath);
            RegisteredSources.Add(newSource);
            return newSource;
        }

        // ==========================================
        // 【核心重构】：修复斜杠死锁，加入清洗防爆机制
        // ==========================================
        private static void CreateSongDatabase(LibrarySource source)
        {
            var extensions = new[] { ".flac", ".wav", ".mp3", ".aac", ".m4a", ".ogg" };
            var files = Directory.EnumerateFiles(source.FolderPath, "*.*", SearchOption.AllDirectories)
                                 .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()));

            StringBuilder sb = new StringBuilder();
            foreach (var rawFile in files)
            {
                string file = rawFile.Replace("/", "\\");

                string relativePath = Path.GetRelativePath(source.FolderPath, file);
                string title = Path.GetFileNameWithoutExtension(file);
                string ext = Path.GetExtension(file).Replace(".", "").ToUpper();
                string artist = "UNKNOWN";
                string duration = "--:--";
                int bitrate = 0;
                double sampleRate = 0.0;
                int bitDepth = 0;

                try
                {
                    using (var tfile = TagLib.File.Create(file))
                    {
                        if (tfile.Tag != null)
                        {
                            if (!string.IsNullOrWhiteSpace(tfile.Tag.Title)) title = tfile.Tag.Title;
                            if (tfile.Tag.Performers?.Length > 0) artist = string.Join(", ", tfile.Tag.Performers);
                            else if (!string.IsNullOrWhiteSpace(tfile.Tag.FirstAlbumArtist)) artist = tfile.Tag.FirstAlbumArtist;
                        }
                        if (tfile.Properties != null)
                        {
                            var ts = tfile.Properties.Duration;
                            if (ts.TotalSeconds > 0) duration = $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
                            bitrate = tfile.Properties.AudioBitrate;
                            sampleRate = tfile.Properties.AudioSampleRate / 1000.0;
                            bitDepth = tfile.Properties.BitsPerSample;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[TagLib 读取失败] {file} | 原因: {ex.Message}");
                }

                title = title.Replace("|", "｜");
                artist = artist.Replace("|", "｜");

                sb.AppendLine($"{relativePath}|{title}|{artist}|{duration}|{bitrate}|{sampleRate:F1}|{bitDepth}|{ext}");
            }

            string dbPath = Path.Combine(source.FolderPath, "oscillasongs");
            File.WriteAllText(dbPath, sb.ToString(), Encoding.UTF8);
        }

        // ==========================================
        // 【新增物理覆盖功能】：点击刷新同步后，将内存中最新的 Tracks 重写物理覆盖日志文件
        // ==========================================
        public static void WriteSourceLog(LibrarySource source)
        {
            if (source == null || source.Tracks == null) return;

            StringBuilder sb = new StringBuilder();
            foreach (var track in source.Tracks)
            {
                // 计算相对路径反向写回（保证格式纯净）
                string relativePath = Path.GetRelativePath(source.FolderPath, track.FilePath).Replace("/", "\\");
                string title = track.Title.Replace("|", "｜");
                string artist = track.Artist.Replace("|", "｜");

                // 还原单行日志行格式：相对路径|标题|艺术家|时长|码率|采样率|位深|格式
                sb.AppendLine($"{relativePath}|{title}|{artist}|{track.Duration}|{track.Bitrate}|{track.SampleRate:F1}|{track.BitDepth}|{track.Format}");
            }

            string dbPath = Path.Combine(source.FolderPath, "oscillasongs");

            // 加入一个首行包含 # LAST_EDITED 的防爆头文件
            string head = $"# LAST_EDITED: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";
            File.WriteAllText(dbPath, head + sb.ToString(), Encoding.UTF8);
        }

        // ==========================================
        // 【已修改为 public】公开此方法，让外围界面可以合法加载音乐库静态数据
        // ==========================================
        public static List<Track> LoadFromSongDatabase(string folderPath)
        {
            var list = new List<Track>();
            string dbPath = Path.Combine(folderPath, "oscillasongs");

            if (!File.Exists(dbPath)) return list;

            var lines = File.ReadAllLines(dbPath);
            foreach (var line in lines)
            {
                // 跳过前面可能夹带的注释编辑时间行
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split('|');

                if (parts.Length >= 8)
                {
                    string relPath = parts[0].Replace("/", "\\");
                    string fullPath = Path.Combine(folderPath, relPath).Replace("/", "\\");

                    list.Add(new Track
                    {
                        FilePath = fullPath,
                        Title = parts[1],
                        Artist = parts[2],
                        Duration = parts[3],
                        Bitrate = int.TryParse(parts[4], out int br) ? br : 0,
                        SampleRate = double.TryParse(parts[5], out double sr) ? sr : 0.0,
                        BitDepth = int.TryParse(parts[6], out int bd) ? bd : 0,
                        Format = parts[7]
                    });
                }
            }
            return list;
        }

        public static List<Track> GetActiveTracks()
        {
            return RegisteredSources.Where(s => s.IsEnabled)
                                    .SelectMany(s => LoadFromSongDatabase(s.FolderPath))
                                    .ToList();
        }
    }
}