using FFmpeg.AutoGen;
using System;

internal unsafe class VideoConverter : IDisposable
{
    private SwsContext* _swsContext;
    private int _dstWidth, _dstHeight;
    private AVPixelFormat _dstFormat;

    public VideoConverter(int srcWidth, int srcHeight, AVPixelFormat srcFormat,
                          int dstWidth, int dstHeight, AVPixelFormat dstFormat)
    {
        _dstWidth = dstWidth;
        _dstHeight = dstHeight;
        _dstFormat = dstFormat;

        _swsContext = ffmpeg.sws_getContext(
            srcWidth, srcHeight, srcFormat,
            dstWidth, dstHeight, dstFormat,
            ffmpeg.SWS_FAST_BILINEAR, null, null, null
        );

        if (_swsContext == null)
            throw new Exception("Failed to initialize sws context.");
    }

    public void Convert(AVFrame* srcFrame, AVFrame* dstFrame)
    {
        ffmpeg.sws_scale(
            _swsContext,
            srcFrame->data,
            srcFrame->linesize,
            0,
            _dstHeight,
            dstFrame->data,
            dstFrame->linesize
        );
    }

    public void Dispose()
    {
        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }
    }
}
