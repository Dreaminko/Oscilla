using System;
using NAudio.Wave;

namespace Oscilla.UI
{
    public class OscillaAudioEngine : IDisposable
    {
        private AsioOut _asioOut;
        private AudioFileReader _audioFile;

        /// <summary>
        /// 获取所有可用的 ASIO 驱动名称 (替代原先的 BASSASIO 设备枚举)
        /// </summary>
        public string[] GetAsioDrivers()
        {
            try
            {
                return AsioOut.GetDriverNames();
            }
            catch
            {
                return new string[0];
            }
        }

        /// <summary>
        /// 加载并播放音频
        /// </summary>
        public void LoadAndPlay(string filePath, string asioDriverName)
        {
            Stop(); // 先清理上一个任务

            if (string.IsNullOrEmpty(asioDriverName))
                throw new ArgumentException("请先选择一个 ASIO 驱动");

            // 替代 Bass.CreateStream... (自动兼容 WAV, MP3, FLAC 等)
            _audioFile = new AudioFileReader(filePath);

            // 替代 BassAsio.Init...
            _asioOut = new AsioOut(asioDriverName);

            // 替代 Bass.ChannelPlay...
            _asioOut.Init(_audioFile);
            _asioOut.Play();
        }

        /// <summary>
        /// 暂停播放
        /// </summary>
        public void Pause()
        {
            _asioOut?.Pause();
        }

        /// <summary>
        /// 继续播放
        /// </summary>
        public void Play()
        {
            _asioOut?.Play();
        }

        /// <summary>
        /// 停止并释放资源 (替代 Bass.StreamFree)
        /// </summary>
        public void Stop()
        {
            if (_asioOut != null)
            {
                _asioOut.Stop();
                _asioOut.Dispose();
                _asioOut = null;
            }
            if (_audioFile != null)
            {
                _audioFile.Dispose();
                _audioFile = null;
            }
        }

        /// <summary>
        /// 设置音量 (范围 0.0 到 1.0，替代 Bass.ChannelSetAttribute 音量调节)
        /// </summary>
        public void SetVolume(float volume)
        {
            if (_audioFile != null)
            {
                _audioFile.Volume = volume;
            }
        }

        /// <summary>
        /// 获取当前播放进度和总时长 (用于给你的 PlayerBar 进度条更新)
        /// </summary>
        public TimeSpan CurrentTime => _audioFile?.CurrentTime ?? TimeSpan.Zero;
        public TimeSpan TotalTime => _audioFile?.TotalTime ?? TimeSpan.Zero;

        /// <summary>
        /// 拖动进度条跳转 (替代 Bass.ChannelSetPosition)
        /// </summary>
        public void SetPosition(TimeSpan position)
        {
            if (_audioFile != null)
            {
                _audioFile.CurrentTime = position;
            }
        }

        public void Dispose()
        {
            Stop(); // 窗口关闭时自动释放声卡
        }
    }
}