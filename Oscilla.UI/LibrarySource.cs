using System.Collections.Generic;

namespace Oscilla.Models
{
    public class LibrarySource
    {
        public string FolderPath { get; set; } = string.Empty;

        // 自动拼装出该文件夹下专属的 Log 路径
        public string LogPath => System.IO.Path.Combine(FolderPath, "Oscilla.log");

        // UI 上的开关状态
        public bool IsEnabled { get; set; } = true;

        // 该源下识别到的所有歌曲缓存
        public List<Oscilla.Core.Track> Tracks { get; set; } = new List<Oscilla.Core.Track>();

        // 简短的文件夹名称，用于 UI 显示 (比如 D:\Music\FLAC -> 显示 FLAC)
        public string SourceName => System.IO.Path.GetFileName(FolderPath.TrimEnd('\\', '/'));
    }
}