using UnityEngine;

// ─────────────────────────────────────────────────────────────────
//  RoadTileAutoRegister
//  Attach to: the same GameObject as RoadTile (auto-added by
//  RoadTileGenerator — add it to the generator's tile setup).
//
//  Registers / unregisters this tile with GameManager automatically.
//  Keeps RoadTile clean (no GameManager dependency inside RoadTile).
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
        if (GameManager.Instance != null)
            GameManager.Instance.RegisterTile(_tile);
        else
            Debug.LogWarning($"[RoadTileAutoRegister] {_tile.tileID}: " +
                             "GameManager not found. Is it in the scene?");
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.UnregisterTile(_tile);
    }
}
