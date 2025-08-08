using FFmpegMediaFramework.Decoder;
using FFmpeg.AutoGen;

/// <summary>
/// “Ù∆µΩ‚¬Î∆˜
/// </summary>
public class AudioDecoder : DecoderBase
{
    public unsafe AudioDecoder(AVCodecParameters* codecpar, AVHWDeviceType hWDeviceType) : base(codecpar)
    {
    }

    public unsafe override bool DecodePacket(AVPacket* pkt, out AVFrame* frame)
    {
        frame = null;

        if (ffmpeg.avcodec_send_packet(CodecContext, pkt) < 0)
            return false;

        AVFrame* tempFrame = ffmpeg.av_frame_alloc();
        int ret = ffmpeg.avcodec_receive_frame(CodecContext, tempFrame);
        if (ret == 0)
        {
            frame = tempFrame;
            return true;
        }

        ffmpeg.av_frame_free(&tempFrame);
        return false;
    }
}
