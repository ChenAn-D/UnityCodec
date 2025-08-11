﻿using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using UnityEngine;

namespace FFmpegMediaFramework.Decoder
{
    public unsafe abstract class DecoderBase : IDecoder
    {
        public AVCodecContext* CodecContext { get; protected set; }

        private readonly AVFrame* _pFrame;
        private readonly AVPacket* _pPacket;
        private readonly AVFrame* _receivedFrame;

        protected AVCodec* _codec;

        protected DecoderBase(AVCodecParameters* codecpar, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            _codec = ffmpeg.avcodec_find_decoder(codecpar->codec_id);
            _receivedFrame = ffmpeg.av_frame_alloc();
            if (_codec == null)
                throw new InvalidOperationException("Decoder not found.");

            CodecContext = ffmpeg.avcodec_alloc_context3(_codec);
            if (CodecContext == null)
                throw new InvalidOperationException("Failed to allocate codec context.");

            if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                ffmpeg.av_hwdevice_ctx_create(&CodecContext->hw_device_ctx, HWDeviceType, null, null, 0)
                    .ThrowExceptionIfError();
            }

            ffmpeg.avcodec_parameters_to_context(CodecContext, codecpar).ThrowExceptionIfError();
            ffmpeg.avcodec_open2(CodecContext, _codec, null).ThrowExceptionIfError();

            _pPacket = ffmpeg.av_packet_alloc();
            _pFrame = ffmpeg.av_frame_alloc();
        }

        public virtual bool DecodePacket(AVPacket* pkt, out AVFrame* frame)
        {
            frame = null;

            int sendResult = ffmpeg.avcodec_send_packet(CodecContext, pkt);
            if (sendResult < 0 && sendResult != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                return false;

            AVFrame* tempFrame = ffmpeg.av_frame_alloc();
            if (tempFrame == null)
            {
                UnityEngine.Debug.LogError("Failed to allocate frame memory.");
                return false;
            }

            //从解码器中获取解码后的帧数据
            int receiveResult = ffmpeg.avcodec_receive_frame(CodecContext, tempFrame);
            if (receiveResult == 0)
            {
                frame = tempFrame;
                return true;
            }
            else if (receiveResult == ffmpeg.AVERROR(ffmpeg.AVERROR_EOF))
            {
                // 所有的输入数据都已经被解码并返回
                frame = tempFrame;
                return false;
            }
            else if (receiveResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                // 需要更多数据来解码当前帧
                frame = tempFrame;
                return false;
            }
            else
            {
                UnityEngine.Debug.LogError($"Error receiving frame: {receiveResult}");
            }

            ffmpeg.av_frame_free(&tempFrame);
            return false;
        }

        public virtual void Dispose()
        {
            if (CodecContext != null)
            {
                var _CodecContext = CodecContext;
                ffmpeg.avcodec_free_context(&_CodecContext);
                CodecContext = null;
            }
        }
    }

}
