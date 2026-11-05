using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Api.Helpers;
using BTD_Mod_Helper.Extensions;
using Il2CppAssets.Scripts.Data;
using Il2CppAssets.Scripts.Models;
using Il2CppAssets.Scripts.Models.Bloons;
using Il2CppAssets.Scripts.Models.Map;
using Il2CppAssets.Scripts.Models.Towers;
using Il2CppAssets.Scripts.Simulation;
using Il2CppAssets.Scripts.Unity.UI_New.InGame;
using Il2CppAssets.Scripts.Unity;
using Il2CppAssets.Scripts.Unity.UI_New.Knowledge;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

[assembly: MelonInfo(typeof(KnowledgeDumper.Mod), KnowledgeDumper.ModHelperData.Name, KnowledgeDumper.ModHelperData.Version, KnowledgeDumper.ModHelperData.Author)]
[assembly: MelonGame("Ninja Kiwi", "BloonsTD6")]

namespace KnowledgeDumper;

// Open the Monkey Knowledge screen, press F6: writes the tree layout (button and arrow positions)
// and every knowledge icon to <game>/KnowledgeLayout/ for the tech wiki.
// Ctrl+Shift+F6 from the main menu does it all: Monkey Knowledge tree, hero screen, a sandbox match for the XP data,
// then back to the main menu, with the progress shown on screen.
public class Mod : BloonsTD6Mod
{
    static string lastMenu;
    static string status;  // auto dump progress, drawn on screen while set

    public override void OnGUI()
    {
        if (status == null) return;
        GUI.skin.box.fontSize = 22;
        GUI.Box(new Rect(Screen.width / 2f - 300, 24, 600, 44), status);
    }

    static void Step(int n, string text)
    {
        status = $"Knowledge Dumper {n}/5: {text}";
        Log(status);
    }

    static IEnumerator Finish(string text)
    {
        status = text;
        Log(text);
        yield return new WaitForSecondsRealtime(4);
        status = null;
    }

