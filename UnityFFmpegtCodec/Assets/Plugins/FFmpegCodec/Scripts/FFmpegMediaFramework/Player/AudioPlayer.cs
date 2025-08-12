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

    // ��������� PCM ���ݣ�byte[]����������
    public void PlayPCM(byte[] pcmData, int channels, int sampleRate, bool isFloat)
    {
        int sampleCount = pcmData.Length / (isFloat ? sizeof(float) : sizeof(short));
        float[] samples = new float[sampleCount];

        if (isFloat)
        {
            // F32 ��ʽֱ�ӿ���
            Buffer.BlockCopy(pcmData, 0, samples, 0, pcmData.Length);
        }
        else
        {
            // S16 ��ʽת���� float (-32768~32767 => -1~1)
            for (int i = 0; i < sampleCount; i++)
                samples[i] = BitConverter.ToInt16(pcmData, i * 2) / 32768f;
        }

        AudioClip clip = AudioClip.Create("FFmpegAudio", sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);

        audioSource.clip = clip;
        audioSource.Play();
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

        // ��������� AudioClip
        clip = AudioClip.Create("DecodedAudio", sampleCount / channels, channels, sampleRate, false);
        clip.SetData(floatBuffer, 0);

        // ����
        audioSource.clip = clip;
        audioSource.Play();
    }
}
