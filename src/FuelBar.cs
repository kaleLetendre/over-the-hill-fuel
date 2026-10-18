using System;
using System.IO;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OTH.Fuel;

// HUD fuel bar: a clone of the game's engine-flooding bar (EngineFloodingUIPanel.
// floodFill, plus its icon if the icon sits next to it), placed on the right just
// above the gear display and at 60% of the flooding bar's size (its rect size,
// in HUD canvas units). The clone lives
// under the HUD's driving-mode cluster, so it hides with the HUD.
//
// Full bar = full tank. Green normally, red below LowFuelThreshold, over a
// faint translucent square showing the full size. The engine icon is replaced
// by the embedded jerry-can PNG, drawn on top of the fill.
internal static class FuelBar
{
    private const float Scale = 0.6f;

    private const float PlaceInterval = 1f;

    internal static readonly Color Normal = new(0.2f, 0.75f, 0.3f, 1f);

    // Faint square behind the fill, so the full size is visible when low.
    internal static readonly Color Track = new(1f, 1f, 1f, 0.25f);

    internal static readonly Color Low = new(0.9f, 0.15f, 0.1f, 1f);

    private static GameObject root;

    private static RectTransform rootRect;

    private static RectTransform gearRect;

    private static ContextUIFill fill;

    private static Image fillImage;

    private static Sprite icon;

    private static float nextPlace;

    private static float lastFrac = -1f;

    private static bool lastLow;

    private static bool broken;

    private static readonly Il2CppStructArray<Vector3> corners = new(4);

    internal static void Build(HUDManager hud)
    {
        if (hud == null)
        {
            return;
        }
        if (root != null)
        {
            broken = false;
            return;
        }
        try
        {
            Create(hud);
            broken = false;
        }
        catch (Exception ex)
        {
            Mod.Log.Warning("Fuel bar setup failed: " + ex);
            if (root != null)
            {
                Object.Destroy(root);
            }
            root = null;
        }
    }

    private static void Create(HUDManager hud)
    {
        EngineFloodingUIPanel panel = hud.engineFloodingUIPanel;
        ContextUIFill src = panel != null ? panel.floodFill : null;
        ContextUIDrivingModeSelector selector = hud.drivingModeSelector;
        GameObject gear = selector != null ? selector.GearDisplay : null;
        if (src == null || gear == null)
        {
            Mod.Log.Warning($"Fuel bar: HUD parts missing (flood fill {src != null}, gear display {gear != null})");
            return;
        }

        // Clone the flood bar's own group (icon + bar) when the fill sits inside
        // floodObject and that isn't the tracked panel root; otherwise just the fill.
        GameObject template = src.gameObject;
        GameObject floodObject = panel.floodObject;
        if (floodObject != null && floodObject.Pointer != panel.gameObject.Pointer && src.transform.IsChildOf(floodObject.transform))
        {
            template = floodObject;
        }
        Transform parent = selector.transform;

        // Clone under an inactive staging object, so the copy's components
        // (flood panel logic, world tracking) never Awake before we strip them.
        GameObject staging = new("OTH.Fuel.Staging");
        staging.SetActive(false);
        staging.transform.SetParent(parent, false);
        root = Object.Instantiate(template, staging.transform, false);
        root.name = "OTH.Fuel.Bar";
        Strip(root);
        root.SetActive(true);
        root.transform.SetParent(parent, false);
        Object.Destroy(staging);
        foreach (CanvasGroup cg in root.GetComponentsInChildren<CanvasGroup>(true))
        {
            cg.alpha = 1f;
        }
        rootRect = root.GetComponent<RectTransform>();
        gearRect = gear.GetComponent<RectTransform>();
        if (rootRect == null || gearRect == null)
        {
            throw new InvalidOperationException("bar or gear display has no RectTransform");
        }

        // Keep any layout group on the cluster from moving or resizing the bar.
        LayoutElement le = root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        // Size: the template's own rect (local units, unaffected by the flood
        // panel's scale tweens), fixed anchors so the new parent can't stretch
        // it, then 60% in HUD canvas units whatever scale the cluster has.
        Vector2 size = template.GetComponent<RectTransform>().rect.size;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(1f, 0f);
        rootRect.sizeDelta = size;
        Canvas canvas = selector.GetComponentInParent<Canvas>();
        float canvasScale = canvas != null ? canvas.rootCanvas.transform.lossyScale.x : 0f;
        float parentScale = parent.lossyScale.x;
        float k = canvasScale > 0f && parentScale > 0f ? Scale * canvasScale / parentScale : Scale;
        root.transform.localScale = new Vector3(k, k, 1f);
        root.transform.SetAsLastSibling();

        fill = root.GetComponentInChildren<ContextUIFill>(true);
        fillImage = fill != null ? fill.fillImage : null;
        if (fillImage == null)
        {
            throw new InvalidOperationException("cloned bar has no fill image");
        }
        fill.gameObject.SetActive(true);
        fillImage.gameObject.SetActive(true);
        // Plain quads instead of the flood panel's engine-shaped sprites: a
        // faint square for the whole tank, and the fill (filled from the
        // bottom) on top of it. The icon goes above both (SetIcon).
        // Unity ignores Filled mode on an Image without a sprite, so the fill
        // gets a plain white one.
        fillImage.sprite = WhiteSprite();
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Vertical;
        fillImage.fillOrigin = 0;
        if (fill.backgroundImage != null)
        {
            fill.backgroundImage.sprite = null;
            fill.backgroundImage.color = Track;
        }

        SetIcon(root, fill);
        lastFrac = -1f;
        nextPlace = 0f;
        Place();
        Mod.Log.Msg($"Fuel bar added (cloned '{template.name}', size {Mod.F(size.x)}x{Mod.F(size.y)}, scale {Mod.F(k)}, fill {fillImage.type}/{fillImage.fillMethod} origin {fillImage.fillOrigin})");
    }

