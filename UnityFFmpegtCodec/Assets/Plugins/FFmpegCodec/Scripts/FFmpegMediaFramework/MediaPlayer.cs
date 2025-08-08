using FFmpeg.AutoGen;
using FFmpegMediaFramework.Decoder;
using System;
using System.Threading;
using Unity.Mathematics;

public enum MediaPlayerState
{
    Stopped,
    Playing,
    Paused
}

internal class MediaPlayer : IDisposable
{
    private readonly AVHWDeviceType HWDeviceType;
    private readonly MediaDemuxer _demuxer;
    private IDecoder _videoDecoder;
    private IDecoder _audioDecoder;
    private VideoConverter _converter;
    private int _videoStreamIndex = -1;
    private int _audioStreamIndex = -1;

    private Thread _readThread;
    private CancellationTokenSource _cancellation;

    public event Action<IntPtr> OnVideoFrame;
    public event Action<IntPtr> OnAudioFrame;

    public MediaPlayer(string url, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
    {
        _demuxer = new MediaDemuxer(url);
        this.HWDeviceType = HWDeviceType;
        InitDecoders();
    }

    private MediaPlayerState mediaPlayerState;

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

                var sourcePixelFormat = HWDeviceType == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
                    ? vd.PixelFormat
                    : GetHWPixelFormat(HWDeviceType);
                var destinationPixelFormat = AVPixelFormat.@AV_PIX_FMT_BGRA;
                _converter = new VideoConverter(vd.Width,vd.Height, sourcePixelFormat, vd.Width, vd.Height, destinationPixelFormat);
            }
            else if (codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO && _audioStreamIndex == -1)
            {
                _audioStreamIndex = i;
                _audioDecoder = new AudioDecoder(codecpar, HWDeviceType);
            }
        }

        if (_videoDecoder == null && _audioDecoder == null)
            throw new InvalidOperationException("No audio or video stream found.");


    }


    public void ChangePlayState(MediaPlayerState state)
    {
        mediaPlayerState = state;
    }

    /// <summary>
    /// 启动解码线程
    /// </summary>
    public void Start()
    {
        if (mediaPlayerState == MediaPlayerState.Playing) return;

        mediaPlayerState = MediaPlayerState.Playing;
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
                    FlushDecoder(_audioDecoder, _audioStreamIndex, OnAudioFrame);
                    break;
                }

                if (packet->stream_index == _videoStreamIndex)
                {
                    if (_videoDecoder.DecodePacket(packet, out decodedFrame))
                    {
                        _converter.Convert(decodedFrame, decodedFrame);
                        OnVideoFrame?.Invoke((IntPtr)decodedFrame);
                    }
                }
                else if (packet->stream_index == _audioStreamIndex)
                {
                    if (_audioDecoder.DecodePacket(packet, out decodedFrame))
                        OnAudioFrame?.Invoke((IntPtr)decodedFrame);
                }

                ffmpeg.av_packet_unref(packet);
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
        }
    }

    private static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
    {
        return hWDevice switch
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => AVPixelFormat.AV_PIX_FMT_NONE,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU => AVPixelFormat.AV_PIX_FMT_VDPAU,
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DRM => AVPixelFormat.AV_PIX_FMT_DRM_PRIME,
            AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL => AVPixelFormat.AV_PIX_FMT_OPENCL,
            AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
            _ => AVPixelFormat.AV_PIX_FMT_NONE
        };
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
    }
}
