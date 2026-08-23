using UnityEngine;
using System.Collections;

// ─────────────────────────────────────────────────────────────────
//  This script manages the registration of a RoadTile with the
//  GameManager, registering it once the manager becomes available
//  and unregistering it when the tile is destroyed.
// ─────────────────────────────────────────────────────────────────

[RequireComponent(typeof(RoadTile))]
public class RoadTileAutoRegister : MonoBehaviour
{
    private RoadTile _tile;

    private void Awake()
    {
        _tile = GetComponent<RoadTile>();
    }

    private void Start()
    {
        StartCoroutine(RegisterWhenReady());
    }

    private IEnumerator RegisterWhenReady()
    {
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.RegisterTile(_tile);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterTile(_tile);
    }
}