    // Remove the flood panel logic, world tracking and localization from the clone.
    private static void Strip(GameObject go)
    {
        foreach (Component c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null)
            {
                continue;
            }
            string type = c.GetIl2CppType().Name;
            if (type == "EngineFloodingUIPanel" || type == "WorldTrackingRectTransform" || type.Contains("Locali"))
            {
                Object.DestroyImmediate(c);
            }
        }
    }

    // Use the clone's own icon image if it has one (any image that isn't the
    // bar's background or fill); otherwise add an icon left of the bar.
    private static void SetIcon(GameObject go, ContextUIFill bar)
    {
        Sprite sprite = LoadIcon();
        if (sprite == null)
        {
            return;
        }
        IntPtr bg = bar.backgroundImage != null ? bar.backgroundImage.Pointer : IntPtr.Zero;
        IntPtr fg = bar.fillImage != null ? bar.fillImage.Pointer : IntPtr.Zero;
        Image found = null;
        foreach (Image img in go.GetComponentsInChildren<Image>(true))
        {
            if (img.Pointer == bg || img.Pointer == fg || img.sprite == null)
            {
                continue;
            }
            // Prefer something named like an icon; else take the first candidate.
            if (found == null || img.name.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = img;
            }
        }
        if (found == null)
        {
            RectTransform barRect = bar.GetComponent<RectTransform>();
            float h = barRect != null && barRect.rect.height > 0f ? barRect.rect.height * 1.6f : 32f;
            GameObject iconGo = new("FuelIcon");
            iconGo.transform.SetParent(bar.transform, false);
            found = iconGo.AddComponent<Image>();
            RectTransform rt = iconGo.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-h * 0.2f, 0f);
            rt.sizeDelta = new Vector2(h, h);
        }
        found.gameObject.SetActive(true);
        // Draw the icon over the fill.
        found.transform.SetAsLastSibling();
        found.sprite = sprite;
        found.preserveAspect = true;
        found.color = Color.white;
    }

    private static Sprite white;

    // Unity ignores Filled mode on an Image without a sprite.
    internal static Sprite WhiteSprite()
    {
        if (white == null)
        {
            white = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            white.hideFlags = HideFlags.HideAndDontSave;
        }
        return white;
    }

    // Also used for the jerry can in the tools panel.
    internal static Sprite LoadIcon()
    {
        if (icon != null)
        {
            return icon;
        }
        using Stream s = typeof(FuelBar).Assembly.GetManifestResourceStream("OTH.Fuel.fuel-icon.png");
        if (s == null)
        {
            Mod.Log.Warning("Fuel icon resource missing");
            return null;
        }
        using MemoryStream ms = new();
        s.CopyTo(ms);
        byte[] bytes = ms.ToArray();
        Texture2D tex = new(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(tex, bytes))
        {
            Mod.Log.Warning("Fuel icon PNG could not be decoded");
            return null;
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        icon = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        icon.hideFlags = HideFlags.HideAndDontSave;
        return icon;
    }

    // Bottom-right corner of the bar sits just above the gear display's
    // top-right corner. Repeated now and then, since the HUD may lay out or
    // change resolution after the bar is built.
    private static void Place()
    {
        if (rootRect == null || gearRect == null)
        {
            return;
        }
        gearRect.GetWorldCorners(corners);
        Vector3 topRight = corners[2];
        float gap = (corners[2].y - corners[3].y) * 0.15f;
        rootRect.position = new Vector3(topRight.x, topRight.y + gap, rootRect.position.z);
    }

    // Called at 10 Hz from Mod.OnUpdate.
    internal static void Tick(Tank tank)
    {
        if (root == null || broken)
        {
            return;
        }
        try
        {
            bool show = Mod.Enabled && tank != null && tank.Capacity > 0f;
            if (root.activeSelf != show)
            {
                root.SetActive(show);
            }
            if (!show)
            {
                return;
            }
            float now = Time.unscaledTime;
            if (now >= nextPlace)
            {
                nextPlace = now + PlaceInterval;
                Place();
            }
            float frac = Mathf.Clamp01(tank.Fuel / tank.Capacity);
            if (Mathf.Abs(frac - lastFrac) > 0.001f)
            {
                lastFrac = frac;
                // A Filled image is set directly, so full always means a full bar,
                // whatever direction ContextUIFill.SetValue uses for flooding.
                if (fillImage.type == Image.Type.Filled)
                {
                    fillImage.fillAmount = frac;
                }
                else
                {
                    fill.SetValue(frac, 1f);
                }
            }
            bool low = frac < Mod.LowFuelThreshold;
            if (low != lastLow || fillImage.color != (low ? Low : Normal))
            {
                lastLow = low;
                fillImage.color = low ? Low : Normal;
            }
        }
        catch (Exception ex)
        {
            broken = true;
            Mod.Log.Warning("Fuel bar update failed; bar disabled until the next car spawn: " + ex.Message);
        }
    }
}