    public override void OnUpdate()
    {
        var menu = Menus != null ? Menus.GetCurrentMenuName() : null;
        if (menu != lastMenu) ModHelper.Msg<Mod>($"menu: {lastMenu = menu}");
        var combo = Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift);
        if (Input.GetKeyDown(KeyCode.F6)) { if (combo) MelonCoroutines.Start(Auto()); else Dump(); }
        if (Input.GetKeyDown(KeyCode.F7)) { if (combo) WhereAmI(); else MelonCoroutines.Start(Icons.Dump()); }
        if (combo && Input.GetKeyDown(KeyCode.F9))
        {
            if (mapsRunning) mapsStop = true;
            else MelonCoroutines.Start(AutoMaps());
        }
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
        Step(1, "opening Monkey Knowledge");
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
            yield return Finish("Knowledge Dumper: Monkey Knowledge menu did not open");
            yield break;
        }
        Log("knowledge picker open");
        main.OpenPage(0);
        for (var end = Time.realtimeSinceStartup + 15; Time.realtimeSinceStartup < end && !KnowledgeOpen();) yield return null;
        Step(2, KnowledgeOpen() ? "dumping the knowledge tree" : "knowledge tree did not open");
        yield return new WaitForSecondsRealtime(1);  // let the tree lay itself out before reading positions
        Dump();
        // hero select screen: game.json then gets the order of its hero buttons
        Step(3, "hero screen");
        yield return ToMainMenu();
        Menus.OpenMenu("HeroDetailsUI", null, false);
        for (var end = Time.realtimeSinceStartup + 15; Time.realtimeSinceStartup < end && HeroScreen().Count == 0;) yield return null;
        yield return new WaitForSecondsRealtime(1);
        Log($"hero screen: {HeroScreen().Count} heroes");
        DumpGame(Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout"));
        // a match must start from the main menu: loading it over another menu leaves an empty blurred screen
        Step(4, "starting a sandbox match");
        yield return ToMainMenu();
        // sandbox: a new standard game here could replace a saved game on this map
        var data = InGameData.Editable;
        data.selectedMode = "Sandbox";
        data.selectedMap = "Tutorial";  // Monkey Meadow
        data.selectedDifficulty = "Easy";
        Il2CppAssets.Scripts.Unity.UI_New.UI.instance.LoadGame(null, null, null, false, false);
        Log("match loading");
        for (var end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end && !InMatch();) yield return null;
        yield return new WaitForSecondsRealtime(1);
        if (!InMatch())
        {
            yield return Finish("Knowledge Dumper: match did not start in 60 s, press F6 inside a match for the XP data");
            yield break;
        }
        Step(5, "dumping hero XP, back to the main menu");
        DumpInGame(Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout"));
        InGame.instance.QuitToMainMenu();  // what the pause screen home button does
        for (var end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end && InMatch();) yield return null;
        yield return Finish("Knowledge Dumper: done, files in <game>/KnowledgeLayout");
    }

    static bool mapsRunning, mapsStop;

    // Ctrl+Shift+F9 from the main menu: loads every map in a sandbox match and writes a summary of its MapModel
    // (paths, removable obstacles, line of sight blockers, map behaviours) to maps.json. The MapModel only exists while
    // its map is loaded, so there is no way around loading each one. Maps already in the file are skipped, so a run
    // that stopped continues where it left off; pressing the keys again stops after the current map.
    static IEnumerator AutoMaps()
    {
        mapsRunning = true;
        mapsStop = false;
        var path = Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout", "maps.json");
        var done = new Dictionary<string, JsonElement>();
        if (File.Exists(path))
            foreach (var p in JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject()) done[p.Name] = p.Value.Clone();
        var ids = new List<string>();
        foreach (var m in GameData.Instance.mapSet.Maps.items)
            if (!m.isDebug && !done.ContainsKey(m.id)) ids.Add(m.id);
        for (var i = 0; i < ids.Count && !mapsStop; i++)
        {
            status = $"Knowledge Dumper maps {i + 1}/{ids.Count}: {ids[i]} (Ctrl+Shift+F9 stops)";
            Log(status);
            var data = InGameData.Editable;
            data.selectedMode = "Sandbox";
            data.selectedMap = ids[i];
            data.selectedDifficulty = "Easy";
            Il2CppAssets.Scripts.Unity.UI_New.UI.instance.LoadGame(null, null, null, false, false);
            for (var end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end && !InMatch();) yield return null;
            yield return new WaitForSecondsRealtime(1);
            if (InMatch())
            {
                done[ids[i]] = JsonSerializer.SerializeToElement(MapSummary(InGame.instance.bridge.simulation.model.map));
                File.WriteAllText(path, JsonSerializer.Serialize(done, new JsonSerializerOptions { WriteIndented = true }));
                InGame.instance.QuitToMainMenu();
            }
            else Log($"{ids[i]} did not load, skipped");
            for (var end = Time.realtimeSinceStartup + 60;
                 Time.realtimeSinceStartup < end && (InMatch() || Menus.IsTransitioning || Menus.GetCurrentMenuName() != "MainMenuUi");)
                yield return null;
            yield return new WaitForSecondsRealtime(1);
        }
        mapsRunning = false;
        yield return Finish($"Knowledge Dumper: {done.Count} maps in maps.json" + (mapsStop ? " (stopped)" : ""));
    }

    static object MapSummary(MapModel map)
    {
        var paths = new List<float>();
        foreach (var p in map.paths)
        {
            if (p == null || !p.isActive || p.isHidden || p.points == null || p.points.Length == 0) continue;
            paths.Add(p.points[p.points.Length - 1].distance);  // distance runs along the path, the last point holds its length
        }
        var removables = new List<object>();
        if (map.removeables != null)
            foreach (var r in map.removeables)
                if (r != null) removables.Add(new { name = r.menuName, text = r.textKey, cost = r.removealCost });
        var behaviours = new List<string>();
        if (map.mapBehaviours != null)
            foreach (var b in map.mapBehaviours)
                if (b != null) behaviours.Add(b.GetIl2CppType().Name);
        if (map.simBehaviors != null)
            foreach (var b in map.simBehaviors)
                if (b != null) behaviours.Add(b.GetIl2CppType().Name);
        return new
        {
            paths, removables, blockers = map.blockers?.Length ?? 0, speed = map.mapWideBloonSpeed,
            peninsula = map.isPeninsula, regenRemovables = map.regenRemovables,
            behaviours,
        };
    }

    static IEnumerator ToMainMenu()
    {
        Menus.CloseAllMenus();
        for (var end = Time.realtimeSinceStartup + 15; Time.realtimeSinceStartup < end && Menus.IsTransitioning;) yield return null;
        yield return new WaitForSecondsRealtime(1);
        Log("closed menus");
    }

    // order of the buttons on the hero select screen (empty unless that screen is open), left to right, top to bottom
    static List<string> HeroScreen()
    {
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
        return screen;
    }

    static float[] Rect(RectTransform rt)
    {
        // world-space centre and size; the wiki normalises per knowledge set
        var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(corners);
        var c = (corners[0] + corners[2]) / 2;
        return new[] { c.x, c.y, corners[2].x - corners[0].x, corners[2].y - corners[0].y };
    }

    // Facts the Mod Helper export lacks: game version, shop order of towers and heroes, bloon property bits, tower set bits.
    static void DumpGame(string dir)
    {
        var towers = new List<object>();
        foreach (var t in Game.instance.model.towerSet)
            towers.Add(new { id = t.towerId, index = t.towerIndex });
        var heroes = new List<object>();
        foreach (var h in Game.instance.model.heroSet)
            heroes.Add(new { id = h.towerId, index = h.towerIndex });
        var screen = HeroScreen();
        var path = Path.Combine(dir, "game.json");
        if (screen.Count == 0 && File.Exists(path))  // hero screen not open: keep the order from the last dump
        {
            var old = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            if (old.TryGetProperty("heroScreen", out var hs))
                foreach (var h in hs.EnumerateArray()) screen.Add(h.GetString());
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
        File.WriteAllText(path, JsonSerializer.Serialize(
            new
            {
                version = Application.version, towerSet = towers, heroSet = heroes, heroScreen = screen, bloonProperties = props,
                towerSets = sets
            },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped {towers.Count} towers, version {Application.version}");
        DumpAchievements(dir);
    }

    // achievements.json: every achievement with its goal, hidden flag and rewards. Names and descriptions are in
    // textTable (the wiki reads them there); what completes one is game code, not data.
    static void DumpAchievements(string dir)
    {
        var list = new List<object>();
        foreach (var a in GameData.Instance.achievements.achievements)
        {
            list.Add(new
            {
                id = a.achievementId, goal = a.achievementGoal, hidden = a.hidden, coop = a.isCoop,
                type = a.type.ToString(), subscript = a.subscript, loot = a.loot  // loot: id of the reward set
            });
        }
        File.WriteAllText(Path.Combine(dir, "achievements.json"),
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped {list.Count} achievements");
    }

    // Only inside a match (GetXpForRound fails in the menu): hero XP per round and the XP scale of each map difficulty.
    static void DumpInGame(string dir)
    {
        var sim = InGame.instance != null && InGame.instance.bridge != null ? InGame.instance.bridge.simulation : null;
        if (sim == null) return;
        var heroXp = new List<float>();
        for (var r = 0; r < 500; r++)  // index = rounds completed, so [0] is the XP for finishing round 1
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
