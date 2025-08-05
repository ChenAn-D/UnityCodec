using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

/// <summary>
/// ����
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
        //�����ʽ�����ĺͽ���֡
        _pFormatContext = ffmpeg.avformat_alloc_context();
        _receivedFrame = ffmpeg.av_frame_alloc();
        var pFormatContext = _pFormatContext;
        //������������ȡý���ļ�ͷ��Ϣ
        ffmpeg.avformat_open_input(&pFormatContext, url, null, null).ThrowExceptionIfError();
        //��ȡ��ý����Ϣ���洢�� _pFormatContext ��
        ffmpeg.avformat_find_stream_info(_pFormatContext, null).ThrowExceptionIfError();
        AVCodec* codec = null;
        //�ݸ�AVFormatContext��ý����Ϣ���ҵ����ƥ������������������ؽ�����
        _streamIndex = ffmpeg
            .av_find_best_stream(_pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0)
            .ThrowExceptionIfError();
        //���ݽ���������������
        _pCodecContext = ffmpeg.avcodec_alloc_context3(codec);

        //���Ӳ�����������Ͳ�Ϊ AV_HWDEVICE_TYPE_NONE���򴴽�Ӳ���豸������
        if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            //ע��Ӳ���豸
            int ret = ffmpeg.av_hwdevice_ctx_create(&_pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError("��ʼ��ʧ��");
                FFmpegHelper.av_strerror(ret);
            }
        }


        //���ý�����������
        ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar)
            .ThrowExceptionIfError();


        _pAVCodec = ffmpeg.avcodec_find_decoder(_pCodecContext->codec_id);

        //�򿪽�����
        ffmpeg.avcodec_open2(_pCodecContext, codec, null).ThrowExceptionIfError();

        //��ȡ���������ơ�֡��С�����ظ�ʽ
        CodecName = ffmpeg.avcodec_get_name(codec->id);

        FrameSize = new Size(_pCodecContext->width, _pCodecContext->height);
        PixelFormat = _pCodecContext->pix_fmt;

        //�������֡�����ݰ�
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
    /// ���Խ�����һ֡
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
    /// �ж��Ƿ���Ӳ��֡
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
    /// ��ȡ��֡��
    /// </summary>
    /// <returns></returns>
    public unsafe long TotalFrames()
    {
        return _pFormatContext->streams[_streamIndex]->nb_frames;
    }
    /// <summary>
    /// ��ȡ��������������Ϣ
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
}
