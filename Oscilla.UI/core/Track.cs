using System.Collections.Generic;

namespace Oscilla.Core // 【已修改】由 Oscilla.UI.core 改为 Oscilla.Core，与 AudioEngine 完美合体
{
    public class Track
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = "Unknown Track";
        public string Artist { get; set; } = "Unknown Artist";
        public string Duration { get; set; } = "00:00";

        // ==========================================
        // 【核心新增】：原生文件格式，取代不靠谱的计算推测
        // ==========================================
        public string Format { get; set; } = "UNK";

        // 高保真音频参数
        public int Bitrate { get; set; } = 320;
        public double SampleRate { get; set; } = 44.1;
        public int BitDepth { get; set; } = 16;

        // 核心：这首歌挂载了哪些歌单的标签
        public List<string> Playlists { get; set; } = new List<string>();

        // ==========================================
        // 【新增功能】：用于跳转定位的“来源视图ID”
        // ==========================================
        /// <summary>
        /// 标记这首歌曲当前所属的大窗口/视图 ID。
        /// 例如："Library" (全部音乐), "Favorites" (我喜欢的), "Playlist_001" (特定歌单)
        /// 当在 PlayerBar 点击歌名时，主窗口读取此 ID 并自动切换界面。
        /// </summary>
        public string? SourceViewId { get; set; }
    }
}