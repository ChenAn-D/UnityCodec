using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.VisualScripting;
using UnityEngine;

public unsafe static class FFmpegHelper
{

    /// <summary>
    /// 注册FFmpeg库
    /// </summary>
    public static void RegisterFFmpegBinaries()
    {
        string arch = IntPtr.Size == 8 ? "x64" : "x86";
        string ffmpegPath = Path.Combine(Application.dataPath, "Plugins/FFmpegCodec/Plugins/FFmpeg", arch);
        Environment.SetEnvironmentVariable("PATH", $"{ffmpegPath};{Environment.GetEnvironmentVariable("PATH")}");
        ffmpeg.RootPath = ffmpegPath;
    }

    public static void SetupFFmpeg()
    {
        ffmpeg.avdevice_register_all();
        ffmpeg.avformat_network_init();
    }

    public static unsafe string av_strerror(int error)
    {
        var bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
        var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
        return message;
    }

    public static int ThrowExceptionIfError(this int error)
    {
        if (error < 0) throw new ApplicationException(av_strerror(error));
        return error;
    }
    /// <summary>
    /// 配置日志输出
    /// </summary>
    public static unsafe void SetupLogging()
    {
        ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

        // do not convert to local function
        av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
        {
            if (level > ffmpeg.av_log_get_level()) return;

            var lineSize = 1024;
            var lineBuffer = stackalloc byte[lineSize];
            var printPrefix = 1;
            ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
            var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);
            UnityEngine.Debug.Log(line);
        };

        ffmpeg.av_log_set_callback(logCallback);
    }

    /// <summary>
    /// 配置硬件解码器
    /// </summary>
    /// <param name="HWtype"></param>
    public static void ConfigureHWDecoder(ref AVHWDeviceType HWtype)
    {
        var availableHWDecoders = new List<AVHWDeviceType>();
        var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            AVBufferRef* hw_device_ctx = null;
            //判断硬件是否初始化
            availableHWDecoders.Add(type);
        }

        if (availableHWDecoders.Count == 0)
        {
            UnityEngine.Debug.Log("系统没有硬件解码器");
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            return;
        }

        foreach (var item in availableHWDecoders)
        {
            if (item == HWtype)
            {
                UnityEngine.Debug.Log($"系统支持的硬件解码器{HWtype}");
                return;
            }
        }

        UnityEngine.Debug.LogWarning($"系统不支持支持的硬件解码器{HWtype},将不使用硬件解码器加速");
        HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
    }
}
