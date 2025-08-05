using FFmpeg.AutoGen;
using UnityEngine;
using System.IO;
using System;
using System.Runtime.InteropServices;
using System.Linq;
using UnityEngine.UI;

public unsafe class FFmpegTest : MonoBehaviour
{
    public string video_path;
    public Image Image;
    void Start()
    {
        Init();
    }

    public void Init()
    {
        //注册FFmpeg库
        FFmpegHelper.RegisterFFmpegBinaries();
        DynamicallyLoadedBindings.Initialize();

        FFmpegHelper.SetupFFmpeg();
        //配置输出日志
        FFmpegHelper.SetupLogging();
        //配置硬件解码器
        AVHWDeviceType deviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
        FFmpegHelper.ConfigureHWDecoder(ref deviceType);
        //解码
        DecodeAllFramesToImages(video_path, deviceType);
    }

    /// <summary>
    /// 解码视频所有帧为图像（支持硬解码）
    /// </summary>
    /// <param name="video_path">视频路径</param>
    /// <param name="HWDevice">硬编码类型</param>
    private unsafe void DecodeAllFramesToImages(string url, AVHWDeviceType HWDevice)
    {
        // 创建视频解码器实例，传入视频路径和硬件解码类型
        _vsd = new VideoStreamDecoder(url, HWDevice);

        Debug.Log($"解码器名称: {_vsd.CodecName}");

        // 获取视频解码器上下文信息（如分辨率、格式等），并逐条打印
        var info = _vsd.GetContextInfo();
        info.ToList().ForEach(x =>
        {
            Debug.Log($"{x.Key} = {x.Value}");
        });

        _hwDevice = HWDevice;

        _frameNumber = 0;
        Debug.Log($"总帧数{_vsd.TotalFrames()}");

        // 设置播放标记为 true，准备开始解码和播放
        _isPlaying = true;
    }

    private VideoStreamDecoder _vsd;
    private VideoFrameConverter _vfc;
    private AVHWDeviceType _hwDevice;
    private int _frameNumber = 0;
    private bool _isPlaying = false;

    private unsafe void Update()
    {
        if (!_isPlaying) return;

        if (_vsd.TryDecodeNextFrame(out var frame))
        {
            if (_vfc == null)
            {

                var inputPixelFormat = _hwDevice == AVHWDeviceType.AV_HWDEVICE_TYPE_NONE
? _vsd.PixelFormat
: GetHWPixelFormat(_hwDevice);
                // 某些旧格式（如 YUVJ420P）已废弃，需要替换为标准格式
                if (inputPixelFormat == AVPixelFormat.AV_PIX_FMT_YUVJ420P) inputPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
                var pCode = ffmpeg.avcodec_find_decoder_by_name(_vsd.CodecName);
                _vfc = new VideoFrameConverter(_vsd.FrameSize, inputPixelFormat, _vsd.FrameSize, AVPixelFormat.@AV_PIX_FMT_BGRA);

            }

            if (_vfc == null)
            {
                return;
            }

            var convertedFrame = _vfc.Convert(frame);
            // TODO: 显示convertedFrame
            SetFrame(convertedFrame);

            //_frameNumber++;
            //if (_frameNumber > 1000)
            //{
            //    _isPlaying = false;
            //    // 可以选择释放资源或重置
            //    DisposeDecoder();
            //}

        }
        else
        {
            // 视频解码结束，可以重置或停止
            Debug.Log("Video decoding finished.");
            _isPlaying = false;
            DisposeDecoder();
        }
    }

    private void OnDestroy()
    {
        DisposeDecoder();
    }

    private void DisposeDecoder()
    {
        _vfc?.Dispose();
        _vsd?.Dispose();
        _vfc = null;
        _vsd = null;
    }

    private Texture2D _videoTexture;
    private void SetFrame(AVFrame* convertedFrame)
    {
        if (convertedFrame != null)
        {
            int width = convertedFrame->width;
            int height = convertedFrame->height;
            int stride = convertedFrame->linesize[0];
            byte* source = convertedFrame->data[0];

            if (_videoTexture == null)
            {
                _videoTexture = new Texture2D(width, height, TextureFormat.BGRA32, false);
                if (Image != null)
                {
                    Image.sprite = Sprite.Create(_videoTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
                    Image.rectTransform.sizeDelta = new Vector2(width, height);
                }
            }

            // Flip and copy
            byte[] pixelData = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                IntPtr srcRowPtr = (IntPtr)(source + (height - 1 - y) * stride);
                Marshal.Copy(srcRowPtr, pixelData, y * stride, stride);
            }

            _videoTexture.LoadRawTextureData(pixelData);
            _videoTexture.Apply(false);
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
