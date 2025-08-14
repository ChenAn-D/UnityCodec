using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class AudioPlayer : MonoBehaviour
{
    public AudioSource audioSource;
    private AudioClip clip;

    private readonly Queue<float> queue = new Queue<float>();
    private object lockObj = new object();

    private int position = 0;
    private int samplerate = 44100; // Ĭ�ϲ�����
    private float frequency = 440;

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
        else if (!isFloatFormat && bytesPerSample == 4)
        {
            // int16 PCM ת��Ϊ float [-1, 1]
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(pcmBuffer[i * 4] | (pcmBuffer[i * 4 + 1] << 8));
                floatBuffer[i] = sample / 32768f;
            }
        }
        else
        {
            throw new NotSupportedException("��֧�ֵ� PCM ��ʽ");
        }

        lock (lockObj)
        {

            //OnAudioRead(ref floatBuffer, sampleRate);
            foreach (var s in floatBuffer)
            {
                queue.Enqueue(s);

            }
        }
    }

    void OnAudioRead(ref float[] data, int sampleRate)
    {
        int count = 0;
        position = 0;
        while (count < data.Length)
        {
            float x0 = data[count];
            data[count] = Mathf.Sin(2 * Mathf.PI * frequency * x0 / sampleRate);
            position++;
            count++;
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
        //audioSource.Stop();
        //// ��������� AudioClip
        if (clip == null)
        {
            clip = AudioClip.Create("DecodedAudio", sampleCount / channels, channels, sampleRate, false);
            // ����
            audioSource.clip = clip;
        }
        clip?.SetData(floatBuffer, 0);
        audioSource.Play();
    }
}

