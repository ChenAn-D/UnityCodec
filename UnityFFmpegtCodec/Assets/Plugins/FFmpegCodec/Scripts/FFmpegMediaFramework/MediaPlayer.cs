using FFmpeg.AutoGen;
using FFmpegMediaFramework.Decoder;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 播放状态
/// </summary>
public enum MediaPlayerState
{
    /// <summary>
    /// 结束
    /// </summary>
    Stopped,
    /// <summary>
    /// 播放
    /// </summary>
    Playing,
    /// <summary>
    /// 暂停
    /// </summary>
    Paused
}

public class MediaPlayer : IDisposable
{
    private readonly string _url;
    private readonly AVHWDeviceType HWDeviceType;
    private readonly object videoFrameLock = new object();
    private MediaDemuxer _demuxer;
    private IDecoder _videoDecoder;
    private IDecoder _audioDecoder;
    private VideoConverter _converter;
    private AudioResampler _audioResampler;
    private int _videoStreamIndex = -1;
    private int _audioStreamIndex = -1;
    private AVFrame _current_avFrame;
    private double masterClock; // 视频播放的时间

    private Thread _readThread;
    private CancellationTokenSource _cancellation;

    public event Action<IntPtr> OnVideoFrame;
    //public event Action<IntPtr> OnAudioFrame;
    //public Action<IntPtr, int> OnAudioFrame;
    public Action<byte[]> OnAudioFrame;
    /// <summary>
    /// 结束播放事件回调 bool:是否主动结束
    /// </summary>
    public Action<bool> EndPlayAction;
    public Action<float> CurrentPrsAction;
    /// <summary>
    /// 加载视频流 true 开始  false 结束
    /// </summary>
    public Action<bool> LoadingAction;

    /// <summary>
    /// 当前播放状态
    /// </summary>
    private MediaPlayerState mediaPlayerState = MediaPlayerState.Stopped;
    /// <summary>
    /// 播放速度
    /// </summary>
    private float PlayBackSpeed = 1f;

    private bool isFirstFrame = true;
    private bool wasSeeked = false;
    private float percent = 0;
    private double lastPtsSeconds = 0;

    public MediaPlayer(string url, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
    {
        _url = url;
        this.HWDeviceType = HWDeviceType;

    }

    private async Task Init()
    {
        LoadingAction?.Invoke(true);
        await Task.Run(() =>
        {
            _demuxer = new MediaDemuxer(_url);
            InitDecoders();
        });
        LoadingAction.Invoke(false);
    }

    /// <summary>
    /// 初始化音视频解码器
    /// </summary>
    private unsafe void InitDecoders()
    {
        for (int i = 0; i < _demuxer.FormatContext->nb_streams; i++)
        {
            AVStream* stream = _demuxer.FormatContext->streams[i];
            AVCodecParameters* codecpar = stream->codecpar;

            if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO && _videoStreamIndex == -1)
            {
                _videoStreamIndex = i;
                _videoDecoder = new VideoDecoder(codecpar, HWDeviceType);
                var vd = _videoDecoder as VideoDecoder;
                Size size = vd.FrameSize;

                var sourcePixelFormat = HWDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
                    ? vd.PixelFormat
                    : FrameUtils.GetHWPixelFormat(HWDeviceType);
                var destinationPixelFormat = AVPixelFormat.@AV_PIX_FMT_BGRA;
                _converter = new VideoConverter(size, sourcePixelFormat, size, destinationPixelFormat);
            }
            else if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && _audioStreamIndex == -1)
            {
                _audioStreamIndex = i;
                _audioDecoder = new AudioDecoder(codecpar, HWDeviceType);
                _audioResampler = new AudioResampler(codecpar);
                //_audioResampler.Init(_audioDecoder.CodecContext);
            }
        }

