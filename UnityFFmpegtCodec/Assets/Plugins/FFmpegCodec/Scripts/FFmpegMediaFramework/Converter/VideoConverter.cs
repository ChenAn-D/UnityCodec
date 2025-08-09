using FFmpeg.AutoGen;
using System;
using System.Drawing;
using System.Runtime.InteropServices;

/// <summary>
/// 视频像素格式和分辨率转换器
/// </summary>
internal unsafe class VideoConverter : IDisposable
{
    private SwsContext* _swsContext;
    private AVPixelFormat _dstFormat;
    private readonly IntPtr _convertedFrameBufferPtr;
    private readonly byte_ptrArray4 _dstData;
    private readonly int_array4 _dstLinesize;

    private Size dstSize;

    public VideoConverter(Size srceSize, AVPixelFormat srcFormat,
                           Size dstSize, AVPixelFormat dstFormat)
    {
        this.dstSize = dstSize;
        _dstFormat = dstFormat;

        _swsContext = ffmpeg.sws_getContext(
            srceSize.Width, srceSize.Height, srcFormat,
            dstSize.Width, dstSize.Height, dstFormat,
            ffmpeg.SWS_FAST_BILINEAR, null, null, null
        );

        if (_swsContext == null)
            throw new Exception("Failed to initialize sws context.");

        var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(dstFormat,
           dstSize.Width,
           dstSize.Height,
           1);
        _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
        _dstData = new byte_ptrArray4();
        _dstLinesize = new int_array4();

        ffmpeg.av_image_fill_arrays(ref _dstData,
            ref _dstLinesize,
            (byte*)_convertedFrameBufferPtr,
            dstFormat,
            dstSize.Width,
            dstSize.Height,
            1);
    }

    public AVFrame Convert(AVFrame* srcFrame)
    {
        ffmpeg.sws_scale(
            _swsContext,
            srcFrame->data,
            srcFrame->linesize,
            0,
            srcFrame->height,
            _dstData,
            _dstLinesize
        );

        var data = new byte_ptrArray8();
        data.UpdateFrom(_dstData);
        var linesize = new int_array8();
        linesize.UpdateFrom(_dstLinesize);

        return new AVFrame
        {
            data = data,
            linesize = linesize,
            width = dstSize.Width,
            height = dstSize.Height
        };
    }

    public void Dispose()
    {
        Marshal.FreeHGlobal(_convertedFrameBufferPtr);
        if (_swsContext != null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }
    }
}
