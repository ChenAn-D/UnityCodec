using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class VideoSession
{
    public VideoFrameConverter Converter;
    public volatile bool IsCancelled; //结束
    public volatile bool IsPaused;//暂停
    public volatile bool IsRecording; //录制
    public float speed = 1; //调整播放速度
    public AVFrame CaptureFrame;//当前帧图片，为截图做准备
    public bool SeekRequested;//跳转播放进度
    public float progress;//当前播放进度
    public string Recorder_Path;
    public FFmpegMp4Encoder Recorder;
    public VideoFrameConverter Recorder_Converter;
}

public class FFmpegMgr : Single<FFmpegMgr>
{
    private Dictionary<VideoStreamDecoder, VideoSession> video_dic = new Dictionary<VideoStreamDecoder, VideoSession>();
    private readonly object videoDicLock = new object();
    private bool isRunning = true;

    double lastPtsSeconds = 0;
    bool isFirstFrame = true;

    public void Init()
    {
        //注册FFmpeg库
        FFmpegHelper.RegisterFFmpegBinaries();
        DynamicallyLoadedBindings.Initialize();

        FFmpegHelper.SetupFFmpeg();
        //配置输出日志
        FFmpegHelper.SetupLogging();
        //查找和生成需要调用主线程的类
        var MainThreadDispatcher = UnityEngine.GameObject.FindAnyObjectByType<MainThreadDispatcher>();
        if (MainThreadDispatcher == null) MainThreadDispatcher = new GameObject("MainThreadDispatcher").AddComponent<MainThreadDispatcher>();
    }

    public void DisposeDecoderAll()
    {

        foreach (var pair in video_dic)
        {
            if (pair.Value != null) pair.Value.IsCancelled = true;
        }
        video_dic.Clear();
        isRunning = false;
    }

    public void DisposeDecoder(VideoStreamDecoder decoder)
    {

        if (decoder == null || !video_dic.TryGetValue(decoder, out var session)) return;
        session.IsCancelled = true;

        lock (videoDicLock)
        {
            video_dic.Remove(decoder);
        }
    }

    /// <summary>
    /// 暂停
    /// </summary>
    /// <param name="decoder"></param>
    /// <param name="pause"></param>
    public void PauseDecoder(VideoStreamDecoder decoder, bool pause)
    {
        if (decoder != null && video_dic.TryGetValue(decoder, out var session))
        {
            lock (videoDicLock)
            {
                session.IsPaused = pause;
            }
        }
    }


    /// <summary>
    /// 跳转
    /// </summary>
    /// <param name="seconds"></param>
    /// <param name="progress">播放进度</param>
    /// <returns></returns>
    public unsafe bool Seek(VideoStreamDecoder decoder, float progress)
    {
        if (decoder != null && video_dic.TryGetValue(decoder, out var session))
        {

            lock (videoDicLock)
            {
                if (decoder.GetDuration() <= 0) return false;

                session.SeekRequested = true;
                session.progress = progress; //记录当前进度
                return true;
            }
        }

        return false;

    }

    /// <summary>
    /// 获取当前进度
    /// </summary>
    /// <param name="decoder"></param>
    /// <returns></returns>
    public unsafe double GetCurrentProgress(VideoStreamDecoder decoder)
    {
        if (decoder != null && video_dic.TryGetValue(decoder, out var session))
        {
            lock (videoDicLock)
            {
                var time_base = decoder.GetStream()->time_base;
                long pts = session.CaptureFrame.best_effort_timestamp;
                double timeInSeconds = pts * ffmpeg.av_q2d(time_base);
                var duratuin = decoder.GetDuration();
                if (duratuin <= 0) return 1;
                double totalDuration = decoder.GetDuration() * ffmpeg.av_q2d(time_base);

                return timeInSeconds / totalDuration;
            }

        }

        return 0;
    }

