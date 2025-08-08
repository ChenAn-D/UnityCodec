using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using FFmpeg.AutoGen;
using Unity.Mathematics;
using Unity.VisualScripting;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;

public unsafe class FFmpegMp4Encoder : IDisposable
{
    private AVFormatContext* formatCtx;
    private AVStream* stream;
    private AVCodecContext* codecCtx;
    private AVFrame* convertedFrame;
    private SwsContext* swsCtx;
    private AVCodec* codec;
    private AVPacket* packet;
    private int frameCounter;
    private int width, height;
    private AVRational timeBase;
    private long start_pts;

    public FFmpegMp4Encoder(string outputPath, int fps, Size size, long start_pts)
    {
        this.width = size.Width;
        this.height = size.Height;
        this.start_pts = start_pts;
        frameCounter = 0;

        AVFormatContext* formatctx = null;
        ffmpeg.avformat_alloc_output_context2(&formatctx, null, null, outputPath);
        formatCtx = formatctx;
        if (formatctx == null)
            throw new ApplicationException("Could not allocate output context");

        codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
        if (codec == null)
            throw new ApplicationException("Codec not found");

        stream = ffmpeg.avformat_new_stream(formatctx, codec);
        stream->id = (int)formatctx->nb_streams - 1;

        codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        codecCtx->codec_id = AVCodecID.AV_CODEC_ID_H264;
        codecCtx->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        codecCtx->width = width;
        codecCtx->height = height;
        codecCtx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        codecCtx->time_base = new AVRational { num = 1, den = fps };
        //codecCtx->gop_size = 12;
        //codecCtx->max_b_frames = 2;
        //codecCtx->bit_rate = 400000;

        if ((formatctx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        AVDictionary* opts = null;
        ffmpeg.av_dict_set(&opts, "preset", "ultrafast", 0);
        int ret = ffmpeg.avcodec_open2(codecCtx, codec, &opts);
        if (ret < 0)
            throw new ApplicationException("Could not open codec: " + GetErrorText(ret));

        ffmpeg.avcodec_parameters_from_context(stream->codecpar, codecCtx);
        stream->time_base = codecCtx->time_base;
        timeBase = codecCtx->time_base;

        ret = ffmpeg.avio_open(&formatctx->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
        if (ret < 0)
            throw new ApplicationException("Could not open output file: " + GetErrorText(ret));

        ffmpeg.avformat_write_header(formatCtx, null);

        // allocate converted frame
        convertedFrame = ffmpeg.av_frame_alloc();
        convertedFrame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        convertedFrame->width = width;
        convertedFrame->height = height;
        ffmpeg.av_frame_get_buffer(convertedFrame, 32);

        // SwsContext for RGB24 to YUV420P
        swsCtx = ffmpeg.sws_getContext(width, height, AVPixelFormat.AV_PIX_FMT_RGB24,
                                       width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                                       ffmpeg.SWS_FAST_BILINEAR, null, null, null);

        packet = ffmpeg.av_packet_alloc();
    }


    public void EncodeFrame(AVFrame* rgbFrame)
    {
        // Convert RGB24 to YUV420P
        byte_ptrArray8 srcData = rgbFrame->data;
        int_array8 srcLinesize = rgbFrame->linesize;

        ffmpeg.sws_scale(swsCtx, srcData, srcLinesize, 0, height, convertedFrame->data, convertedFrame->linesize);
        convertedFrame->pts = frameCounter++;

        int ret = ffmpeg.avcodec_send_frame(codecCtx, convertedFrame);
        if (ret < 0)
            throw new ApplicationException("Failed to send frame: " + GetErrorText(ret));

        while (ret >= 0)
        {
            ret = ffmpeg.avcodec_receive_packet(codecCtx, packet);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                break;
            else if (ret < 0)
                throw new ApplicationException("Error during encoding: " + GetErrorText(ret));

            ffmpeg.av_packet_rescale_ts(packet, codecCtx->time_base, stream->time_base);
            packet->stream_index = stream->index;

            ret = ffmpeg.av_interleaved_write_frame(formatCtx, packet);
            if (ret < 0)
                throw new ApplicationException("Error writing packet: " + GetErrorText(ret));

            ffmpeg.av_packet_unref(packet);
        }
    }

    public void Dispose()
    {

    }

    public void Finish()
    {
        // Flush encoder
        int ret = ffmpeg.avcodec_send_frame(codecCtx, null);
        while (ret >= 0)
        {
            ret = ffmpeg.avcodec_receive_packet(codecCtx, packet);
            if (ret == ffmpeg.AVERROR_EOF || ret == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                break;

            ffmpeg.av_packet_rescale_ts(packet, codecCtx->time_base, stream->time_base);
            packet->stream_index = stream->index;
            ffmpeg.av_interleaved_write_frame(formatCtx, packet);
            ffmpeg.av_packet_unref(packet);
        }

        ffmpeg.av_write_trailer(formatCtx);

        // Cleanup
        ffmpeg.sws_freeContext(swsCtx);
        var _convertedFrame = convertedFrame;
        ffmpeg.av_frame_free(&_convertedFrame);

        var _codecCtx = codecCtx;
        ffmpeg.avcodec_free_context(&_codecCtx);

        var _packet = packet;
        ffmpeg.av_packet_free(&_packet);
        ffmpeg.avio_closep(&formatCtx->pb);
        ffmpeg.avformat_free_context(formatCtx);
    }

    private string GetErrorText(int error)
    {
        const int bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
        return Marshal.PtrToStringAnsi((IntPtr)buffer);
    }
}
