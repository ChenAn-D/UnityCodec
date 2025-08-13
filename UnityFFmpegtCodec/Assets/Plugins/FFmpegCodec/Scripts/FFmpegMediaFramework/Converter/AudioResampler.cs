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
        AVSampleFormat outSampleFmt = AVSampleFormat.AV_SAMPLE_FMT_FLT
        )
    {

        this.outSampleFmt = outSampleFmt;
        this.outSampleRate = codecpar->sample_rate;
        this.outChannels = codecpar->ch_layout.nb_channels;

        AVChannelLayout outChannelLayout;
        ffmpeg.av_channel_layout_default(&outChannelLayout, 2); // 2声道立体声

        // 初始化 SwrContext
        swrContext = ffmpeg.swr_alloc();
        if (swrContext == null)
            throw new ApplicationException("Could not allocate SwrContext");

        var _swrContext = swrContext;
        ffmpeg.swr_alloc_set_opts2(
             &_swrContext,
             &outChannelLayout,           // 输出声道布局（你自己传入的）
             outSampleFmt,                // 输出采样格式
             outSampleRate,               // 输出采样率
             &codecpar->ch_layout,        // 输入声道布局
             (AVSampleFormat)codecpar->format,            // 输入采样格式，从codecpar获取
             codecpar->sample_rate,       // 输入采样率
             0,
             null
         );

        int ret = ffmpeg.swr_init(swrContext);
        if (ret < 0)
            throw new ApplicationException($"Failed to initialize the resampling context: {ret}");
    }

    /*
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

         // 如果缓冲区不够大就扩容
         if (buffer == null || buffer.Length < totalBytes)
             buffer = new byte[totalBytes];
         fixed (byte* dstPtr = buffer)
         {
             int offset = 0;
             // 判断是否 planar 格式
             bool isPlanar = ffmpeg.av_sample_fmt_is_planar((AVSampleFormat)frame->format) != 0;
             if (isPlanar)
             {
                 // Planar: 各声道数据分开存储
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
                 // Packed: 声道交错存储，数据在 extended_data[0]
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
         outputBuffer = buffer; // 直接返回内部缓冲区
     }
   */


    private byte[] buffer;
    private int bufferLength;
    // 假设 swrCtx 已经用 ffmpeg.swr_alloc_set_opts2 初始化为目标格式
    public void Convert(AVFrame* frame, AVCodecContext* codec_ctx, out byte[] outputBuffer)
    {
        int dstNbSamples = (int)ffmpeg.av_rescale_rnd(
            ffmpeg.swr_get_delay(swrContext, codec_ctx->sample_rate) + frame->nb_samples,
            outSampleRate,       // 目标采样率
            codec_ctx->sample_rate,
            AVRounding.AV_ROUND_PASS_MINMAX);

        int bytesPerSample = ffmpeg.av_get_bytes_per_sample(outSampleFmt);
        int channels = codec_ctx->ch_layout.nb_channels;

        int bufferSize = dstNbSamples * channels * bytesPerSample;

        if (buffer == null || buffer.Length < bufferSize)
            buffer = new byte[bufferSize];

        fixed (byte* dstPtr = buffer)
        {
            // 创建多声道指针数组
            byte** dstData = stackalloc byte*[channels];

            for (int i = 0; i < channels; i++)
            {
                dstData[i] = dstPtr + i * bytesPerSample * dstNbSamples;
            }

            int convertedSamples = ffmpeg.swr_convert(
                swrContext,
                dstData,
                dstNbSamples,          // 这里传的是目标缓冲区的采样数
                frame->extended_data,
                frame->nb_samples      // 输入采样数
            );

            int outDelay = ffmpeg.swr_get_out_samples(swrContext, 0);
            if (outDelay > 0)
            {
                UnityEngine.Debug.LogWarning($"Audio resampler has {outDelay} samples of delay.");
                // 如果有延迟，可能需要调整输出缓冲区大小
                //bufferSize = outDelay * channels * bytesPerSample;
                //if (buffer.Length < bufferSize)
                //    Array.Resize(ref buffer, bufferSize);
            }

            if (convertedSamples < 0)
                throw new ApplicationException("Error while converting");

            bufferLength = convertedSamples * channels * bytesPerSample;
        }

        outputBuffer = buffer;
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
