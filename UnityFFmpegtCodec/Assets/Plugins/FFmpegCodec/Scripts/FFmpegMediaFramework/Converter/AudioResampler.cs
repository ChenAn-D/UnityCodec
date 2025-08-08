using FFmpeg.AutoGen;
using System;

internal unsafe class AudioResampler : IDisposable
{
    private SwrContext* _swrContext;
    private AVSampleFormat _outSampleFmt;
    private int _outSampleRate;
    private int _outChannels;

    public AudioResampler(AVCodecContext* decoderContext, AVSampleFormat outSampleFmt = AVSampleFormat.AV_SAMPLE_FMT_S16, int outSampleRate = 44100, int outChannels = -1)
    {
        _outSampleFmt = outSampleFmt;
        _outSampleRate = outSampleRate;
        //_outChannels = outChannels <= 0 ? decoderContext->channels : outChannels;

        //_swrContext = ffmpeg.swr_alloc_set_opts(
        //    null,
        //    ffmpeg.av_get_default_channel_layout(_outChannels),
        //    _outSampleFmt,
        //    _outSampleRate,
        //    ffmpeg.av_channel_description(decoderContext->channels),
        //    decoderContext->sample_fmt,
        //    decoderContext->sample_rate,
        //    0, null
        //);

        ffmpeg.swr_init(_swrContext).ThrowExceptionIfError();
    }

    public int Resample(AVFrame* inFrame, byte** outBuffer, int outBufferSize)
    {
        int samples = ffmpeg.swr_convert(
            _swrContext,
            outBuffer, outBufferSize,
            inFrame->extended_data, inFrame->nb_samples
        );

        return samples;
    }

    public void Dispose()
    {
        if (_swrContext != null)
        {
            var swrContext = _swrContext;
            ffmpeg.swr_free(&swrContext);
            _swrContext = null;
        }
    }
}
