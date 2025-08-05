using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
/// <summary>
/// 对帧数据进行规格形状转换
/// </summary>
public class VideoFrameConverter : IDisposable
{
    private unsafe readonly IntPtr _convertedFrameBufferPtr;
    private readonly Size _destinationSize;
    private readonly byte_ptrArray4 _dstData;
    private readonly int_array4 _dstLinesize;
    private unsafe readonly SwsContext* _pConvertContext;
    private AVPixelFormat _dstPixelFormat;

    public unsafe VideoFrameConverter(Size sourceSize, AVPixelFormat sourcePixelFormat,
       Size destinationSize, AVPixelFormat destinationPixelFormat)
    {
        // 先判断参数合法性
        if (sourceSize.Width <= 0 || sourceSize.Height <= 0)
            throw new ArgumentException("Invalid source size");
        if (destinationSize.Width <= 0 || destinationSize.Height <= 0)
            throw new ArgumentException("Invalid destination size");
        if (!Enum.IsDefined(typeof(AVPixelFormat), sourcePixelFormat))
            throw new ArgumentException("Invalid source pixel format");
        if (!Enum.IsDefined(typeof(AVPixelFormat), destinationPixelFormat))
            throw new ArgumentException("Invalid destination pixel format");


        _destinationSize = destinationSize;
        _dstPixelFormat = destinationPixelFormat;

        //创建格式转换上下文
        _pConvertContext = ffmpeg.sws_getContext(sourceSize.Width,
             sourceSize.Height,
             sourcePixelFormat,
             destinationSize.Width,
             destinationSize.Height,
             destinationPixelFormat,
             ffmpeg.SWS_FAST_BILINEAR, //指定转换器的算法
             null,
             null,
             null);

        if (_pConvertContext == null)
            throw new ApplicationException("Could not initialize the conversion context.");
        //计算转换过程中需要的缓存

        var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat, destinationSize.Width, destinationSize.Height, 1);
        _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);

        //_convertedFrameBufferPtr = (byte*)ffmpeg.av_malloc((ulong)convertedFrameBufferSize);
        _dstData = new byte_ptrArray4();
        _dstLinesize = new int_array4();

        ffmpeg.av_image_fill_arrays(
            ref _dstData,
            ref _dstLinesize,
            (byte*)_convertedFrameBufferPtr,
            destinationPixelFormat,
            destinationSize.Width,
            destinationSize.Height,
        1);

        //int size = ffmpeg.av_image_alloc(ref _dstData, ref _dstLinesize, destinationSize.Width, destinationSize.Height, destinationPixelFormat, 1);
        /*
                

        */
    }


    public unsafe void Dispose()
    {
        Marshal.FreeHGlobal(_convertedFrameBufferPtr);
        //ffmpeg.av_free(_convertedFrameBufferPtr);
        ffmpeg.sws_freeContext(_pConvertContext);
    }

    public unsafe AVFrame* Convert(AVFrame* sourceFrame)
    {
        if (sourceFrame == null)
            return null;

        // 1. 判断是否是硬件帧（如 AV_PIX_FMT_CUDA）
        AVPixelFormat srcFormat = (AVPixelFormat)sourceFrame->format;
        AVFrame* cpuFrame = sourceFrame;

        AVPixFmtDescriptor* desc = ffmpeg.av_pix_fmt_desc_get(srcFormat);
        bool isHardwareFrame = (desc != null) && ((desc->flags & ffmpeg.AV_PIX_FMT_FLAG_HWACCEL) != 0);
        //bool isHardwareFrame = ffmpeg.av_pix_fmt_desc_get(srcFormat)->flags.GetHashCode();
        if (isHardwareFrame)
        {
            cpuFrame = ffmpeg.av_frame_alloc();

            // 2. 转换 GPU → CPU
            int err = ffmpeg.av_hwframe_transfer_data(cpuFrame, sourceFrame, 0);
            if (err < 0)
            {
                UnityEngine.Debug.LogError("av_hwframe_transfer_data failed: " + err);
                ffmpeg.av_frame_free(&cpuFrame);
                return null;
            }

            srcFormat = (AVPixelFormat)cpuFrame->format; // 转换后是 CPU 格式
        }

        // 3. 继续 sws_scale
        int scale = ffmpeg.sws_scale(_pConvertContext,
            cpuFrame->data, cpuFrame->linesize,
            0, cpuFrame->height,
            _dstData, _dstLinesize);

        // 4. 如果 GPU → CPU 分配了新帧，记得释放
        if (cpuFrame != sourceFrame)
        {
            ffmpeg.av_frame_free(&cpuFrame);
        }

        if (scale <= 0)
        {
            UnityEngine.Debug.LogError("sws_scale failed");
            return null;
        }

        // 5. 创建目标 AVFrame 并填充数据
        AVFrame* dstFrame = ffmpeg.av_frame_alloc();
        dstFrame->format = (int)_dstPixelFormat;
        dstFrame->width = _destinationSize.Width;
        dstFrame->height = _destinationSize.Height;

        for (int i = 0; i < 4; i++)
        {
            dstFrame->data[(uint)i] = _dstData[(uint)i];
            dstFrame->linesize[(uint)i] = _dstLinesize[(uint)i];
        }

        return dstFrame;
    }

}
