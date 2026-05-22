using System;

namespace Oscilla.Core
{
    public interface IAudioEngine : IDisposable
    {
        // ==========================================
        // 1. 基础播放控制
        // ==========================================
        void Load(string filePath);
        void Play();
        void Pause();
        void Stop();

        // 播放状态属性
        TimeSpan TotalTime { get; }
        TimeSpan CurrentTime { get; set; } // 允许 get/set 实现进度条拖动
        bool IsPlaying { get; }

        // ==========================================
        // 2. 现代 ASIO 核心与热切换
        // ==========================================
        string[] GetAsioDrivers();
        void InitAsio(string driverName, int sampleRate = 44100, int bufferSize = 256);

        // ==========================================
        // 3. 进阶扩展（为后期重构铺路）
        // ==========================================
        // EQ 调节：传入各频段的增益值 (如 -15.0 到 +15.0)
        void UpdateEqualizer(float[] bandGains);

        // 可视化：获取用于绘制频谱的 FFT 浮点数组
        float[] GetFftData();
    }
}