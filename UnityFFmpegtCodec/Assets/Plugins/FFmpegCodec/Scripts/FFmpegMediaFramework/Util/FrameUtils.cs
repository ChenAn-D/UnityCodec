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
}
