using System;
using System.Collections.Generic;
using UnityEngine;

public class AudioPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    private AudioClip clip;

    private readonly Queue<float> queue = new Queue<float>();
    private object lockObj = new object();

    public void Start()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.Play();
    }



    public void PushAudio(byte[] pcmBuffer, int bytesPerSample, int channels, int sampleRate, bool isFloatFormat)
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

        lock (lockObj)
        {
            foreach (var s in floatBuffer)
                queue.Enqueue(s);
        }
    }

    public void NotchFilter(float[] samples, float sampleRate, float notchFreq, float q)
    {
        float w0 = 2 * Mathf.PI * notchFreq / sampleRate;
        float alpha = Mathf.Sin(w0) / (2 * q);
        float cosw0 = Mathf.Cos(w0);

        float b0 = 1;
        float b1 = -2 * cosw0;
        float b2 = 1;
        float a0 = 1 + alpha;
        float a1 = -2 * cosw0;
        float a2 = 1 - alpha;

        b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;

        float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float x0 = samples[i];
            float y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            samples[i] = y0;
            x2 = x1; x1 = x0;
            y2 = y1; y1 = y0;
        }
    }


    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (lockObj)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (queue.Count > 0)
                    data[i] = queue.Dequeue();
                else
                    data[i] = 0f; // 缓冲不足就填 0（静音）
            }
        }
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
        audioSource.Stop();
        // 创建或更新 AudioClip
        if (clip == null) clip = AudioClip.Create("DecodedAudio", sampleCount / channels, channels, sampleRate, false);
        clip.SetData(floatBuffer, 0);
        // 播放
        audioSource.clip = clip;
        audioSource.Play();
    }
}
