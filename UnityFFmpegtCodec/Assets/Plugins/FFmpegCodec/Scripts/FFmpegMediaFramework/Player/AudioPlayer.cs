using UnityEngine;
using System;

public class AudioPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    private AudioClip clip;
    public void Start()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    // 传入解码后的 PCM 数据（byte[]），并播放
    public void PlayPCM(byte[] pcmData, int channels, int sampleRate, bool isFloat)
    {
        int sampleCount = pcmData.Length / (isFloat ? sizeof(float) : sizeof(short));
        float[] samples = new float[sampleCount];

        if (isFloat)
        {
            // F32 格式直接拷贝
            Buffer.BlockCopy(pcmData, 0, samples, 0, pcmData.Length);
        }
        else
        {
            // S16 格式转换到 float (-32768~32767 => -1~1)
            for (int i = 0; i < sampleCount; i++)
                samples[i] = BitConverter.ToInt16(pcmData, i * 2) / 32768f;
        }

        AudioClip clip = AudioClip.Create("FFmpegAudio", sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);

        audioSource.clip = clip;
        audioSource.Play();
    }

    /// <summary>
    /// 播放 PCM 数据
    /// </summary>
    /// <param name="pcmBuffer">PCM 原始数据</param>
    /// <param name="bytesPerSample">每个采样的字节数（如 2 = 16bit，4 = float32）</param>
    /// <param name="channels">声道数</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="isFloatFormat">是否为 float PCM 格式</param>
    public void PlayPCM(byte[] pcmBuffer, int bytesPerSample, int channels, int sampleRate, bool isFloatFormat)
    {
        if (pcmBuffer == null || pcmBuffer.Length == 0)
        {
            Debug.LogWarning("PCM 缓冲区为空，无法播放");
            return;
        }

        int sampleCount = pcmBuffer.Length / bytesPerSample;
        float[] floatBuffer = new float[sampleCount];

        if (isFloatFormat && bytesPerSample == 4)
        {
            // 直接复制 float PCM
            Buffer.BlockCopy(pcmBuffer, 0, floatBuffer, 0, pcmBuffer.Length);
        }
        else if (!isFloatFormat && bytesPerSample == 2)
        {
            // int16 PCM 转换为 float [-1, 1]
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(pcmBuffer[i * 2] | (pcmBuffer[i * 2 + 1] << 8));
                floatBuffer[i] = sample / 32768f;
            }
        }
        else
        {
            throw new NotSupportedException("不支持的 PCM 格式");
        }

        // 创建或更新 AudioClip
        clip = AudioClip.Create("DecodedAudio", sampleCount / channels, channels, sampleRate, false);
        clip.SetData(floatBuffer, 0);

        // 播放
        audioSource.clip = clip;
        audioSource.Play();
    }
}
