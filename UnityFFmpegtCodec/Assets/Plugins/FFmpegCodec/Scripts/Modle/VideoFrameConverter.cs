using FFmpeg.AutoGen;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
/// <summary>
/// 对帧数据进行规格形状转换
/// </summary>
public class VideoFrameConverter : IDisposable
{
    private unsafe readonly IntPtr _convertedFrameBufferPtr;
    private readonly Size _outputSize;
    private readonly byte_ptrArray4 _outputData;
    private readonly int_array4 _outputLinesize;
    private unsafe readonly SwsContext* _pConvertContext;
    private AVPixelFormat _outputPixelFormat;

    public unsafe VideoFrameConverter(Size inputSize, AVPixelFormat inputPixelFormat,
       Size outputSize, AVPixelFormat outputPixelFormat)
    {
        // 先判断参数合法性
        if (inputSize.Width <= 0 || inputSize.Height <= 0)
            throw new ArgumentException($"输入大小错误{inputSize}");
        if (outputSize.Width <= 0 || outputSize.Height <= 0)
            throw new ArgumentException($"输出大小错误{outputSize}");
        if (!Enum.IsDefined(typeof(AVPixelFormat), inputPixelFormat))
            throw new ArgumentException($"输入像素格式错误{inputPixelFormat}");
        if (!Enum.IsDefined(typeof(AVPixelFormat), outputPixelFormat))
            throw new ArgumentException($"输出像素格式错误{outputPixelFormat}");


        _outputSize = outputSize;
        _outputPixelFormat = outputPixelFormat;

        _pConvertContext = ffmpeg.sws_getCachedContext(_pConvertContext, inputSize.Width, inputSize.Height,
            inputPixelFormat, outputSize.Width, outputSize.Height,
            outputPixelFormat, ffmpeg.SWS_FAST_BILINEAR,
            null,
            null,
             null);
        if (_pConvertContext == null)
            throw new ApplicationException("Could not initialize the conversion context.");
        //计算转换过程中需要的缓存

        var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(outputPixelFormat, outputSize.Width, outputSize.Height, 1);
        _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);

        //_convertedFrameBufferPtr = (byte*)ffmpeg.av_malloc((ulong)convertedFrameBufferSize);
        _outputData = new byte_ptrArray4();
        _outputLinesize = new int_array4();

        ffmpeg.av_image_fill_arrays(
            ref _outputData,
            ref _outputLinesize,
            (byte*)_convertedFrameBufferPtr,
            outputPixelFormat,
            outputSize.Width,
            outputSize.Height,
        1);

        //int size = ffmpeg.av_image_alloc(ref _dstData, ref _dstLinesize, outputSize.Width, outputSize.Height, outputPixelFormat, 1);
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
            _outputData, _outputLinesize);

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
        dstFrame->format = (int)_outputPixelFormat;
        dstFrame->width = _outputSize.Width;
        dstFrame->height = _outputSize.Height;

        for (int i = 0; i < 4; i++)
        {
            dstFrame->data[(uint)i] = _outputData[(uint)i];
            dstFrame->linesize[(uint)i] = _outputLinesize[(uint)i];
        }

        return dstFrame;
    }

    public unsafe AVFrame Convert(AVFrame sourceFrame)
    {
        ffmpeg.sws_scale(_pConvertContext,
            sourceFrame.data,
            sourceFrame.linesize,
            0,
            sourceFrame.height,
            _outputData,
            _outputLinesize);

        var data = new byte_ptrArray8();
        data.UpdateFrom(_outputData);
        var linesize = new int_array8();
        linesize.UpdateFrom(_outputLinesize);

        return new AVFrame
        {
            data = data,
            linesize = linesize,
            width = _outputSize.Width,
            height = _outputSize.Height,
            best_effort_timestamp = sourceFrame.best_effort_timestamp// 这里可以设置为实际的时间戳

        };
    }

}
