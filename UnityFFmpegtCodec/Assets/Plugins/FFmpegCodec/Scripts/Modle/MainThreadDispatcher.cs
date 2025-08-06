using System;
using System.Collections.Concurrent;
using UnityEngine;

public class MainThreadDispatcher : MonoBehaviour
{
    private static readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();
    private static MainThreadDispatcher _instance;

    public static void Enqueue(Action action)
    {
        if (action == null)
            return;

        _executionQueue.Enqueue(action);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        while (_executionQueue.TryDequeue(out var action))
        {
            action?.Invoke();
        }
    }

    private void OnDestroy()
    {
        FFmpegMgr.Instance().DisposeDecoderAll();
    }
}
