using System.Collections;
using UnityEngine;

// This script is used to run coroutines on behalf of non-MonoBehaviour classes,
// via a single persistent instance that is kept alive across scenes.

public class CoroutineRunner : MonoBehaviour
{
    public static CoroutineRunner Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Run(IEnumerator coroutine) => StartCoroutine(coroutine);
}
