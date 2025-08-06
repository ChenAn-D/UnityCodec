using FFmpeg.AutoGen;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class FFmpegPlay : MonoBehaviour
{
    public string url = "rtmp://58.222.192.76:1935/rtp/34020000001180003200_34020000001310000001";
    public Button Play_Btn;
    public Button Stop_Btn;
    public Image Play_Img;
    public Image Loading_Icon;

    [Space(10)]
    public AVHWDeviceType HWDevice = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

    private VideoStreamDecoder decoder;
    private bool IsPlaying = false;
    private bool IsLoading = false;
    private Texture2D _videoTexture;

    void Start()
    {
        Play_Btn.onClick.AddListener(PlayVedio);
        Stop_Btn.onClick.AddListener(StopVedio);
        FFmpegMgr.Instance().Init();
    }

    private void PlayVedio()
    {
        if (IsLoading) return;
        IsPlaying = false;
        IsLoading = true;
        if (decoder != null)
        {
            StopVedio();
        }
        FFmpegMgr.Instance().OpenVedio(url, HWDevice, OpenVedioCallBack, OnAVFrameCallBack);
    }

    private void StopVedio()
    {
        IsPlaying = false;
        FFmpegMgr.Instance().DisposeDecoder(decoder);
        decoder = null;

        Play_Img.sprite = null;
        _videoTexture = null;
    }

    private unsafe void OpenVedioCallBack(VideoStreamDecoder decoder)
    {
        IsLoading = false;

        if (decoder == null)
        {
            return;
        }

        this.decoder = decoder;
        IsPlaying = true;
    }

    private void OnAVFrameCallBack(AVFrame frame)
    {
        SetFrame(frame);
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

    private unsafe void SetFrame(AVFrame* convertedFrame)
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
                if (Play_Img != null)
                {
                    Play_Img.sprite = Sprite.Create(_videoTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
                    //Image.rectTransform.sizeDelta = new Vector2(width, height);
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

    private unsafe void SetFrame(AVFrame convertedFrame)
    {
        int width = convertedFrame.width;
        int height = convertedFrame.height;
        int stride = convertedFrame.linesize[0];
        byte* source = convertedFrame.data[0];

        MainThreadDispatcher.Enqueue(() =>
        {
            if (source == null) return;
            if (_videoTexture == null)
            {
                _videoTexture = new Texture2D(width, height, TextureFormat.BGRA32, false);
                if (Play_Img != null)
                {
                    Play_Img.sprite = Sprite.Create(_videoTexture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
                    //Image.rectTransform.sizeDelta = new Vector2(width, height);
                }
            }

            byte[] pixelData = new byte[height * stride];
            for (int y = 0; y < height; y++)
            {
                IntPtr srcRowPtr = (IntPtr)(source + (height - 1 - y) * stride);
                Marshal.Copy(srcRowPtr, pixelData, y * stride, stride);
            }

            _videoTexture.LoadRawTextureData(pixelData);
            _videoTexture.Apply(false);

        });
    }
}