    /// <summary>
    /// 录制
    /// </summary>
    /// <param name="decoder"></param>
    /// <param name="start"></param>
    public void ToggleRecording(VideoStreamDecoder decoder, bool start)
    {
        if (decoder != null && video_dic.TryGetValue(decoder, out var session))
        {
            if (session.IsRecording == start) return;

            session.IsRecording = start;

            if (start)
            {
                string path = $"{Application.streamingAssetsPath}/record_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
                // if (!File.Exists(path)) File.Create(path);
                session.Recorder_Path = path;
                //var fs = File.Open(path, FileMode.Create);
                session.Recorder_Converter = new VideoFrameConverter(decoder.FrameSize, AVPixelFormat.@AV_PIX_FMT_BGRA, decoder.FrameSize, AVPixelFormat.AV_PIX_FMT_YUV420P);

                session.Recorder = new FFmpegMp4Encoder(path, 25, decoder.FrameSize, session.CaptureFrame.best_effort_timestamp);
            }
            else
            {
                //session.Recorder?.DrainAsync();
                session.Recorder?.Finish();
                session.Recorder_Converter?.Dispose();
                session.Recorder = null;
                session.Recorder_Converter = null;
            }

        }
    }

    /// <summary>
    /// 截图
    /// </summary>
    /// <param name="decoder"></param>
    public unsafe void CaptureFrame(VideoStreamDecoder decoder, Action<bool, byte[]> pngDataCall)
    {
        lock (videoDicLock)
        {
            if (decoder != null && video_dic.TryGetValue(decoder, out var session))
            {
                try
                {

                    AVFrame frame = session.CaptureFrame;
                    AVFrame* pFrame = &frame;
                    pngDataCall?.Invoke(true, session.Converter.EncodeFrameToImage(pFrame));
                }
                catch (Exception e)
                {
                    pngDataCall?.Invoke(false, new byte[0]);
                    throw e;
                }

            }
        }
    }

