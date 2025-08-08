using FFmpeg.AutoGen;
using FFmpegMediaFramework.Decoder;

/// <summary>
/// ÊÓÆµ½âÂëÆ÷
/// </summary>
public class VideoDecoder : DecoderBase
{
    public unsafe VideoDecoder(AVCodecParameters* codecpar, AVHWDeviceType hWDeviceType) : base(codecpar)
    {

    }

    public unsafe AVPixelFormat PixelFormat => CodecContext->pix_fmt;
    public unsafe int Width => CodecContext->width;
    public unsafe int Height => CodecContext->height;

}
