using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public unsafe class AudioResampler : IDisposable
{
    private SwrContext* swrContext;
    private AVSampleFormat outSampleFmt;
    private int outSampleRate;
    private long outChannelLayout;
    private int outChannels;

    private byte** convertedData;
    private int maxConvertedSamples;

    public int OutChannels => outChannels;
    public int OutSampleRate => outSampleRate;

    public AudioResampler(AVCodecParameters* codecpar,
        AVSampleFormat outSampleFmt = AVSampleFormat.AV_SAMPLE_FMT_S16,
        int outSampleRate = 48000,
        long outChannelLayout = 3)
    {

        // ��ʼ�� SwrContext
        //swrContext = ffmpeg.swr_alloc();
        //if (swrContext == null)
        //    throw new ApplicationException("Could not allocate SwrContext");

        /*
        this.outSampleFmt = outSampleFmt;
        this.outSampleRate = outSampleRate;
        this.outChannelLayout = outChannelLayout;
        this.outChannels = codecpar->ch_layout.nb_channels;

        // ��ʼ�� SwrContext
        swrContext = ffmpeg.swr_alloc();
        if (swrContext == null)
            throw new ApplicationException("Could not allocate SwrContext");

        var _swrContext = swrContext;
        ffmpeg.swr_alloc_set_opts2(
              &_swrContext,
              &codecpar->ch_layout,
              (AVSampleFormat)codecpar->format,
              outSampleRate,
              &codecpar->ch_layout,
              (AVSampleFormat)codecpar->format,
              codecpar->sample_rate,
              0,
              null
            );

        int ret = ffmpeg.swr_init(swrContext);
        if (ret < 0)
            throw new ApplicationException($"Failed to initialize the resampling context: {ret}");

        maxConvertedSamples = 1024 * 4; // Ԥ�軺������С
        convertedData = (byte**)ffmpeg.av_malloc_array((uint)outChannels, (ulong)sizeof(byte*));
        ffmpeg.av_samples_alloc(convertedData, null, outChannels, maxConvertedSamples, outSampleFmt, 0);
        */


    }

    public void Init(AVCodecContext* codec_ctx)
    {
        outChannels = codec_ctx->ch_layout.nb_channels;
        outSampleFmt = AVSampleFormat.AV_SAMPLE_FMT_S32; // Ĭ�������ʽ
        outSampleFmt = codec_ctx->sample_fmt;
        outSampleRate = codec_ctx->sample_rate;
        UnityEngine.Debug.Log(outSampleRate);
        UnityEngine.Debug.Log(outSampleFmt);
    }

    public int Convert(AVFrame* frame, out IntPtr outputBuffer)
    {
        int inSamples = frame->nb_samples;

        // ����ת����������
        int outSamples = (int)ffmpeg.av_rescale_rnd(
            ffmpeg.swr_get_delay(swrContext, frame->sample_rate) + inSamples,
            outSampleRate,
            frame->sample_rate,
            AVRounding.AV_ROUND_UP);

        if (outSamples > maxConvertedSamples)
        {
            ffmpeg.av_freep(&convertedData[0]);
            ffmpeg.av_samples_alloc(convertedData, null, outChannels, outSamples, outSampleFmt, 1);
            maxConvertedSamples = outSamples;
        }

        // ת��
        int convertedSampleCount = ffmpeg.swr_convert(swrContext, convertedData, outSamples, frame->extended_data, inSamples);
        if (convertedSampleCount < 0)
            throw new ApplicationException($"Error while converting audio: {convertedSampleCount}");

        int dataSize = ffmpeg.av_samples_get_buffer_size(null, outChannels, convertedSampleCount, outSampleFmt, 1);
        outputBuffer = (IntPtr)convertedData[0];

        return dataSize; // �����ֽ���
    }


    private byte[] buffer;
    private int bufferLength;
    public void Convert(AVFrame* frame, AVCodecContext* codec_ctx, out byte[] outputBuffer)
    {
        var data = frame->extended_data;
        int linesize = frame->linesize[0];
        AVChannelLayout av_layout = codec_ctx->ch_layout; // AV_CH_LAYOUT_STEREO
        int bytes_per_sample = ffmpeg.av_get_bytes_per_sample((AVSampleFormat)frame->format);
        int channels = av_layout.nb_channels;
        int outputSampleRate = codec_ctx->sample_rate;
        int samples = frame->nb_samples;

        int totalBytes = samples * channels * bytes_per_sample;

        // ��������������������
        if (buffer == null || buffer.Length < totalBytes)
            buffer = new byte[totalBytes];
        fixed (byte* dstPtr = buffer)
        {
            int offset = 0;
            // �ж��Ƿ� planar ��ʽ
            bool isPlanar = ffmpeg.av_sample_fmt_is_planar((AVSampleFormat)frame->format) != 0;
            if (isPlanar)
            {
                // Planar: ���������ݷֿ��洢
                for (int i = 0; i < samples; i++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        byte* src = frame->extended_data[ch] + i * bytes_per_sample;
                        for (int b = 0; b < bytes_per_sample; b++)
                        {
                            dstPtr[offset++] = src[b];
                        }
                    }
                }
            }
            else
            {
                // Packed: ��������洢�������� extended_data[0]
                byte* srcStart = frame->extended_data[0];
                int totalDataBytes = samples * channels * bytes_per_sample;
                for (int i = 0; i < totalDataBytes; i++)
                {
                    dstPtr[i] = srcStart[i];
                }
                offset = totalDataBytes;
            }
        }

        bufferLength = totalBytes;
        outputBuffer = buffer; // ֱ�ӷ����ڲ�������

    }


    public void Dispose()
    {
        if (convertedData != null)
        {
            ffmpeg.av_freep(&convertedData[0]);
            var _convertedData = convertedData;
            ffmpeg.av_freep(&_convertedData);
            convertedData = null;
        }
        if (swrContext != null)
        {
            var _swrContext = swrContext;
            ffmpeg.swr_free(&_swrContext);
            swrContext = null;
        }
    }
}
