using BTD_Mod_Helper.Api.Components;
using BTD_Mod_Helper.Api.Enums;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KnowledgeDumper;

// Progress window in the game's own style (Mod Helper panels, game font), on its own overlay canvas above every
// game canvas, kept across scene loads so it stays up while the dump hops between the menu and matches.
static class Overlay
{
    const float BarWidth = 900;
    static GameObject root;
    static ModHelperText text;
    static RectTransform fill;
    public static bool Failed;  // building the window threw: Mod.OnGUI draws the plain box instead

    public static void Show(string status, float progress)
    {
        if (Failed) return;
        if (status == null)
        {
            if (root != null) root.SetActive(false);
            return;  // built on first use only: at game start Mod Helper's sprites and font are not loaded yet
        }
        try
        {
            if (root == null) Build();
            root.SetActive(true);
            text.SetText(status);
            fill.gameObject.SetActive(progress >= 0);
            fill.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(progress), fill.sizeDelta.y);
        }
        catch (System.Exception e)
        {
            Failed = true;
            if (root != null) Object.Destroy(root);
            root = null;
            BTD_Mod_Helper.ModHelper.Error<Mod>($"overlay: {e}");  // full trace: which call threw
        }
    }

    static void Build()
    {
        root = new GameObject("KnowledgeDumperOverlay");
        Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;  // above every game canvas, like the co-op reconnect window
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = ModHelperPanel.Create(new Info("Panel", 0, 0, 1000, 300), VanillaSprites.MainBGPanelBlue);
        panel.transform.SetParent(root.transform, false);
        panel.AddText(new Info("Title", 0, 85, 950, 70), "Knowledge Dumper", 60);
        text = panel.AddText(new Info("Status", 0, 0, 950, 80), "", 36);
        text.Text.enableAutoSizing = true;
        text.Text.fontSizeMax = 36;
        text.Text.fontSizeMin = 18;
        var bar = panel.AddPanel(new Info("Bar", 0, -90, BarWidth + 20, 54), VanillaSprites.BlueInsertPanelRound);
        var f = bar.AddImage(new Info("Fill", 10, 0, 0, 34) { Pivot = new Vector2(0, 0.5f), AnchorMin = new Vector2(0, 0.5f), AnchorMax = new Vector2(0, 0.5f) },
            VanillaSprites.GreenBtnLong);
        fill = f.RectTransform;
        // 9-slice the game sprites: stretched whole, their round corners turned the panel into an ellipse
        Slice(panel.gameObject, 2.5f);
        Slice(bar.gameObject, 2.5f);
        Slice(f.gameObject, 3f);
    }

    static void Slice(GameObject go, float corner)
    {
        var image = go.GetComponent<Image>();
        if (image == null) return;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = corner;  // bigger: smaller corners
    }
}
