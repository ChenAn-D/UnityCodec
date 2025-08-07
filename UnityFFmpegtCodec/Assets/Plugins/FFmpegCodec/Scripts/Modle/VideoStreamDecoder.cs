using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

/// <summary>
/// 解码
/// </summary>
public class VideoStreamDecoder : IDisposable
{
    private unsafe readonly FFmpeg.AutoGen.AVCodecContext* _pCodecContext;
    private unsafe readonly FFmpeg.AutoGen.AVFormatContext* _pFormatContext;
    private unsafe readonly FFmpeg.AutoGen.AVFrame* _pFrame;
    private unsafe readonly FFmpeg.AutoGen.AVPacket* _pPacket;
    private unsafe readonly FFmpeg.AutoGen.AVFrame* _receivedFrame;
    private unsafe readonly FFmpeg.AutoGen.AVCodec* _pAVCodec;
    private readonly int _streamIndex;

    public string CodecName { get; }
    public Size FrameSize { get; }
    public AVPixelFormat PixelFormat { get; }

    public unsafe VideoStreamDecoder(string url, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
    {
        //分配格式上下文和解码帧
        _pFormatContext = ffmpeg.avformat_alloc_context();
        _receivedFrame = ffmpeg.av_frame_alloc();
        var pFormatContext = _pFormatContext;

        //打开输入流并读取媒体文件头信息
        ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();

        //获取流媒体信息并存储到 _pFormatContext 中
        ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();
        AVCodec* codec = null;
        //据根AVFormatContext和媒体信息，找到最佳匹配的流索引参数并返回解码器
        _streamIndex = ffmpeg
            .av_find_best_stream(_pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0)
            .ThrowExceptionIfError();
        //根据解码器分配上下文
        _pCodecContext = ffmpeg.avcodec_alloc_context3(codec);

        //如果硬件解码器类型不为 AV_HWDEVICE_TYPE_NONE，则创建硬件设备上下文
        if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            //注册硬件设备
            int ret = ffmpeg.av_hwdevice_ctx_create(&_pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError("初始化失败");
                FFmpegHelper.av_strerror(ret);
            }
        }


        //配置解码器上下文
        ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar)
            .ThrowExceptionIfError();


        _pAVCodec = ffmpeg.avcodec_find_decoder(_pCodecContext->codec_id);

        //打开解码器
        ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

        //获取解码器名称、帧大小和像素格式
        CodecName = ffmpeg.avcodec_get_name(codec->id);

        FrameSize = new Size(_pCodecContext->width, _pCodecContext->height);
        PixelFormat = _pCodecContext->pix_fmt;

