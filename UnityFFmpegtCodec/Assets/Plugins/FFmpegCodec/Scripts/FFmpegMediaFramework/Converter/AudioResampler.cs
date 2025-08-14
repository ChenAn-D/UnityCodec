using FFmpeg.AutoGen;
using System;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
using System.Runtime.InteropServices;
using UnityEngine.LightTransport;

public unsafe class AudioResampler : IDisposable
{
    private SwrContext* swrContext;
    private AVSampleFormat outSampleFmt; //������ʽ
    private int outSampleRate; //������
    private long outChannelLayout; //ͨ������
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
        ffmpeg.av_channel_layout_default(&outChannelLayout, 2); // 2����������

        // ��ʼ�� SwrContext
        swrContext = ffmpeg.swr_alloc();
        if (swrContext == null)
            throw new ApplicationException("Could not allocate SwrContext");

        UnityEngine.Debug.Log(codecpar->format);

        var _swrContext = swrContext;
        ffmpeg.swr_alloc_set_opts2(
             &_swrContext,
             &outChannelLayout,           // ����������֣����Լ�����ģ�
             outSampleFmt,                // ���������ʽ
             outSampleRate,               // ���������
             &codecpar->ch_layout,        // ������������
             (AVSampleFormat)codecpar->format,            // ���������ʽ����codecpar��ȡ
             codecpar->sample_rate,       // ���������
             0,
             null
         );


        ////��������  //��ȡ������Ƶ��������Ļ�������С��
        //var BitsPerSample = ffmpeg.av_samples_get_buffer_size(null, 2, codecpar->frame_size, AVSampleFormat.AV_SAMPLE_FMT_S16, 1);
        ////����һ��ָ��
        //audioBuffer = Marshal.AllocHGlobal((int)BitsPerSample);

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
   */

    private byte[] buffer;
    private int bufferLength;
    // ���� swrCtx �Ѿ��� ffmpeg.swr_alloc_set_opts2 ��ʼ��ΪĿ���ʽ
    public void Convert(AVFrame* frame, AVCodecContext* codec_ctx, out byte[] outputBuffer)
    {
        int dstNbSamples = (int)ffmpeg.av_rescale_rnd(
            ffmpeg.swr_get_delay(swrContext, codec_ctx->sample_rate) + frame->nb_samples,
            outSampleRate,       // Ŀ�������
            codec_ctx->sample_rate,
            AVRounding.AV_ROUND_UP);

        int bytesPerSample = ffmpeg.av_get_bytes_per_sample(outSampleFmt);
        //int channels = codec_ctx->ch_layout.nb_channels;
        int channels = outChannels;

        int bufferSize = dstNbSamples * channels * bytesPerSample;

        //UnityEngine.Debug.Log("ԭʼ��Ƶ֡������: " + frame->sample_rate); // 44100
        //UnityEngine.Debug.Log("ԭʼ��Ƶ֡������ʽ: " + (AVSampleFormat)frame->format); // AV_SAMPLE_FMT_FLTP
        //UnityEngine.Debug.Log("ԭʼ��Ƶ֡ͨ����: " + frame->ch_layout.nb_channels); //2
        //UnityEngine.Debug.Log("ÿ֡��������: " + frame->nb_samples); //1024

        int is_planar = ffmpeg.av_sample_fmt_is_planar(codec_ctx->sample_fmt);
        if (is_planar <= 0)
        {
            UnityEngine.Debug.LogWarning("Audio resampler does not support planar format, please use packed format.");
            throw new NotSupportedException("Audio resampler does not support planar format, please use packed format.");
        }

        if (buffer == null || buffer.Length < bufferSize)
            buffer = new byte[bufferSize];

        fixed (byte* dstPtr = buffer)
        {
            // ����������ָ������
            byte** dstData = stackalloc byte*[channels];
            //dstData[0] = dstPtr;
            for (int i = 0; i < channels; i++)
            {
                dstData[i] = dstPtr + i * bytesPerSample * dstNbSamples;
            }

            int convertedSamples = ffmpeg.swr_convert(
                swrContext,
                dstData,
                dstNbSamples,          // ���ﴫ����Ŀ�껺�����Ĳ�����
                frame->extended_data,
                frame->nb_samples      // ���������
            );

            int outDelay = ffmpeg.swr_get_out_samples(swrContext, 0);
            if (outDelay > 0)
            {
                UnityEngine.Debug.LogWarning($"Audio resampler has {outDelay} samples of delay.");
                // ������ӳ٣�������Ҫ���������������С
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

    //������ָ��
    IntPtr audioBuffer;
    /// <summary>
    /// ����Ƶ֡ת�����ֽ�����
    /// </summary>
    /// <param name="sourceFrame"></param>
    /// <returns></returns>
    public void FrameConvertBytes(AVFrame* sourceFrame, out byte[] outputBuffer)
    {
        int dstNbSamples = (int)ffmpeg.av_rescale_rnd(
            ffmpeg.swr_get_delay(swrContext, sourceFrame->sample_rate) + sourceFrame->nb_samples,
            outSampleRate,       // Ŀ�������
            sourceFrame->sample_rate,
            AVRounding.AV_ROUND_UP);

        int bytesPerSample = ffmpeg.av_get_bytes_per_sample(outSampleFmt);
        int channels = outChannels;

        int bufferSize = dstNbSamples * channels * bytesPerSample;


        if (buffer == null || buffer.Length < bufferSize)
            buffer = new byte[bufferSize];

        fixed (byte* dstPtr = buffer)
        {

            //�ز�����Ƶ
            var outputSamplesPerChannel = ffmpeg.swr_convert(swrContext, &dstPtr, outSampleRate, sourceFrame->extended_data, sourceFrame->nb_samples);
        }

        outputBuffer = buffer;

        //�������
        if (audioBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(audioBuffer);
            audioBuffer = IntPtr.Zero;
        }
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
