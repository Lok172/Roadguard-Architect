using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  COROUTINE RUNNER
//
//  A persistent MonoBehaviour that lets static classes (like
//  LevelProgress) fire coroutines without being a MonoBehaviour
//  themselves.
//
//  Place on a DontDestroyOnLoad GameObject in the Main / Boot scene.
// ─────────────────────────────────────────────────────────────────

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
