using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ═════════════════════════════════════════════════════════════════
//  SIMPLE LINE CHART  — pure UGUI Graphic, zero dependencies
// ═════════════════════════════════════════════════════════════════

[RequireComponent(typeof(CanvasRenderer))]
public class SimpleLineChart : Graphic
{
    // ── Line & Dot ───────────────────────────────────────────────
    [Header("Line Style")]
    public Color lineColor = new Color(0.2f, 0.6f, 1f, 1f);
    public float lineWidth = 2f;
    public float dotRadius = 4f;

    // ── Grid ─────────────────────────────────────────────────────
    [Header("Grid")]
    public Color gridColor = new Color(1.0f, 1.0f, 1.0f, 0.5f);
    public int horizontalTicks = 5;
    public int verticalTicks = 0;

    // ── Axes ─────────────────────────────────────────────────────
    [Header("Axes")]
    public Color axisColor = new Color(0.4f, 0.4f, 0.4f, 0.8f);
    public float axisWidth = 1.5f;

    // ── Padding (left, right, top, bottom) ───────────────────────
    [Header("Padding")]
    public Vector4 padding = new Vector4(50, 20, 20, 40);

    // ── Labels ───────────────────────────────────────────────────
    [Header("Axis Labels")]
    public GameObject axisLabelPrefab;
    public RectTransform axisLabelParent;

    [Header("Tick Label Style")]
    public float tickFontSize = 30f;
    public Color tickColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    // ── Internal ─────────────────────────────────────────────────
    private List<Vector2> _data = new List<Vector2>();
    private List<GameObject> _spawnedLabels = new List<GameObject>();

