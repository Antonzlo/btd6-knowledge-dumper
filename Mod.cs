using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api.Helpers;
using BTD_Mod_Helper.Extensions;
using Il2CppAssets.Scripts.Models;
using Il2CppAssets.Scripts.Models.Bloons;
using Il2CppAssets.Scripts.Models.Towers;
using Il2CppAssets.Scripts.Simulation;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
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
// Ctrl+Shift+F6 from the main menu does it all: opens Monkey Knowledge, dumps, starts a sandbox match, dumps the XP data.
public class Mod : BloonsTD6Mod
{
    static string lastMenu;

    public override void OnUpdate()
    {
        var menu = Menus != null ? Menus.GetCurrentMenuName() : null;
        if (menu != lastMenu) ModHelper.Msg<Mod>($"menu: {lastMenu = menu}");
        var combo = Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift);
        if (Input.GetKeyDown(KeyCode.F6)) { if (combo) MelonCoroutines.Start(Auto()); else Dump(); }
        if (Input.GetKeyDown(KeyCode.F7)) { if (combo) WhereAmI(); else MelonCoroutines.Start(Icons.Dump()); }
    }

    static bool KnowledgeOpen()
    {
        foreach (var b in UnityEngine.Resources.FindObjectsOfTypeAll<KnowledgeSkillBtn>())
            if (b.gameObject.scene.IsValid() && !string.IsNullOrEmpty(b.knowledgeID)) return true;
        return false;
    }

    static bool InMatch() => InGame.instance != null && InGame.instance.bridge != null && InGame.instance.bridge.simulation != null;

    static Il2CppAssets.Scripts.Unity.Menu.MenuManager Menus => Il2CppAssets.Scripts.Unity.Menu.MenuManager.instance;

    // every step names the current menu, so a stuck auto dump shows where it stopped
    static void Log(string step) =>
        ModHelper.Msg<Mod>($"{step} (menu: {(Menus != null ? Menus.GetCurrentMenuName() : "none")}, in match: {InMatch()})");

    // Ctrl+Shift+F7: scene, open menus with the data each was opened with, and the picked map/mode,
    // to learn the ids a scripted step needs
    static void WhereAmI()
    {
        if (Menus == null) { ModHelper.Msg<Mod>("where: no MenuManager yet"); return; }
        ModHelper.Msg<Mod>($"where: scene {Menus.CurrentScene}, menu {Menus.GetCurrentMenuName()}, previous {Menus.GetPreviousMenuName()}, in match {InMatch()}");
        foreach (var entry in Menus.GetMenuStack())
        {
            var data = entry.Item2;
            ModHelper.Msg<Mod>($"  stack: {entry.Item1}, data {(data == null ? "null" : data.GetIl2CppType().FullName + " = " + data.ToString())}");
        }
        var d = InGameData.Editable;
        if (d != null) ModHelper.Msg<Mod>($"  picked: map {d.selectedMap}, mode {d.selectedMode}, difficulty {d.selectedDifficulty}");
    }

    static IEnumerator Auto()
    {
        Log("auto dump start");
        Menus.OpenMenu("KnowledgeMainUI", null, false);
        // KnowledgeMain is the category picker; OpenPage is what a category button calls to show the tree
        KnowledgeMain main = null;
        for (var end = Time.realtimeSinceStartup + 15; Time.realtimeSinceStartup < end && main == null;)
        {
            foreach (var m in UnityEngine.Resources.FindObjectsOfTypeAll<KnowledgeMain>())
                if (m.gameObject.scene.IsValid() && m.gameObject.activeInHierarchy) main = m;
            if (main == null) yield return null;
        }
        if (main == null)
        {
            ModHelper.Msg<Mod>("Monkey Knowledge menu did not open.");
            yield break;
        }
        Log("knowledge picker open");
        main.OpenPage(0);
        for (var end = Time.realtimeSinceStartup + 15; Time.realtimeSinceStartup < end && !KnowledgeOpen();) yield return null;
        Log(KnowledgeOpen() ? "knowledge tree open" : "knowledge tree did not open");
        yield return new WaitForSecondsRealtime(1);  // let the tree lay itself out before reading positions
        Dump();
        // a match must start from the main menu: loading it over the knowledge tree leaves an empty blurred screen
        Menus.CloseAllMenus();
        for (var end = Time.realtimeSinceStartup + 15; Time.realtimeSinceStartup < end && Menus.IsTransitioning;) yield return null;
        yield return new WaitForSecondsRealtime(1);
        Log("closed menus");
        // sandbox: a new standard game here could replace a saved game on this map
        var data = InGameData.Editable;
        data.selectedMode = "Sandbox";
        data.selectedMap = "Tutorial";  // Monkey Meadow
        data.selectedDifficulty = "Easy";
        Il2CppAssets.Scripts.Unity.UI_New.UI.instance.LoadGame(null, null, null, false, false);
        Log("match loading");
        for (var end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end && !InMatch();) yield return null;
        yield return new WaitForSecondsRealtime(1);
        Log("match wait over");
        if (InMatch()) DumpInGame(Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout"));
        else ModHelper.Msg<Mod>("Match did not start in 60 s, press F6 inside a match for the XP data.");
    }

    static float[] Rect(RectTransform rt)
    {
        // world-space centre and size; the wiki normalises per knowledge set
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(corners);
        var c = (corners[0] + corners[2]) / 2;
        return new[] { c.x, c.y, corners[2].x - corners[0].x, corners[2].y - corners[0].y };
    }

    // Facts the Mod Helper export lacks: game version, shop order of towers, bloon property bits, tower set bits,
    // starting cash.
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
        var sets = new Dictionary<string, int>();
        var setType = typeof(TowerModel).GetProperty("towerSet").PropertyType;
        foreach (var v in System.Enum.GetValues(setType))
            sets[v.ToString()] = System.Convert.ToInt32(v);
        File.WriteAllText(Path.Combine(dir, "game.json"), JsonSerializer.Serialize(
            new
            {
                version = Application.version, towerSet = towers, heroSet = heroes, heroScreen = screen, bloonProperties = props,
                towerSets = sets, startCash = Game.instance.model.cash
            },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped {towers.Count} towers, version {Application.version}");
    }

    // Only inside a match (GetXpForRound fails in the menu): hero XP per round and the XP scale of each map difficulty.
    static void DumpInGame(string dir)
    {
        var sim = InGame.instance != null && InGame.instance.bridge != null ? InGame.instance.bridge.simulation : null;
        if (sim == null) return;
        var heroXp = new List<float>();
        for (var r = 0; r <= 140; r++)  // index = round number, 0 included to see the base
            heroXp.Add(Simulation.GetXpForRound(r, sim.model));
        // the static array is indexed by the MapDifficulty enum
        var scales = new Dictionary<string, float>();
        var table = Simulation.trackDifficultyXpScale;
        foreach (var v in System.Enum.GetValues(typeof(Il2CppAssets.Scripts.Data.MapSets.MapDifficulty)))
        {
            var i = System.Convert.ToInt32(v);
            if (table != null && i >= 0 && i < table.Length) scales[v.ToString()] = table[i];
        }
        File.WriteAllText(Path.Combine(dir, "xp.json"), JsonSerializer.Serialize(
            new { heroXpPerRound = heroXp, trackDifficultyXpScale = scales },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped hero XP for 140 rounds and {scales.Count} map XP scales");
    }

    static void Dump()
    {
        var dir = Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout");
        Directory.CreateDirectory(Path.Combine(dir, "icons"));
        DumpGame(dir);
        DumpInGame(dir);

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
