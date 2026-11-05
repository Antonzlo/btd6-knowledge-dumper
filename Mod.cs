using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api.Helpers;
using BTD_Mod_Helper.Extensions;
using Il2CppAssets.Scripts.Models.Bloons;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.UI_New.Knowledge;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(KnowledgeDumper.Mod), "Knowledge Dumper", "0.1.0", "antonzlo")]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]

namespace KnowledgeDumper;

// Open the Monkey Knowledge screen, press F6: writes the tree layout (button and arrow positions)
// and every knowledge icon to <game>/KnowledgeLayout/ for the tech wiki.
public class Mod : BloonsTD6Mod
{
    public override void OnUpdate()
    {
        if (Input.GetKeyDown(KeyCode.F6)) Dump();
        if (Input.GetKeyDown(KeyCode.F7)) MelonCoroutines.Start(Icons.Dump());
    }

    static float[] Rect(RectTransform rt)
    {
        // world-space centre and size; the wiki normalises per knowledge set
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(corners);
        var c = (corners[0] + corners[2]) / 2;
        return new[] { c.x, c.y, corners[2].x - corners[0].x, corners[2].y - corners[0].y };
    }

    // Facts the Mod Helper export lacks: game version, shop order of towers, bloon property bits.
    static void DumpGame(string dir)
    {
        var towers = new List<object>();
        foreach (var t in Game.instance.model.towerSet)
            towers.Add(new { id = t.towerId, index = t.towerIndex });
        var heroes = new List<object>();
        foreach (var h in Game.instance.model.heroSet)
            heroes.Add(new { id = h.towerId, index = h.towerIndex });
        // order of the buttons on the hero select screen (only when that screen is open), as shown left to right, top to bottom
        var screen = new List<string>();
        foreach (var s in UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppAssets.Scripts.Unity.UI_New.Main.HeroSelect.HeroUpgradeDetails>())
            if (s.gameObject.scene.IsValid() && s.heroButtons != null && s.heroButtons.Count > 0)
            {
                var buttons = new List<Il2CppAssets.Scripts.Unity.UI_New.Main.HeroSelect.HeroButton>();
                foreach (var b in s.heroButtons) if (b != null && b.heroModel != null) buttons.Add(b);
                buttons.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
                foreach (var b in buttons) screen.Add(b.heroModel.towerId);
                break;
            }
        var props = new Dictionary<string, int>();
        // the enum's namespace differs between game versions; take its type from the model field
        var propType = typeof(BloonModel).GetProperty("bloonProperties").PropertyType;
        foreach (var v in System.Enum.GetValues(propType))
            props[v.ToString()] = System.Convert.ToInt32(v);
        File.WriteAllText(Path.Combine(dir, "game.json"), JsonSerializer.Serialize(
            new { version = Application.version, towerSet = towers, heroSet = heroes, heroScreen = screen, bloonProperties = props },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped {towers.Count} towers, version {Application.version}");
    }

    static void Dump()
    {
        var dir = Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout");
        Directory.CreateDirectory(Path.Combine(dir, "icons"));
        DumpGame(dir);

        var buttons = new List<KnowledgeSkillBtn>();
        foreach (var b in UnityEngine.Resources.FindObjectsOfTypeAll<KnowledgeSkillBtn>())
            if (b.gameObject.scene.IsValid() && !string.IsNullOrEmpty(b.knowledgeID))
                buttons.Add(b);
        if (buttons.Count == 0)
        {
            ModHelper.Msg<Mod>("No knowledge buttons found. Open the Monkey Knowledge screen first.");
            return;
        }

        var items = new List<object>();
        foreach (var b in buttons)
        {
            var arrows = new List<object>();
            if (b.arrows != null)
                foreach (var a in b.arrows)
                {
                    if (a == null) continue;
                    var rt = a.rectTransform;
                    arrows.Add(new
                    {
                        rect = Rect(rt),
                        // size before rotation, so a rotated arrow keeps its real length
                        w = rt.rect.width * rt.lossyScale.x,
                        h = rt.rect.height * rt.lossyScale.y,
                        rot = rt.eulerAngles.z,
                        sx = Mathf.Sign(rt.lossyScale.x),
                        sy = Mathf.Sign(rt.lossyScale.y),
                        sprite = a.sprite != null ? a.sprite.name : null
                    });
                }

            var icon = b.icon != null && b.icon.sprite != null ? b.icon.sprite : null;
            var iconPath = Path.Combine(dir, "icons", b.knowledgeID + ".png");
            if (icon != null && !File.Exists(iconPath)) icon.TrySaveToPNG(iconPath);

            items.Add(new
            {
                id = b.knowledgeID,
                set = b.knowledgeSet,
                tier = b.tierInvestmentRequired,
                rect = Rect(b.GetComponent<RectTransform>()),
                icon = icon != null ? icon.name : null,
                arrows
            });
        }

        File.WriteAllText(Path.Combine(dir, "layout.json"),
            JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped {items.Count} knowledge buttons to {dir}");
    }
}
