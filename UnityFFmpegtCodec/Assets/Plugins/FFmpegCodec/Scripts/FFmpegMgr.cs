using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class VideoSession
{
    public VideoFrameConverter Converter;
    public volatile bool IsCancelled;
    public volatile bool IsPaused;
    public volatile bool IsRecording;
    public AVFrame CaptureFrame;
}

public class FFmpegMgr : Single<FFmpegMgr>
{
    private Dictionary<VideoStreamDecoder, VideoSession> video_dic = new Dictionary<VideoStreamDecoder, VideoSession>();
    private readonly object videoDicLock = new object();
    private bool isRunning = true;

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
            session.IsPaused = pause;
        }
    }

    /// <summary>
    /// 录制
    /// </summary>
    /// <param name="decoder"></param>
    /// <param name="start"></param>
    public void ToggleRecording(VideoStreamDecoder decoder, bool start)
    {
        //if (decoder != null && video_dic.TryGetValue(decoder, out var session))
        //{
        //    session.IsRecording = start;
        //    if (start)
        //    {
        //        string path = $"{Application.persistentDataPath}/record_{DateTime.Now:yyyyMMdd_HHmmss}";
        //        session.Recorder = new FFmpegRecorder();
        //        session.Recorder.Start(path);
        //    }
        //    else
        //    {
        //        session.Recorder?.Stop();
        //        session.Recorder = null;
        //    }
        //}
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

    private unsafe void DecodeAllFramesToImages(string url, AVHWDeviceType HWDevice, Action<VideoStreamDecoder> onFrameConverted, Action<AVFrame> av_action)
    {
        // 创建视频解码器实例，传入视频路径和硬件解码类型
        var vsd = new VideoStreamDecoder(url, HWDevice);
        //Debug.Log($"解码器名称: {vsd.CodecName}");
        // 获取视频解码器上下文信息（如分辨率、格式等），并逐条打印

        //var info = vsd.GetContextInfo();
        //info.ToList().ForEach(x =>
        //{
        //    //Debug.Log($"{x.Key} = {x.Value}");
        //});

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

            while (vsd.TryDecodeNextFrame(out var frame))
            {
                if (!video_dic.TryGetValue(vsd, out var currentSession) || currentSession.IsCancelled || !isRunning)
                {
                    vsd?.Dispose();
                    vfc?.Dispose();
                    break;
                }

                //如果时实时视频流就过滤当前帧，否则就等待恢复播放
                if (currentSession.IsPaused && !currentSession.IsCancelled && vsd.TotalFrames() == 0) continue;

                // 等待恢复播放
                while (currentSession.IsPaused && !currentSession.IsCancelled)
                {
                    Thread.Sleep(10);
                }

                var convertedFrame = vfc.Convert(*frame);
                currentSession.CaptureFrame = convertedFrame;
                av_action?.Invoke(convertedFrame);
            }

        }
        catch (Exception e)
        {
            Debug.LogError(e);
            onFrameConverted?.Invoke(null);
        }


    }

    private static AVPixelFormat GetHWPixelFormat(AVHWDeviceType hWDevice)
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
