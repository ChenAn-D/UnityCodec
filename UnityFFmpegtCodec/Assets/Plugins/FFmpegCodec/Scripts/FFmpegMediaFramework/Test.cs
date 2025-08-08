using FFmpeg.AutoGen;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class Test : MonoBehaviour
{
    public string url = "rtmp://58.222.192.76:1935/rtp/34020000001180003200_34020000001310000001";
    public Button Play_Btn;
    public Button Stop_Btn;
    [Space(10)]
    public Toggle Puase_Toggle;
    public Button StartRecording;
    public Button EndRecording;
    public Button CaptureFrame_Btn;
    [Space(10)]
    public Slider slider;

    public Image Play_Img;
    public Image Loading_Icon;

    [Space(10)]
    public AVHWDeviceType HWDevice = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

    private VideoStreamDecoder decoder;
    private bool IsPlaying = false;
    private bool IsLoading = false;
    private Texture2D _videoTexture;
    MediaPlayer mediaPlayer;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        //注册FFmpeg库
        FFmpegHelper.RegisterFFmpegBinaries();
        DynamicallyLoadedBindings.Initialize();

        FFmpegHelper.SetupFFmpeg();
        //配置输出日志
        FFmpegHelper.SetupLogging();


        Play_Btn.onClick.AddListener(PlayVedio);
        Stop_Btn.onClick.AddListener(StopVedio);
        Puase_Toggle.onValueChanged.AddListener(OnPuase);
        StartRecording.onClick.AddListener(OnStartRecording);
        EndRecording.onClick.AddListener(OnEndRecording);
        CaptureFrame_Btn.onClick.AddListener(OnCaptureFrame);
        slider.onValueChanged.AddListener(OnPressChange);

        mediaPlayer = new MediaPlayer(url, HWDevice);

        mediaPlayer.OnVideoFrame += OnVideoFrameCallBack;

        mediaPlayer.OnAudioFrame += OnAudioFrameCallBack;

    }

    private void PlayVedio()
    {
        IsPlaying = true;
        mediaPlayer.Start();
    }

    private void StopVedio()
    {
        mediaPlayer.Stop();
        IsPlaying = false;
    }

    private void OnPuase(bool puase)
    {
        mediaPlayer.ChangePlayState(puase ? MediaPlayerState.Paused : MediaPlayerState.Playing);
    }

    private void OnStartRecording()
    {

    }

    private void OnEndRecording()
    {

    }

    private void OnCaptureFrame()
    {

    }

    private void OnPressChange(float arg0)
    {

    }

    unsafe void OnVideoFrameCallBack(IntPtr ptr)
    {
        AVFrame* frame = (AVFrame*)ptr;
        SetFrame(frame);
        // 转 Texture2D 等操作
        Debug.Log(frame->best_effort_timestamp);
    }

    unsafe void OnAudioFrameCallBack(IntPtr ptr)
    {
        AVFrame* frame = (AVFrame*)ptr;

        Debug.Log(frame->best_effort_timestamp);
    }

    private unsafe void SetFrame(AVFrame* convertedFrame)
    {
        int width = convertedFrame->width;
        int height = convertedFrame->height;
        int stride = convertedFrame->linesize[0];
        byte* source = convertedFrame->data[0];
        float timer = (float)FFmpegMgr.Instance().GetCurrentProgress(decoder);

        byte[] pixelData = new byte[height * stride];
        for (int y = 0; y < height; y++)
        {
            IntPtr srcRowPtr = (IntPtr)(source + (height - 1 - y) * stride);
            Marshal.Copy(srcRowPtr, pixelData, y * stride, stride);
        }

        MainThreadDispatcher.Enqueue(() =>
        {
            if (!IsPlaying) return;
            if (_videoTexture == null)
            {
                _videoTexture = new Texture2D(width, height, TextureFormat.BGRA32, false);
                if (Play_Img != null)
                {
                    Play_Img.sprite = Sprite.Create(_videoTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
                }
            }

            _videoTexture.LoadRawTextureData(pixelData);
            _videoTexture.Apply(false);

            //更新进度条
            slider.SetValueWithoutNotify(timer);
        });
    }

    // Update is called once per frame
    void Update()
    {
        Loading();
    }

    private void Loading()
    {
        Loading_Icon.gameObject.SetActive(IsLoading);
        if (IsLoading)
        {
            Loading_Icon.transform.Rotate(new Vector3(0, 0, -1));
        }
    }

    private void OnDestroy()
    {
        mediaPlayer?.Dispose();
    }
}
