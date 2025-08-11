using FFmpegMediaFramework.Decoder;
using FFmpeg.AutoGen;
using System;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
using UnityEngine;

/// <summary>
/// 音频解码器
/// </summary>
public class AudioDecoder : DecoderBase
{
    public unsafe AudioDecoder(AVCodecParameters* codecpar, AVHWDeviceType hWDeviceType) : base(codecpar)
    {
       
        AVCodec* codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_MP3);
        //打开解码器
        int ret = ffmpeg.avcodec_open2(CodecContext, codec, null);
        if (ret < 0)
            throw new ApplicationException($"Failed to open codec: {ret}");
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