        //分配解码帧和数据包
        _pPacket = ffmpeg.av_packet_alloc();
        _pFrame = ffmpeg.av_frame_alloc();
    }

    public unsafe void Dispose()
    {
        var pFrame = _pFrame;
        ffmpeg.av_frame_free(&pFrame);
        var pPacket = _pPacket;
        ffmpeg.av_packet_free(&pPacket);
        var pCodecContext = _pCodecContext;
        ffmpeg.avcodec_free_context(&pCodecContext);
        var pFormatContext = _pFormatContext;
        ffmpeg.avformat_close_input(&pFormatContext);
        var pReceivedFrame = _receivedFrame;
        ffmpeg.av_frame_free(&pReceivedFrame);
    }

    /// <summary>
    /// 尝试解码下一帧
    /// </summary>
    /// <param name="frame"></param>
    /// <returns></returns>
    public unsafe bool TryDecodeNextFrame(out FFmpeg.AutoGen.AVFrame* frame)
    {
        ffmpeg.av_frame_unref(_pFrame);
        ffmpeg.av_frame_unref(_receivedFrame);
        int error;

        do
        {
            try
            {
                do
                {
                    ffmpeg.av_packet_unref(_pPacket);
                    error = ffmpeg.av_read_frame(_pFormatContext, _pPacket);

                    if (error == ffmpeg.AVERROR_EOF)
                    {
                        frame = _pFrame;
                        return false;
                    }

                    error.ThrowExceptionIfError();
                } while (_pPacket->stream_index != _streamIndex);

                ffmpeg.avcodec_send_packet(_pCodecContext, _pPacket).ThrowExceptionIfError();
            }
            finally
            {
                ffmpeg.av_packet_unref(_pPacket);
            }

            error = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);
        } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));

        error.ThrowExceptionIfError();

        if (_pCodecContext->hw_device_ctx != null && IsHardwareFrame(_pFrame))
        {
            ffmpeg.av_hwframe_transfer_data(_receivedFrame, _pFrame, 0).ThrowExceptionIfError();
            frame = _receivedFrame;
        }
        else
            frame = _pFrame;

        return true;
    }

    /// <summary>
    /// 判断是否是硬件帧
    /// </summary>
    /// <param name="frame"></param>
    /// <returns></returns>
    private unsafe bool IsHardwareFrame(AVFrame* frame)
    {
        return frame->format == (int)AVPixelFormat.AV_PIX_FMT_CUDA
            || frame->format == (int)AVPixelFormat.AV_PIX_FMT_DXVA2_VLD
            || frame->format == (int)AVPixelFormat.AV_PIX_FMT_QSV
            || frame->format == (int)AVPixelFormat.AV_PIX_FMT_VAAPI;
    }

    /// <summary>
    /// 获取总帧数
    /// </summary>
    /// <returns></returns>
    public unsafe long TotalFrames()
    {
        return _pFormatContext->streams[_streamIndex]->nb_frames;
    }
    /// <summary>
    /// 获取总时长
    /// </summary>
    /// <returns></returns>
    public unsafe float GetDuration()
    {
        return (float)_pFormatContext->streams[_streamIndex]->duration;
    }

    public unsafe AVStream* GetStream()
    {
        return _pFormatContext->streams[_streamIndex];
    }
    /// <summary>
    /// 获取解码器上下文信息
    /// </summary>
    /// <returns></returns>
    public unsafe IReadOnlyDictionary<string, string> GetContextInfo()
    {
        AVDictionaryEntry* tag = null;
        var result = new Dictionary<string, string>();

        while ((tag = ffmpeg.av_dict_get(_pFormatContext->metadata, "", tag, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
        {
            var key = Marshal.PtrToStringAnsi((IntPtr)tag->key);
            var value = Marshal.PtrToStringAnsi((IntPtr)tag->value);
            result.Add(key, value);
        }

        return result;
    }

    /// <summary>
    /// 跳转
    /// </summary>
    /// <param name="percent"></param>
    /// <param name="backward"></param>
    /// <returns></returns>
    public unsafe bool SeekToPercent(float percent, bool backward = true)
    {
        if (percent < 0f) percent = 0f;
        if (percent > 1f) percent = 1f;

        var stream = _pFormatContext->streams[_streamIndex];

        //实时流不改变
        if (stream->duration <= 0) return false;

        AVRational time_base = stream->time_base;
        double duration = stream->duration * ffmpeg.av_q2d(time_base);
        double targetSeconds = duration * percent;

        long targetPts = (long)(targetSeconds / ffmpeg.av_q2d(time_base)); // 注意是 / 而不是 *

        int seekFlag = backward ? ffmpeg.AVSEEK_FLAG_BACKWARD : ffmpeg.AVSEEK_FLAG_ANY;

        int seek = (int)targetPts;

        int ret = ffmpeg.av_seek_frame(_pFormatContext, _streamIndex, targetPts, seekFlag);
        if (ret < 0)
        {
            FFmpegHelper.av_strerror(ret);
            return false;
        }

        ffmpeg.avcodec_flush_buffers(_pCodecContext);
        AVPacket packet;
        while (ffmpeg.av_read_frame(_pFormatContext, &packet) >= 0)
        {
            if (packet.stream_index == _streamIndex)
            {
                ffmpeg.avcodec_send_packet(_pCodecContext, &packet);
                AVFrame* frame = ffmpeg.av_frame_alloc();
                while (ffmpeg.avcodec_receive_frame(_pCodecContext, frame) == 0)
                {
                    //处理解码后的帧...

                }
                ffmpeg.av_frame_free(&frame);
            }
            ffmpeg.av_packet_unref(&packet);
            return true;
        }

        return false;
    }

}
