using System;
using System.Threading; // 【新增】确保 Thread.Sleep(200) 不会报红叉
using System.Windows.Threading;
using NAudio.Wave;
using Oscilla.Models;

namespace Oscilla.Core // 【已修改】去掉多余的 .UI.core，完美对齐你的 Core 文件夹路径
{
    public enum OutputMode
    {
        Standard,
        Asio
    }

    public class AudioEngine : IDisposable
    {
        private static readonly Lazy<AudioEngine> _instance = new(() => new AudioEngine());
        public static AudioEngine Instance => _instance.Value;

        private IWavePlayer? _outputDevice;
        private AudioFileReader? _audioFile;
        private DispatcherTimer _progressTimer;

        private bool _isLooping = false;
        private float _volume = 0.5f;

        private OutputMode _currentMode = OutputMode.Standard;
        private Track? _currentTrack;

        public OutputMode CurrentMode => _currentMode;

        public event Action<double, double, double>? PositionChanged;
        public event Action? TrackEnded;

        private AudioEngine()
        {
            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _progressTimer.Tick += UpdatePosition;
        }

        public void PrintAsioDevices()
        {
            var asioNames = AsioOut.GetDriverNames();
            System.Diagnostics.Debug.WriteLine($"========== 发现 {asioNames.Length} 个 ASIO 设备 ==========");
            for (int i = 0; i < asioNames.Length; i++)
            {
                System.Diagnostics.Debug.WriteLine($"[设备ID: {i}] 名称: {asioNames[i]}");
            }
            System.Diagnostics.Debug.WriteLine("==================================================");
        }

        // ==========================================
        // 【终极方案】真正的热插拔：只换发声管，不换音频源
        // ==========================================
        public bool SwitchMode(OutputMode targetMode)
        {
            if (_currentMode == targetMode) return true;

            // 1. 记录切换前是否正在播放
            bool wasPlaying = _outputDevice != null && _outputDevice.PlaybackState == PlaybackState.Playing;

            // 2. 【核心大招】：只拔掉声卡线，绝对不销毁正在读取的音频文件！
            DisposeOutputDevice();

            // 给硬件底层 200ms 的释放时间，防爆音
            Thread.Sleep(200);

            // 3. 校验驱动
            if (targetMode == OutputMode.Asio)
            {
                try
                {
                    var asioNames = AsioOut.GetDriverNames();
                    if (asioNames.Length == 0) throw new Exception("无设备");
                }
                catch
                {
                    _currentMode = OutputMode.Standard;
                    RewireDeviceAndResume(wasPlaying);
                    return false;
                }
            }

            _currentMode = targetMode;

            // 4. 把新声卡插到依然保留着断点的音频文件上
            RewireDeviceAndResume(wasPlaying);

            return true;
        }

        // 专门用于销毁发声设备（拔掉线）
        private void DisposeOutputDevice()
        {
            _progressTimer.Stop();
            if (_outputDevice != null)
            {
                _outputDevice.Stop();
                _outputDevice.Dispose();
                _outputDevice = null;
            }
        }

        // 彻底切歌时才调用的“全粉碎”
        private void StopPlaybackPipeline()
        {
            DisposeOutputDevice();
            if (_audioFile != null)
            {
                _audioFile.Dispose();
                _audioFile = null;
            }
        }

        // ==========================================
        // 管线重组中心：将 _audioFile 与声卡无缝对接
        // ==========================================
        private void RewireDeviceAndResume(bool playAfterRewire)
        {
            if (_audioFile == null) return; // 防御机制：没有加载音乐时直接返回

            try
            {
                IWaveProvider finalProvider = _audioFile;

                if (_currentMode == OutputMode.Standard)
                {
                    _outputDevice = new WaveOutEvent() { DesiredLatency = 200 };
                }
                else if (_currentMode == OutputMode.Asio)
                {
                    var asioNames = AsioOut.GetDriverNames();
                    string driverName = asioNames.Length > 0 ? asioNames[0] : throw new Exception("无 ASIO 设备");

                    _outputDevice = new AsioOut(driverName);

                    // 在 ASIO 管道上加装重采样和声道映射
                    ISampleProvider sampleProvider = _audioFile;
                    if (sampleProvider.WaveFormat.Channels == 1)
                        sampleProvider = new NAudio.Wave.SampleProviders.MonoToStereoSampleProvider(sampleProvider);
                    if (sampleProvider.WaveFormat.SampleRate != 48000)
                        sampleProvider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(sampleProvider, 48000);

                    finalProvider = sampleProvider.ToWaveProvider();
                }

                _outputDevice.Init(finalProvider);

                // 如果切换前在播，那就继续播
                if (playAfterRewire)
                {
                    _outputDevice.Play();
                    _progressTimer.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"管线重组失败: {ex.Message}");
                StopPlaybackPipeline();
            }
        }

        // ==========================================
        // 播放控制
        // ==========================================
        public void Play(Track track)
        {
            StopPlaybackPipeline(); // 真正的切歌，新歌来了全销毁
            _currentTrack = track;

            if (string.IsNullOrEmpty(track.FilePath) || !System.IO.File.Exists(track.FilePath)) return;

            try
            {
                // 创建全新的文件读取流
                _audioFile = new AudioFileReader(track.FilePath);
                _audioFile.Volume = _volume;

                // 借助重组中心完成声卡装配和发声
                RewireDeviceAndResume(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"管道组装失败: {ex.Message}");
                StopPlaybackPipeline();
            }
        }

        public void Pause()
        {
            _outputDevice?.Pause();
            _progressTimer.Stop();
        }

        public void Resume()
        {
            _outputDevice?.Play();
            _progressTimer.Start();
        }

        public void Stop()
        {
            StopPlaybackPipeline();
            _currentTrack = null;
        }

        public void Seek(double targetSeconds)
        {
            if (_audioFile != null)
            {
                double max = _audioFile.TotalTime.TotalSeconds;
                if (max > 0 && targetSeconds > max) targetSeconds = max;
                if (targetSeconds < 0) targetSeconds = 0;

                _audioFile.CurrentTime = TimeSpan.FromSeconds(targetSeconds);
            }
        }

        public bool IsLooping
        {
            get => _isLooping;
            set => _isLooping = value;
        }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = value;
                if (_audioFile != null) _audioFile.Volume = value;
            }
        }

        private void UpdatePosition(object? sender, EventArgs e)
        {
            if (_audioFile == null) return;

            double currentSec = _audioFile.CurrentTime.TotalSeconds;
            double totalSec = _audioFile.TotalTime.TotalSeconds;
            double progress = totalSec > 0 ? currentSec / totalSec * 100 : 0;

            PositionChanged?.Invoke(currentSec, totalSec, progress);

            if (totalSec > 0 && totalSec - currentSec <= 0.15)
            {
                if (_isLooping)
                {
                    Seek(0);
                    Resume();
                }
                else
                {
                    _progressTimer.Stop();
                    TrackEnded?.Invoke();
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}