using FFmpeg.AutoGen;
using System;

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
        AVSampleFormat outSampleFmt = AVSampleFormat.AV_SAMPLE_FMT_FLT,
        int outSampleRate = 48000,
        long outChannelLayout = 3)
    {
        this.outSampleFmt = outSampleFmt;
        this.outSampleRate = outSampleRate;
        this.outChannelLayout = outChannelLayout;
        this.outChannels = codecpar->ch_layout.nb_channels;

        // ��ʼ�� SwrContext
        swrContext = ffmpeg.swr_alloc();
        if (swrContext == null)
            throw new ApplicationException("Could not allocate SwrContext");

        //// ����������� (��Դ����������)
        //ffmpeg.av_opt_set_int(swrContext, "in_channel_layout", codecpar->codec_id, 0);
        //ffmpeg.av_opt_set_int(swrContext, "in_sample_rate", codecpar->sample_rate, 0);
        //ffmpeg.av_opt_set_sample_fmt(swrContext, "in_sample_fmt", (AVSampleFormat)codecpar->format, 0);

        //// ����������� (Unity���ø�ʽ)
        //ffmpeg.av_opt_set_int(swrContext, "out_channel_layout", outChannelLayout, 0);
        //ffmpeg.av_opt_set_int(swrContext, "out_sample_rate", outSampleRate, 0);
        //ffmpeg.av_opt_set_sample_fmt(swrContext, "out_sample_fmt", outSampleFmt, 0);

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
