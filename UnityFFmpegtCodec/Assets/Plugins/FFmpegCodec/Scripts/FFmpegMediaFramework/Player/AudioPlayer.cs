using UnityEngine;
using System;
using UnityEngine.Audio;
using UnityEngine.LightTransport;
using System.Collections.Generic;
using System.Collections;

public class AudioPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    private AudioClip clip;

    private float[] audioBuffer;  // ���λ�����
    private int bufferWritePos = 0;
    private int bufferReadPos = 0;
    private int bufferSize = 48000 * 10; // 10�뻺�棬ʾ��
    private object bufferLock = new object();
    public void Start()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.Play();
    }

    private readonly Queue<float> queue = new Queue<float>();
    private object lockObj = new object();

    public void PushAudio(byte[] pcmBuffer, int bytesPerSample, int channels, int sampleRate, bool isFloatFormat)
    {
        if (pcmBuffer == null || pcmBuffer.Length == 0)
        {
            Debug.LogWarning("PCM ������Ϊ�գ��޷�����");
            return;
        }

        int sampleCount = pcmBuffer.Length / bytesPerSample;
        float[] floatBuffer = new float[sampleCount];

        if (isFloatFormat && bytesPerSample == 4)
        {
            // ֱ�Ӹ��� float PCM
            Buffer.BlockCopy(pcmBuffer, 0, floatBuffer, 0, pcmBuffer.Length);
        }
        else if (!isFloatFormat && bytesPerSample == 2)
        {
            // int16 PCM ת��Ϊ float [-1, 1]
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(pcmBuffer[i * 2] | (pcmBuffer[i * 2 + 1] << 8));
                floatBuffer[i] = sample / 32768f;
            }
        }
        else
        {
            throw new NotSupportedException("��֧�ֵ� PCM ��ʽ");
        }

        lock (lockObj)
        {
            foreach (var s in floatBuffer)
                queue.Enqueue(s);
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
                    data[i] = 0f; // ���岻����� 0��������
            }
        }
    }

    /// <summary>
    /// ���� PCM ����
    /// </summary>
    /// <param name="pcmBuffer">PCM ԭʼ����</param>
    /// <param name="bytesPerSample">ÿ���������ֽ������� 2 = 16bit��4 = float32��</param>
    /// <param name="channels">������</param>
    /// <param name="sampleRate">������</param>
    /// <param name="isFloatFormat">�Ƿ�Ϊ float PCM ��ʽ</param>
    public void PlayPCM(byte[] pcmBuffer, int bytesPerSample, int channels, int sampleRate, bool isFloatFormat)
    {
        if (pcmBuffer == null || pcmBuffer.Length == 0)
        {
            Debug.LogWarning("PCM ������Ϊ�գ��޷�����");
            return;
        }

        int sampleCount = pcmBuffer.Length / bytesPerSample;
        float[] floatBuffer = new float[sampleCount];

        if (isFloatFormat && bytesPerSample == 4)
        {
            // ֱ�Ӹ��� float PCM
            Buffer.BlockCopy(pcmBuffer, 0, floatBuffer, 0, pcmBuffer.Length);
        }
        else if (!isFloatFormat && bytesPerSample == 2)
        {
            // int16 PCM ת��Ϊ float [-1, 1]
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(pcmBuffer[i * 2] | (pcmBuffer[i * 2 + 1] << 8));
                floatBuffer[i] = sample / 32768f;
            }
        }
        else
        {
            throw new NotSupportedException("��֧�ֵ� PCM ��ʽ");
        }
        audioSource.Stop();
        // ��������� AudioClip
        if (clip == null) clip = AudioClip.Create("DecodedAudio", sampleCount / channels, channels, sampleRate, false);
        clip.SetData(floatBuffer, 0);
        // ����
        audioSource.clip = clip;
        audioSource.Play();
    }
}