        if (_videoDecoder == null && _audioDecoder == null)
            throw new InvalidOperationException("No audio or video stream found.");

    }

    public MediaPlayerState GetPlayState()
    {
        return mediaPlayerState;
    }

    public void ChangePlayState(MediaPlayerState state)
    {
        mediaPlayerState = state;
    }

    /// <summary>
    /// 设置播放速率（1.0=正常速度，2.0=加速，0.5=减速）
    /// </summary>
    public void SetPlaybackSpeed(float speed)
    {
        if (speed <= 0) throw new ArgumentOutOfRangeException(nameof(speed), "Playback speed must be > 0");
        PlayBackSpeed = speed;
    }

    /// <summary>
    /// 启动解码线程
    /// </summary>
    public async Task Start()
    {
        if (mediaPlayerState != MediaPlayerState.Stopped) return;

        if (_demuxer == null) await Init();

        ResetDemuxer();
        mediaPlayerState = MediaPlayerState.Playing;
        isFirstFrame = true;


        _cancellation = new CancellationTokenSource();
        _readThread = new Thread(ReadLoop);
        _readThread.Start();
    }

    /// <summary>
    /// 停止播放
    /// </summary>
    public void Stop()
    {
        mediaPlayerState = MediaPlayerState.Stopped;
        _cancellation?.Cancel();
        _readThread?.Join();
        EndPlayAction?.Invoke(false);
    }

    private unsafe void ResetDemuxer()
    {
        ffmpeg.av_seek_frame(_demuxer.FormatContext, -1, 0, ffmpeg.AVSEEK_FLAG_BACKWARD);
        ffmpeg.avcodec_flush_buffers((_videoDecoder as VideoDecoder).CodecContext);

        if (_audioDecoder != null) ffmpeg.avcodec_flush_buffers((_audioDecoder as AudioDecoder).CodecContext);
    }

    /// <summary>
    /// 跳转播放进度
    /// </summary>
    /// <param name="percent">播放进度0-1</param>
    /// <param name="backward"></param>
    /// <returns></returns>
    public unsafe bool SeekToPercent(float percent, bool backward = true)
    {
        //实时流不能调节进度
        if (_demuxer == null || _demuxer.Duration <= 0) return false;
        wasSeeked = true;
        this.percent = percent;
        //如果视频播放已经结束
        if (mediaPlayerState == MediaPlayerState.Stopped)
        {
            Start();
        }
        return true;
    }

    /// <summary>
    /// 截图
    /// </summary>
    /// <param name="decoder"></param>
    public unsafe void CaptureFrame(Action<bool, byte[]> pngDataCall)
    {
        if (_current_avFrame.data[0] == null)
        {
            pngDataCall?.Invoke(false, new byte[0]);
            return;
        }
        try
        {
            lock (videoFrameLock)
            {
                AVFrame frame = _current_avFrame;
                AVFrame* pFrame = &frame;
                pngDataCall?.Invoke(true, FrameUtils.EncodeFrameToImage(pFrame));
            }
        }
        catch (Exception e)
        {
            pngDataCall?.Invoke(false, new byte[0]);
            throw e;
        }

    }

    /// <summary>
    /// 包读取和解码主循环
    /// </summary>
    private unsafe void ReadLoop()
    {
        AVPacket* packet = ffmpeg.av_packet_alloc();
        AVFrame* decodedFrame;
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {

                if (!_demuxer.ReadPacket(packet))
                {
                    // 文件读取结束，刷新缓冲区
                    FlushDecoder(_videoDecoder, _videoStreamIndex, OnVideoFrame);
                    //FlushDecoder(_audioDecoder, _audioStreamIndex, OnAudioFrame);
                    FlushDecoder(_audioDecoder, _audioStreamIndex, null);
                    break;
                }

                //跳转进度
                if (wasSeeked)
                {
                    //如果是seek操作，跳过当前包
                    ffmpeg.av_packet_unref(packet);
                    wasSeeked = false;
                    isFirstFrame = true;
                    Seek();
                    continue;
                }

                //暂停
                if (mediaPlayerState == MediaPlayerState.Paused) isFirstFrame = true;
                if (mediaPlayerState == MediaPlayerState.Paused && _demuxer.Duration <= 0)
                {
                    continue;
                }

                // 等待恢复播放
                while (mediaPlayerState == MediaPlayerState.Paused && !wasSeeked)
                {
                    Thread.Sleep(10);
                }


                if (packet->stream_index == _videoStreamIndex)
                {
                    if (_videoDecoder.DecodePacket(packet, out decodedFrame))
                    {

                        var av = _converter.Convert(decodedFrame);
                        lock (videoFrameLock) _current_avFrame = av;
                        OnVideoFrame?.Invoke((IntPtr)(&av));

                        //控制播放速度
                        DelayLoop(_demuxer.FormatContext->streams[_videoStreamIndex], decodedFrame->best_effort_timestamp);
                        CurrentPrsAction?.Invoke(GetCurrentProgress(_demuxer.FormatContext->streams[_videoStreamIndex], decodedFrame->best_effort_timestamp));
                    }
                }
                else if (packet->stream_index == _audioStreamIndex)
                {
                    if (_audioDecoder.DecodePacket(packet, out decodedFrame))
                    {
                        if (_audioResampler != null)
                        {
                            _audioResampler.Convert(decodedFrame, _audioDecoder.CodecContext, out byte[] buffer);
                            //_audioResampler.FrameConvertBytes(decodedFrame, out byte[] buffer);
                            OnAudioFrame?.Invoke(buffer);
                        }
                    }
                }

                ffmpeg.av_packet_unref(packet);
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            mediaPlayerState = MediaPlayerState.Stopped;
            EndPlayAction?.Invoke(true);
        }
    }

    private unsafe void DelayLoop(AVStream* stream, long pts)
    {
        //获取时间戳并计算播放间隔 
        AVRational time_base = stream->time_base;
        double currentPtsSeconds = pts * ffmpeg.av_q2d(time_base);

        if (!isFirstFrame && !wasSeeked)
        {
            double delay = (currentPtsSeconds - lastPtsSeconds) / PlayBackSpeed;
            if (delay > 0)
            {
                Thread.Sleep((int)(delay * 1000));
            }
        }
        else
        {
            isFirstFrame = false;
            wasSeeked = false;
        }

        lastPtsSeconds = currentPtsSeconds;
    }

    private unsafe void Seek(bool backward = true)
    {

        if (percent < 0f) percent = 0f;
        if (percent > 1f) percent = 1f;
        if (_demuxer == null) return;

        var stream = _demuxer.FormatContext->streams[_videoStreamIndex];

        //实时流不改变
        if (stream->duration <= 0) return;

        AVRational time_base = stream->time_base;
        double duration = stream->duration * ffmpeg.av_q2d(time_base);
        double targetSeconds = duration * percent;

        long targetPts = (long)(targetSeconds / ffmpeg.av_q2d(time_base)); // 注意是 / 而不是 *

        int seekFlag = backward ? ffmpeg.AVSEEK_FLAG_BACKWARD : ffmpeg.AVSEEK_FLAG_ANY;

        int seek = (int)targetPts;

        int ret = ffmpeg.av_seek_frame(_demuxer.FormatContext, _videoStreamIndex, targetPts, seekFlag);
        if (ret < 0)
        {
            FFmpegHelper.av_strerror(ret);
            return;
        }

        ffmpeg.avcodec_flush_buffers((_videoDecoder as VideoDecoder).CodecContext);
        if (_audioDecoder != null) ffmpeg.avcodec_flush_buffers((_audioDecoder as AudioDecoder).CodecContext);
        return;
    }

    /// <summary>
    /// 计算当前进度
    /// </summary>
    /// <param name="decoder"></param>
    /// <returns></returns>
    private unsafe float GetCurrentProgress(AVStream* stream, long pts)
    {
        var time_base = stream->time_base;
        double timeInSeconds = pts * ffmpeg.av_q2d(time_base);
        var duratuin = stream->duration;
        if (duratuin <= 0) return 1;
        double totalDuration = duratuin * ffmpeg.av_q2d(time_base);

        return (float)(timeInSeconds / totalDuration);

    }

    private unsafe void FlushDecoder(IDecoder decoder, int streamIndex, Action<IntPtr> callback)
    {
        if (decoder == null) return;

        AVPacket* flushPacket = ffmpeg.av_packet_alloc();
        flushPacket->data = null;
        flushPacket->size = 0;

        AVFrame* frame;

        decoder.DecodePacket(flushPacket, out _); // 送入 flush
        while (decoder.DecodePacket(null, out frame))
        {
            callback?.Invoke((IntPtr)frame);
        }

        ffmpeg.av_packet_free(&flushPacket);
    }

    public void Dispose()
    {
        Stop();
        _videoDecoder?.Dispose();
        _audioDecoder?.Dispose();
        _demuxer?.Dispose();
        _cancellation?.Dispose();
        _converter?.Dispose();
        _audioResampler?.Dispose();
    }

    public void GetAudioInitData(out int channels, out int sampleRate)
    {
        if (_audioResampler == null)
        {
            channels = 0;
            sampleRate = 0;
            return;
        }

        channels = _audioResampler.OutChannels;
        sampleRate = _audioResampler.OutSampleRate;

    }
}
