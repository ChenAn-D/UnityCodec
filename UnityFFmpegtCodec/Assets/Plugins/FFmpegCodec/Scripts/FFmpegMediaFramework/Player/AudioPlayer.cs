using UnityEngine;
using System;

public class AudioPlayer : MonoBehaviour
{
    public AudioSource audioSource;

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
}