    // ─────────────────────────────────────────────────────────────
    //  Override the Graphic base color 
    // ─────────────────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        // Force the inherited Graphic.color to white 
        color = Color.white;
    }

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API
    // ─────────────────────────────────────────────────────────────

    public void SetData(List<Vector2> points)
    {
        _data = points ?? new List<Vector2>();
        SetVerticesDirty();
        RebuildLabels();
    }

    // Lets Inspector changes (grid color, line color etc.) reflect live
    protected void OnValidate()
    {
        color = Color.white; // keep base color white always
        SetVerticesDirty();
        if (Application.isPlaying)
            RebuildLabels();
    }

    // ─────────────────────────────────────────────────────────────
    //  MESH
    // ─────────────────────────────────────────────────────────────

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_data == null || _data.Count < 2) return;

        Rect r = rectTransform.rect;
        float left = r.xMin + padding.x;
        float right = r.xMax - padding.y;
        float top = r.yMax - padding.z;
        float bottom = r.yMin + padding.w;
        float drawW = right - left;
        float drawH = top - bottom;

        // Data range
        float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in _data)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }
        if (maxX <= minX) maxX = minX + 1;
        if (maxY <= 0) maxY = 1;

        // Map to local rect coords
        var pts = new List<Vector2>(_data.Count);
        foreach (var p in _data)
        {
            float nx = (p.x - minX) / (maxX - minX);
            float ny = p.y / maxY;
            pts.Add(new Vector2(left + nx * drawW, bottom + ny * drawH));
        }

        // Horizontal grid lines
        for (int i = 0; i <= horizontalTicks; i++)
        {
            float y = bottom + drawH * i / horizontalTicks;
            DrawRect(vh, new Vector2(left, y), new Vector2(right, y), 1f, gridColor);
        }

        // Vertical grid lines
        for (int i = 0; i <= verticalTicks; i++)
        {
            float x = left + drawW * i / verticalTicks;
            DrawRect(vh, new Vector2(x, bottom), new Vector2(x, top), 1f, gridColor);
        }

        // Axes
        DrawRect(vh, new Vector2(left, bottom), new Vector2(right, bottom), axisWidth, axisColor);
        DrawRect(vh, new Vector2(left, bottom), new Vector2(left, top), axisWidth, axisColor);

        // Data line
        for (int i = 0; i < pts.Count - 1; i++)
            DrawRect(vh, pts[i], pts[i + 1], lineWidth, lineColor);

        // Dots
        foreach (var s in pts)
            DrawDot(vh, s, dotRadius, lineColor);
    }

    // ─────────────────────────────────────────────────────────────
    //  LABELS  (tick numbers only — no axis titles)
    // ─────────────────────────────────────────────────────────────

    private void RebuildLabels()
    {
        foreach (var go in _spawnedLabels) { if (go != null) Destroy(go); }
        _spawnedLabels.Clear();

        if (axisLabelPrefab == null || axisLabelParent == null || _data == null || _data.Count == 0)
            return;

        StartCoroutine(SpawnLabelsNextFrame());
    }

    private IEnumerator SpawnLabelsNextFrame()
    {
        yield return new WaitForEndOfFrame();

        // Snapshot style values at spawn time so they match current Inspector values
        float snappedTickSize = tickFontSize;
        Color snappedTickColor = tickColor;

        float maxY = 0;
        int maxDay = 0;
        foreach (var p in _data)
        {
            if (p.y > maxY) maxY = p.y;
            if ((int)p.x > maxDay) maxDay = (int)p.x;
        }

        // World-space draw area
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        // corners: 0=BL, 1=TL, 2=TR, 3=BR
        float left = corners[0].x + padding.x;
        float right = corners[3].x - padding.y;
        float top = corners[1].y - padding.z;
        float bottom = corners[0].y + padding.w;
        float drawW = right - left;
        float drawH = top - bottom;

        // X tick numbers
        int interval = PickInterval(maxDay, 4, 10, new[] { 1, 2, 5, 10, 20, 30 });
        int axisMaxX = Mathf.CeilToInt((float)maxDay / interval) * interval;
        for (int d = 0; d <= axisMaxX; d += interval)
        {
            float xNorm = axisMaxX > 0 ? (float)d / axisMaxX : 0f;
            float xPos = left + xNorm * drawW;
            SpawnLabel(d.ToString(), new Vector2(xPos, bottom - 18f),
                snappedTickSize, snappedTickColor, TextAlignmentOptions.Center);
        }

        // Y tick numbers
        for (int i = 0; i <= horizontalTicks; i++)
        {
            float val = maxY * i / horizontalTicks;
            float yPos = bottom + drawH * i / horizontalTicks;
            SpawnLabel(Mathf.RoundToInt(val).ToString(),
                new Vector2(left - 75f, yPos),
                snappedTickSize, snappedTickColor, TextAlignmentOptions.Right);
        }
    }

    private void SpawnLabel(string text, Vector2 worldPos, float fontSize, Color col,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        GameObject go = Instantiate(axisLabelPrefab, axisLabelParent);
        var tmp = go.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = col;
            tmp.alignment = align;
        }
        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.position = new Vector3(worldPos.x, worldPos.y, 0f);
            rt.sizeDelta = new Vector2(100f, 50f);
        }
        _spawnedLabels.Add(go);
    }

    private static int PickInterval(int maxVal, int minTicks, int maxTicks, int[] candidates)
    {
        foreach (int iv in candidates)
        {
            int count = Mathf.CeilToInt((float)maxVal / iv) + 1;
            if (count >= minTicks && count <= maxTicks) return iv;
        }
        return candidates[candidates.Length - 1];
    }

    // ─────────────────────────────────────────────────────────────
    //  DRAW HELPERS
    // ─────────────────────────────────────────────────────────────

    private void DrawRect(VertexHelper vh, Vector2 a, Vector2 b, float width, Color col)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x) * (width * 0.5f);
        int idx = vh.currentVertCount;
        AddVert(vh, a - perp, col);
        AddVert(vh, a + perp, col);
        AddVert(vh, b + perp, col);
        AddVert(vh, b - perp, col);
        vh.AddTriangle(idx, idx + 1, idx + 2);
        vh.AddTriangle(idx, idx + 2, idx + 3);
    }

    private void DrawDot(VertexHelper vh, Vector2 center, float radius, Color col)
    {
        const int segments = 12;
        int startIdx = vh.currentVertCount;
        AddVert(vh, center, col);
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            AddVert(vh, center + offset, col);
        }
        for (int i = 0; i < segments; i++)
            vh.AddTriangle(startIdx, startIdx + i + 1, startIdx + i + 2);
    }

    private static void AddVert(VertexHelper vh, Vector2 pos, Color col)
    {
        var v = UIVertex.simpleVert;
        v.position = new Vector3(pos.x, pos.y, 0);
        v.color = col;
        vh.AddVert(v);
    }
}