using FFmpeg.AutoGen;
using System.Runtime.InteropServices;
using System;

public unsafe static class FrameUtils
{
    public static byte[] GetAudioBytes(AVFrame* frame)
    {
        int dataSize = ffmpeg.av_get_bytes_per_sample((AVSampleFormat)frame->format);
        int totalSize = dataSize * frame->nb_samples * frame->flags;

        byte[] buffer = new byte[totalSize];
        Marshal.Copy((IntPtr)frame->data[0], buffer, 0, totalSize);

        return buffer;
    }

    public static byte[] GetVideoBytes(AVFrame* frame, int width, int height)
    {
        int bytesPerPixel = 3; // RGB24
        int totalSize = width * height * bytesPerPixel;

        byte[] buffer = new byte[totalSize];
        Marshal.Copy((IntPtr)frame->data[0], buffer, 0, totalSize);

        return buffer;
    }



    public static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
    {
        return hWDevice switch
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => AVPixelFormat.AV_PIX_FMT_NONE,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU => AVPixelFormat.AV_PIX_FMT_VDPAU,
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DRM => AVPixelFormat.AV_PIX_FMT_DRM_PRIME,
            AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL => AVPixelFormat.AV_PIX_FMT_OPENCL,
            AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
            _ => AVPixelFormat.AV_PIX_FMT_NONE
        };
    }


    /// <summary>
    /// 将 AVFrame 编码为 PNG 图像字节数组
    /// </summary>
    /// <param name="frame"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public static unsafe byte[] EncodeFrameToImage(AVFrame* frame)
    {
        AVPixelFormat encodePixelFormat = AVPixelFormat.AV_PIX_FMT_BGRA; // 先试BGRA

        AVCodec* codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_PNG);
        if (codec == null) throw new Exception("PNG encoder not found");

        AVCodecContext* codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        codecCtx->width = frame->width;
        codecCtx->height = frame->height;
        codecCtx->pix_fmt = encodePixelFormat;
        codecCtx->time_base = new AVRational { num = 1, den = 25 };

        int ret = ffmpeg.avcodec_open2(codecCtx, codec, null);
        if (ret < 0)
        {
            // 直接用BGRA编码失败，尝试转换到RGBA
            ffmpeg.avcodec_free_context(&codecCtx);

            encodePixelFormat = AVPixelFormat.AV_PIX_FMT_RGBA;

            codecCtx = ffmpeg.avcodec_alloc_context3(codec);
            codecCtx->width = frame->width;
            codecCtx->height = frame->height;
            codecCtx->pix_fmt = encodePixelFormat;
            codecCtx->time_base = new AVRational { num = 1, den = 25 };

            ret = ffmpeg.avcodec_open2(codecCtx, codec, null);
            if (ret < 0)
            {
                byte* errBuf = stackalloc byte[1024];
                ffmpeg.av_strerror(ret, errBuf, 1024);
                string errMsg = Marshal.PtrToStringAnsi((IntPtr)errBuf);
                ffmpeg.avcodec_free_context(&codecCtx);
                throw new Exception($"Failed to open PNG encoder: {errMsg}");
            }

            // 转换格式 frame BGRA -> RGBA
            SwsContext* swsCtx = ffmpeg.sws_getContext(
                frame->width, frame->height, AVPixelFormat.AV_PIX_FMT_BGRA,
                frame->width, frame->height, encodePixelFormat,
                ffmpeg.SWS_BILINEAR, null, null, null);

            AVFrame* convertedFrame = ffmpeg.av_frame_alloc();
            convertedFrame->format = (int)encodePixelFormat;
            convertedFrame->width = frame->width;
            convertedFrame->height = frame->height;
            ffmpeg.av_frame_get_buffer(convertedFrame, 0);

            ffmpeg.sws_scale(swsCtx, frame->data, frame->linesize, 0, frame->height, convertedFrame->data, convertedFrame->linesize);

            ffmpeg.sws_freeContext(swsCtx);

            byte[] result = EncodeWithContext(codecCtx, convertedFrame);

            ffmpeg.av_frame_free(&convertedFrame);
            ffmpeg.avcodec_free_context(&codecCtx);
            return result;
        }

        // 直接编码成功，直接用原frame编码
        byte[] data = EncodeWithContext(codecCtx, frame);
        ffmpeg.avcodec_free_context(&codecCtx);
        return data;
    }

    private static unsafe byte[] EncodeWithContext(AVCodecContext* codecCtx, AVFrame* frame)
    {
        AVPacket* pkt = ffmpeg.av_packet_alloc();

        int ret = ffmpeg.avcodec_send_frame(codecCtx, frame);
        if (ret < 0)
        {
            ffmpeg.av_packet_free(&pkt);
            throw new Exception($"Error sending frame to encoder: {ret}");
        }

        ret = ffmpeg.avcodec_receive_packet(codecCtx, pkt);
        if (ret < 0)
        {
            ffmpeg.av_packet_free(&pkt);
            throw new Exception($"Error receiving packet from encoder: {ret}");
        }

        byte[] data = new byte[pkt->size];
        Marshal.Copy((IntPtr)pkt->data, data, 0, pkt->size);

        ffmpeg.av_packet_unref(pkt);
        ffmpeg.av_packet_free(&pkt);

        return data;
    }
}
