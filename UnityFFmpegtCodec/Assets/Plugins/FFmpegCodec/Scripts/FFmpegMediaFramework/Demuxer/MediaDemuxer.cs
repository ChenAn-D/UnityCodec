using FFmpeg.AutoGen;
using System;

/// <summary>
/// 拆包器（分离音视频）
/// </summary>
internal unsafe class MediaDemuxer : IDisposable
{
    public AVFormatContext* FormatContext { get; private set; }
    public long Duration { get; private set; }

    public MediaDemuxer(string url)
    {
        //分配格式上下文
        FormatContext = ffmpeg.avformat_alloc_context();

        AVFormatContext* fmtCtx = FormatContext;
        ffmpeg.avformat_open_input(&fmtCtx, url, null, null).ThrowExceptionIfError();
        ffmpeg.avformat_find_stream_info(fmtCtx, null).ThrowExceptionIfError();
        Duration = FormatContext->duration;
    }

    public bool ReadPacket(AVPacket* packet)
    {
        return ffmpeg.av_read_frame(FormatContext, packet) >= 0;
    }

    public void Dispose()
    {
        if (FormatContext != null)
        {
            AVFormatContext* fmtCtx = FormatContext;
            ffmpeg.avformat_close_input(&fmtCtx);
            FormatContext = null;
        }
    }
}
