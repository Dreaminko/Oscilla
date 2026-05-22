using System.Collections.Generic;

namespace Oscilla.Core
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
    }
}