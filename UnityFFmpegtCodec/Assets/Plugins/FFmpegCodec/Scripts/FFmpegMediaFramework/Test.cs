using FFmpeg.AutoGen;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class Test : MonoBehaviour
{
    public string url = "http://zhibo.hkstv.tv/livestream/mutfysrq/playlist.m3u8"; //"rtmp://58.222.192.76:1935/rtp/34020000001180003200_34020000001310000001";
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

    private bool IsPlaying = false;
    private bool IsLoading = false;
    private Texture2D _videoTexture;
    MediaPlayer mediaPlayer;

    AudioPlayer audioPlayer;
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
        mediaPlayer.EndPlayAction += OnAudioEndPlayCallBack;
        mediaPlayer.CurrentPrsAction += OnCurrentPrsCallBack;
        mediaPlayer.LoadingAction += OnLoadingAVStreamCallBack;

        audioPlayer = transform.GetComponent<AudioPlayer>();
    }

    private void OnLoadingAVStreamCallBack(bool loading)
    {
        IsLoading = loading;
    }

    /// <summary>
    /// 当前进度
    /// </summary>
    /// <param name="press"></param>
    private void OnCurrentPrsCallBack(float press)
    {
        MainThreadDispatcher.Enqueue(() =>
        {
            //更新进度条
            slider.SetValueWithoutNotify(press);
        });
    }

    private void OnAudioEndPlayCallBack(bool auto)
    {
        Debug.Log("播放结束");

        MainThreadDispatcher.Enqueue(() =>
        {
            IsPlaying = false;
            Play_Img.sprite = null;
            _videoTexture = null;
        });
    }

    private void PlayVedio()
    {
        IsPlaying = true;
        Task.Run(async () => { await mediaPlayer.Start(); });
    }

    private void StopVedio()
    {
        mediaPlayer.Stop();
        IsPlaying = false;
    }

    private void OnPuase(bool puase)
    {
        Puase_Toggle.transform.GetComponentInChildren<Text>().text = puase ? "继续" : "暂停";
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
        mediaPlayer.CaptureFrame((b, data) =>
        {
            if (b)
            {
                string path = $"{Application.streamingAssetsPath}/screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";

                System.IO.File.WriteAllBytes(path, data);
                Debug.Log("Screenshot saved: " + path);
                return;
            }
            Debug.LogWarning("截图失败");
        });
    }

    private void OnPressChange(float arg0)
    {
        bool can_press = mediaPlayer.SeekToPercent(arg0);
        if (can_press) IsPlaying = true;
    }

    unsafe void OnVideoFrameCallBack(IntPtr ptr)
    {
        AVFrame* frame = (AVFrame*)ptr;
        SetFrame(frame);

    }

    //unsafe void OnAudioFrameCallBack(IntPtr buffer, int size)
    unsafe void OnAudioFrameCallBack(byte[] buffer)
    {
        // AVFrame* frame = (AVFrame*)ptr;
        //int floatCount = size / sizeof(float);
        //byte[] managedData = new byte[floatCount];
        //Marshal.Copy(buffer, managedData, 0, floatCount);
        mediaPlayer.GetAudioInitData(out int channels, out int sampleRate);

        MainThreadDispatcher.Enqueue(() =>
        {
            //audioPlayer?.PlayPCM(buffer, channels, sampleRate, true);
            //audioPlayer?.PlayPCM(buffer, 4, channels, sampleRate, true);
            audioPlayer?.PushAudio(buffer, 4, channels, sampleRate, true);

        });
    }

    private unsafe void SetFrame(AVFrame* convertedFrame)
    {
        int width = convertedFrame->width;
        int height = convertedFrame->height;
        int stride = convertedFrame->linesize[0];
        byte* source = convertedFrame->data[0];

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
