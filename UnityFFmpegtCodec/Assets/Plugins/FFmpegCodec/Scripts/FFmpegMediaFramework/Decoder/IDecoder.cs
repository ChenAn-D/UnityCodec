using FFmpeg.AutoGen;

namespace FFmpegMediaFramework.Decoder
{
    internal interface IDecoder
    {
        unsafe AVCodecContext* CodecContext { get; }
        unsafe bool DecodePacket(AVPacket* pkt, out AVFrame* frame);
        void Dispose();
    }
}