    public void OpenVedio(string url, AVHWDeviceType HWDevice, Action<VideoStreamDecoder> onFrameConverted, Action<AVFrame> av_action)
    {

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                DecodeAllFramesToImages(url, HWDevice, onFrameConverted, av_action);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error decoding video: {ex}\nStackTrace: {ex.StackTrace}");
                onFrameConverted?.Invoke(null);
            }
        });
    }

    /// <summary>
    /// 视频帧读取循环
    /// </summary>
    /// <param name="url"></param>
    /// <param name="HWDevice"></param>
    /// <param name="onFrameConverted"></param>
    /// <param name="av_action"></param>
    private unsafe void DecodeAllFramesToImages(string url, AVHWDeviceType HWDevice, Action<VideoStreamDecoder> onFrameConverted, Action<AVFrame> av_action)
    {
        // 创建视频解码器实例，传入视频路径和硬件解码类型
        var vsd = new VideoStreamDecoder(url, HWDevice);

        /*
        //Debug.Log($"解码器名称: {vsd.CodecName}");
        // 获取视频解码器上下文信息（如分辨率、格式等），并逐条打印

        //var info = vsd.GetContextInfo();
        //info.ToList().ForEach(x =>
        //{
        //    //Debug.Log($"{x.Key} = {x.Value}");
        //});
        */

        var inputPixelFormat = HWDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE ? vsd.PixelFormat : GetHWPixelFormat(HWDevice);
        // 某些旧格式（如 YUVJ420P）已废弃，需要替换为标准格式
        if (inputPixelFormat == AVPixelFormat.AV_PIX_FMT_YUVJ420P) inputPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;

        try
        {
            var vfc = new VideoFrameConverter(vsd.FrameSize, inputPixelFormat, vsd.FrameSize, AVPixelFormat.@AV_PIX_FMT_BGRA);

            if (vfc == null)
            {
                Debug.LogError("VideoFrameConverter creation failed.");
                return;
            }

            lock (videoDicLock)
            {
                var session = new VideoSession { Converter = vfc, IsCancelled = false };
                video_dic[vsd] = session;
                onFrameConverted?.Invoke(vsd);
            }

            bool canSeek = vsd.GetDuration() > 0;
            bool wasSeeked = false;

            while (vsd.TryDecodeNextFrame(out var frame))
            {
                //检测是否停止播放
                if (!video_dic.TryGetValue(vsd, out var currentSession) || currentSession.IsCancelled || !isRunning)
                {
                    vsd?.Dispose();
                    vfc?.Dispose();

                    return;
                }

                //调整播放进度 仅限duration>0的流
                if (currentSession.SeekRequested && canSeek)
                {
                    vsd.SeekToPercent(currentSession.progress);
                    currentSession.SeekRequested = false;
                    wasSeeked = true;
                    isFirstFrame = true; //避免seek后播放间隔为负
                    continue;
                }


                //暂停 如果时实时视频流就过滤当前帧，否则就等待恢复播放
                if (currentSession.IsPaused && !currentSession.IsCancelled && !canSeek)
                {
                    isFirstFrame = true; //避免seek后播放间隔为负
                    continue;
                }

                // 等待恢复播放
                while (currentSession.IsPaused && !currentSession.IsCancelled && !currentSession.SeekRequested && canSeek)
                {
                    Thread.Sleep(10);
                }

                //获取时间戳并计算播放间隔 
                if (!currentSession.SeekRequested)
                {
                    long pts = frame->best_effort_timestamp;
                    AVRational time_base = vsd.GetStream()->time_base;
                    double currentPtsSeconds = pts * ffmpeg.av_q2d(time_base);

                    if (!isFirstFrame && !wasSeeked)
                    {
                        double delay = (currentPtsSeconds - lastPtsSeconds) / currentSession.speed;
                        if (delay > 0)
                        {
                            Thread.Sleep((int)(delay * 1000));
                        }
                    }
                    else
                    {
                        isFirstFrame = false;
                        wasSeeked = false;
                    }

                    lastPtsSeconds = currentPtsSeconds;
                }

                var convertedFrame = vfc.Convert(*frame);

                if (currentSession.IsRecording)
                {
                    var f = currentSession.Recorder_Converter.Convert(*frame);
                    currentSession.Recorder?.EncodeFrame(&f);

                }

                currentSession.CaptureFrame = convertedFrame;
                av_action?.Invoke(convertedFrame);
            }

            DisposeDecoder(vsd);
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            onFrameConverted?.Invoke(null);
        }

    }

    private AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
    {
        return hWDevice switch
        {
            AVHWDeviceType.AV_HWDEVICE_TYPE_NONE => AVPixelFormat.AV_PIX_FMT_NONE,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VDPAU => AVPixelFormat.AV_PIX_FMT_VDPAU,
            AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA => AVPixelFormat.AV_PIX_FMT_CUDA,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI => AVPixelFormat.AV_PIX_FMT_VAAPI,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2 => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_QSV => AVPixelFormat.AV_PIX_FMT_QSV,
            AVHWDeviceType.AV_HWDEVICE_TYPE_VIDEOTOOLBOX => AVPixelFormat.AV_PIX_FMT_VIDEOTOOLBOX,
            AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA => AVPixelFormat.AV_PIX_FMT_NV12,
            AVHWDeviceType.AV_HWDEVICE_TYPE_DRM => AVPixelFormat.AV_PIX_FMT_DRM_PRIME,
            AVHWDeviceType.AV_HWDEVICE_TYPE_OPENCL => AVPixelFormat.AV_PIX_FMT_OPENCL,
            AVHWDeviceType.AV_HWDEVICE_TYPE_MEDIACODEC => AVPixelFormat.AV_PIX_FMT_MEDIACODEC,
            _ => AVPixelFormat.AV_PIX_FMT_NONE
        };
    }

}
