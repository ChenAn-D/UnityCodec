using System;
using System.Drawing;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

public unsafe class FFmpegMp4Encoder : IDisposable
{
    private AVFormatContext* pFormatContext = null;
    private AVStream* pVideoStream = null;
    private AVCodecContext* pCodecContext = null;
    private AVFrame* pFrame = null;
    private AVPacket* pPacket = null;
    private int frameIndex = 0;

    private readonly int width;
    private readonly int height;
    private readonly int fps;

    public FFmpegMp4Encoder(string outputPath, int fps, Size size)
    {
        this.width = size.Width;
        this.height = size.Height;
        this.fps = fps;

        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_INFO);


        AVFormatContext* _pFormatContext = pFormatContext;
        int ret = ffmpeg.avformat_alloc_output_context2(&_pFormatContext, null, "mp4", outputPath);
        if (ret < 0 || pFormatContext == null)
            throw new ApplicationException($"Could not allocate output context: {GetErrorText(ret)}");

        // 2. 查找编码器（H264）
        AVCodec* codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
        if (codec == null)
            throw new ApplicationException("Codec H264 not found.");

        // 3. 新建视频流
        pVideoStream = ffmpeg.avformat_new_stream(pFormatContext, codec);
        if (pVideoStream == null)
            throw new ApplicationException("Could not create video stream.");

        pVideoStream->time_base = new AVRational { num = 1, den = fps };

        // 4. 分配编码器上下文
        pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
        if (pCodecContext == null)
            throw new ApplicationException("Could not allocate codec context.");

        pCodecContext->width = width;
        pCodecContext->height = height;
        pCodecContext->time_base = pVideoStream->time_base;
        pCodecContext->framerate = new AVRational { num = fps, den = 1 };
        pCodecContext->gop_size = 12;
        pCodecContext->max_b_frames = 2;
        pCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

        // 编码器私有参数（可调）
        ffmpeg.av_opt_set(pCodecContext->priv_data, "preset", "medium", 0);

        // 5. 打开编码器
        ret = ffmpeg.avcodec_open2(pCodecContext, codec, null);
        if (ret < 0)
            throw new ApplicationException($"Could not open codec: {GetErrorText(ret)}");

        // 6. 复制参数到流
        ret = ffmpeg.avcodec_parameters_from_context(pVideoStream->codecpar, pCodecContext);
        if (ret < 0)
            throw new ApplicationException($"Could not copy codec params: {GetErrorText(ret)}");

        // 7. 打开输出文件IO（非无文件模式）
        if ((pFormatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        {
            ret = ffmpeg.avio_open(&pFormatContext->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
            if (ret < 0)
                throw new ApplicationException($"Could not open output file: {GetErrorText(ret)}");
        }

        // 8. 写文件头
        ret = ffmpeg.avformat_write_header(pFormatContext, null);
        if (ret < 0)
            throw new ApplicationException($"Error writing header: {GetErrorText(ret)}");

        // 9. 分配帧和包
        pFrame = ffmpeg.av_frame_alloc();
        if (pFrame == null)
            throw new ApplicationException("Could not allocate frame.");

        pFrame->format = (int)pCodecContext->pix_fmt;
        pFrame->width = pCodecContext->width;
        pFrame->height = pCodecContext->height;

        ret = ffmpeg.av_frame_get_buffer(pFrame, 32);
        if (ret < 0)
            throw new ApplicationException($"Could not allocate frame buffer: {GetErrorText(ret)}");

        pPacket = ffmpeg.av_packet_alloc();
        if (pPacket == null)
            throw new ApplicationException("Could not allocate packet.");
    }

    /// <summary>
    /// 编码并写入单帧YUV420P数据，frameData须自行填充YUV420P数据
    /// </summary>
    public void EncodeFrame(AVFrame* frame)
    {
        if (frame != null)
        {
            // 确保帧格式和大小符合编码器要求
            if (frame->format != (int)pCodecContext->pix_fmt)
                throw new ArgumentException("Frame pixel format mismatch.");
            if (frame->width != width || frame->height != height)
                throw new ArgumentException("Frame size mismatch.");
        }

        int ret = ffmpeg.avcodec_send_frame(pCodecContext, frame);
        if (ret < 0)
            throw new ApplicationException($"Error sending frame: {GetErrorText(ret)}");

        while (ret >= 0)
        {
            ret = ffmpeg.avcodec_receive_packet(pCodecContext, pPacket);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                return;
            else if (ret < 0)
                throw new ApplicationException($"Error receiving packet: {GetErrorText(ret)}");

            ffmpeg.av_packet_rescale_ts(pPacket, pCodecContext->time_base, pVideoStream->time_base);
            pPacket->stream_index = pVideoStream->index;

            ret = ffmpeg.av_interleaved_write_frame(pFormatContext, pPacket);
            if (ret < 0)
                throw new ApplicationException($"Error writing packet: {GetErrorText(ret)}");

            ffmpeg.av_packet_unref(pPacket);
        }
    }

    /// <summary>
    /// 编码并写入空帧，触发延迟帧写入（flush）
    /// </summary>
    public void Flush()
    {
        EncodeFrame(null);
    }

    public void Dispose()
    {
        Flush();

        ffmpeg.av_write_trailer(pFormatContext);

        if (pCodecContext != null)
        {
            var _pCodecContext = pCodecContext;
            ffmpeg.avcodec_free_context(&_pCodecContext);
            pCodecContext = null;
        }

        if (pFrame != null)
        {
            var _pFrame = pFrame;
            ffmpeg.av_frame_free(&_pFrame);
            pFrame = null;
        }

        if (pPacket != null)
        {
            var _pPacket = pPacket;
            ffmpeg.av_packet_free(&_pPacket);
            pPacket = null;
        }

        if (pFormatContext != null)
        {
            if ((pFormatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
                ffmpeg.avio_closep(&pFormatContext->pb);

            ffmpeg.avformat_free_context(pFormatContext);
            pFormatContext = null;
        }
    }

    private static string GetErrorText(int error)
    {
        const int bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
        return Marshal.PtrToStringAnsi((IntPtr)buffer);
    }
}
