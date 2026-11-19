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
using Il2CppAssets.Scripts.Simulation.Freeplay;
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
    static float progress = -1;  // 0..1 for the bar, -1 hides it

    public override void OnGUI()
    {
        if (status == null || !Overlay.Failed) return;  // the game-style overlay draws it; this box is the fallback
        GUI.skin.box.fontSize = 22;
        GUI.Box(new Rect(Screen.width / 2f - 300, 24, 600, 44), status);
    }

    static void Step(int n, string text)
    {
        status = $"{n}/5: {text}";
        progress = (n - 1) / 5f;
        Log(status);
    }

    static IEnumerator Finish(string text)
    {
        status = text;
        progress = -1;
        Log(text);
        yield return new WaitForSecondsRealtime(4);
        status = null;
    }

    public override void OnUpdate()
    {
        var menu = Menus != null ? Menus.GetCurrentMenuName() : null;
        if (menu != lastMenu) ModHelper.Msg<Mod>($"menu: {lastMenu = menu}");
        Overlay.Show(status, progress);
        var combo = Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift);
        if (Input.GetKeyDown(KeyCode.F6)) { if (combo) MelonCoroutines.Start(Auto()); else Dump(); }
        if (Input.GetKeyDown(KeyCode.F7)) { if (combo) WhereAmI(); else MelonCoroutines.Start(Icons.Dump()); }
        if (Input.GetKeyDown(KeyCode.F9))
        {
            if (!combo) { if (InMatch()) DumpCurrentMap(); }
            else if (mapsRunning) mapsStop = true;
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
        // mode select screen (map and difficulty picked as the menus would): which difficulty each mode sits under
        Step(3, "mode screen");
        yield return ToMainMenu();
        InGameData.Editable.selectedMap = "Tutorial";
        InGameData.Editable.selectedDifficulty = "Easy";
        Menus.OpenMenu("ModeSelectUI", null, false);
        for (var end = Time.realtimeSinceStartup + 15; Time.realtimeSinceStartup < end && ModeDifficulty().Count == 0;) yield return null;
        lastModes = ModeDifficulty();
        Log($"mode screen: {lastModes.Count} modes");
        // hero select screen: game.json then gets the order of its hero buttons
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
    // its map is loaded, so there is no way around loading each one (plain F9 in a match dumps only that map). Maps already in the file are skipped, so a run
    // that stopped continues where it left off; pressing the keys again stops after the current map.
    static string MapsPath => Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout", "maps.json");
    static string GeoPath => Path.Combine(MelonEnvironment.GameRootDirectory, "KnowledgeLayout", "maps_geo.json");

    static Dictionary<string, JsonElement> LoadMaps(string path)
    {
        var d = new Dictionary<string, JsonElement>();
        if (File.Exists(path))
            foreach (var p in JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateObject()) d[p.Name] = p.Value.Clone();
        return d;
    }

    // adds the loaded map to maps.json and maps_geo.json, replacing an older entry of it
    static void SaveMap(string id, Dictionary<string, JsonElement> done, Dictionary<string, JsonElement> geo)
    {
        var map = InGame.instance.bridge.simulation.model.map;
        done[id] = JsonSerializer.SerializeToElement(MapSummary(map));
        File.WriteAllText(MapsPath, JsonSerializer.Serialize(done, new JsonSerializerOptions { WriteIndented = true }));
        geo[id] = JsonSerializer.SerializeToElement(MapGeo(map));
        File.WriteAllText(GeoPath, JsonSerializer.Serialize(geo));  // compact: thousands of points
    }

    // F9 inside a match: dump just the map being played (any mode or difficulty, browser-only maps too)
    static void DumpCurrentMap()
    {
        var id = InGameData.CurrentGame.selectedMap;
        try
        {
            SaveMap(id, LoadMaps(MapsPath), LoadMaps(GeoPath));
            MelonCoroutines.Start(Finish($"Map {id} written to maps.json and maps_geo.json"));
        }
        catch (System.Exception e)
        {
            ModHelper.Error<Mod>($"{id}: {e.Message}");
        }
    }

    static IEnumerator AutoMaps()
    {
        mapsRunning = true;
        mapsStop = false;
        var done = LoadMaps(MapsPath);
        var geo = LoadMaps(GeoPath);
        var ids = new List<string>();
        foreach (var m in GameData.Instance.mapSet.Maps.items)
            if (!m.isDebug && !m.isBrowserOnly && !(done.ContainsKey(m.id) && geo.ContainsKey(m.id))) ids.Add(m.id);  // browser-only: editor stub and web maps
        for (var i = 0; i < ids.Count && !mapsStop; i++)
        {
            status = $"Maps {i + 1}/{ids.Count}: {ids[i]} (Ctrl+Shift+F9 stops)";
            progress = (float)i / ids.Count;
            Log(status);
            yield return ToMainMenu();  // like the auto dump: LoadGame right over MainMenuUi bounces back to the menu
            var data = InGameData.Editable;
            data.selectedMode = "Sandbox";
            data.selectedMap = ids[i];
            data.selectedDifficulty = "Easy";
            Il2CppAssets.Scripts.Unity.UI_New.UI.instance.LoadGame(null, null, null, false, false);
            for (var end = Time.realtimeSinceStartup + 60; Time.realtimeSinceStartup < end && !InMatch();) yield return null;
            yield return new WaitForSecondsRealtime(1);
            if (InMatch())
            {
                try
                {
                    SaveMap(ids[i], done, geo);
                }
                catch (System.Exception e)
                {
                    ModHelper.Error<Mod>($"{ids[i]}: {e.Message}");  // one bad map should not end the run
                }
                InGame.instance.QuitToMainMenu();
            }
            else Log($"{ids[i]} did not load, skipped");
            for (var end = Time.realtimeSinceStartup + 60;
                 Time.realtimeSinceStartup < end && (InMatch() || Menus.IsTransitioning || Menus.GetCurrentMenuName() != "MainMenuUi");)
                yield return null;
            yield return new WaitForSecondsRealtime(1);
        }
        mapsRunning = false;
        yield return Finish($"{done.Count} maps in maps.json" + (mapsStop ? " (stopped)" : ""));
    }

    // Map geometry for the wiki's map viewer, in sim coordinates rounded to 0.1: path points, area polygons
    // (type is the AreaType name: Land, Water, Track, Unplaceable...; holes cut out of them), line of sight blockers as
    // [x, y, radius] and removable obstacles (position and the ids of the areas that go away with them).
    static float R(float v) => (float)System.Math.Round(v, 1);

    static object MapGeo(MapModel map)
    {
        var paths = new List<object>();
        foreach (var p in map.paths)
        {
            if (p == null || p.points == null) continue;
            var pts = new List<float[]>();
            foreach (var q in p.points) pts.Add(new[] { R(q.point.x), R(q.point.y) });
            paths.Add(new { id = p.pathId, active = p.isActive, hidden = p.isHidden, points = pts });
        }
        var areas = new List<object>();
        if (map.areas != null)
            foreach (var a in map.areas)
                if (a != null) areas.Add(new { id = a.id.Id, name = a.name, type = a.type.ToString(), height = R(a.height), blocker = a.isBlocker, disabled = a.isDisabled, points = Poly(a.polygon), holes = Holes(a.holes) });
        var blockers = new List<object>();
        if (map.blockers != null)
            foreach (var b in map.blockers)
                if (b != null && !b.isDisabled) blockers.Add(new[] { R(b.circle.position.x), R(b.circle.position.y), R(b.circle.radius) });
        var removables = new List<object>();
        if (map.removeables != null)
            foreach (var r in map.removeables)
                if (r != null) removables.Add(new { name = r.menuName, obj = r.objecName, model = r.name, cost = r.removealCost, x = R(r.position.x), y = R(r.position.y), areas = Ids(r.ids) });
        // spawner with its splitters (which path each bloon takes: MOAB-only, by tag, by round...), every subtype as is
        var spawner = map.spawner == null ? (JsonElement?)null : JsonDocument.Parse(ModelSerializer.SerializeModel(map.spawner)).RootElement.Clone();
        // map events with their settings: trigger (round number, obstacle id, timer...) and actions (path or area ids)
        var events = new List<JsonElement>();
        if (map.mapEvents != null)
            foreach (var e in map.mapEvents)
                if (e != null) events.Add(JsonDocument.Parse(ModelSerializer.SerializeModel(e)).RootElement.Clone());
        var gizmos = new List<JsonElement>();
        if (map.gizmos != null)
            foreach (var gz in map.gizmos)
                if (gz != null) gizmos.Add(JsonDocument.Parse(ModelSerializer.SerializeModel(gz)).RootElement.Clone());
        return new { paths, areas, blockers, removables, spawner, events, gizmos, interactables = Interactables(events), towers = PlacedTowers() };
    }

    // towers standing on the map when it is dumped (F9 in a match after placing some): ground truth for the wiki's
    // placement check. Footprint as the model has it: radius for a circle, width and height for a rectangle.
    static List<object> PlacedTowers()
    {
        var list = new List<object>();
        foreach (var t in InGame.instance.GetTowers())
        {
            if (t?.towerModel == null || t.towerModel.isSubTower) continue;  // sub towers (drones, pets) have no placement
            var fp = t.towerModel.footprint;
            var circle = fp?.TryCast<Il2CppAssets.Scripts.Models.Towers.Behaviors.CircleFootprintModel>();
            var rect = fp?.TryCast<Il2CppAssets.Scripts.Models.Towers.Behaviors.RectangleFootprintModel>();
            list.Add(new
            {
                id = t.towerModel.baseId, x = R(t.Position.X), y = R(t.Position.Y),
                r = circle != null ? R(circle.radius) : 0f, w = rect != null ? R(rect.xWidth) : 0f, h = rect != null ? R(rect.yWidth) : 0f,
            });
        }
        return list;
    }

    // click targets of map events are scene objects, named in the triggers: the bounds of their colliders, to place
    // them on the map (map x = world x, map y = -world z)
    static Dictionary<string, List<float[]>> Interactables(List<JsonElement> events)
    {
        var names = new HashSet<string>();
        void Scan(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                {
                    if ((p.Name == "interactableName" || p.Name == "interactableClickSequenceName") && p.Value.ValueKind == JsonValueKind.String)
                        names.Add(p.Value.GetString());
                    Scan(p.Value);
                }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Scan(x);
        }
        foreach (var e in events) Scan(e);
        var found = new Dictionary<string, List<float[]>>();
        if (names.Count == 0) return found;
        foreach (var tr in UnityEngine.Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (tr == null || !names.Contains(tr.name) || found.ContainsKey(tr.name) || !tr.gameObject.scene.IsValid()) continue;
            // the named object carries an InteractableClick (or a sequence of them, in click order); the click lands
            // on its onClickHandler's collider
            var list = new List<float[]>();
            var seq = tr.GetComponent<Il2CppAssets.Scripts.Unity.Map.Effects.InteractableClickSequence>();
            var seqTrig = tr.GetComponent<Il2CppAssets.Scripts.Unity.Map.Triggers.InteractableClickSequenceTrigger>();
            if (seq == null && seqTrig != null) seq = seqTrig.interactableClickSequence;
            if (seq != null && seq.sequence != null)
            {
                foreach (var it in seq.sequence)
                    if (it != null) list.Add(ClickBounds(it)[0]);  // one spot per step: a step clicked twice repeats
            }
            else list.AddRange(ClickBounds(tr));
            found[tr.name] = list;
        }
        return found;
    }

    // a disabled collider (a button that turns on later) reports empty bounds at its object's position, so boxes and
    // spheres are measured from their own settings, which hold either way
    static float[] Bounds(Collider col)
    {
        var t = col.transform;
        var box = col.TryCast<BoxCollider>();
        if (box != null)
        {
            var c = t.TransformPoint(box.center);
            var size = Vector3.Scale(box.size, t.lossyScale);
            return new[] { R(c.x), R(c.y), R(c.z), R(System.Math.Abs(size.x)), R(System.Math.Abs(size.z)) };
        }
        var sphere = col.TryCast<SphereCollider>();
        if (sphere != null)
        {
            var c = t.TransformPoint(sphere.center);
            var d = 2 * sphere.radius * System.Math.Max(System.Math.Abs(t.lossyScale.x), System.Math.Abs(t.lossyScale.z));
            return new[] { R(c.x), R(c.y), R(c.z), R(d), R(d) };
        }
        var b = col.bounds;
        return new[] { R(b.center.x), R(b.center.y), R(b.center.z), R(b.size.x), R(b.size.z) };
    }

    // bounds [center x, y, z, size x, size z] of what takes the click: an InteractableClick's onClickHandler
    // colliders, else the colliders under the object, else its position with size 0
    static List<float[]> ClickBounds(Component c)
    {
        var ic = c.GetComponent<Il2CppAssets.Scripts.Unity.Map.Effects.InteractableClick>();
        Component target = ic != null && ic.onClickHandler != null ? ic.onClickHandler : c;
        var list = new List<float[]>();
        foreach (var col in target.GetComponentsInChildren<Collider>(true))
            list.Add(Bounds(col));
        if (list.Count == 0)
            list.Add(new[] { R(target.transform.position.x), R(target.transform.position.y), R(target.transform.position.z), 0f, 0f });
        return list;
    }

    static List<List<float[]>> Holes(IEnumerable<Il2CppAssets.Scripts.Simulation.SMath.Polygon> holes)
    {
        var list = new List<List<float[]>>();
        if (holes != null) foreach (var h in holes) list.Add(Poly(h));
        return list;
    }

    static List<int> Ids(Il2CppSystem.Collections.Generic.List<Il2CppAssets.Scripts.ObjectId> ids)
    {
        var list = new List<int>();
        if (ids != null) for (var k = 0; k < ids.Count; k++) list.Add(ids[k].Id);
        return list;
    }

    static List<float[]> Poly(Il2CppAssets.Scripts.Simulation.SMath.Polygon poly)
    {
        var pts = new List<float[]>();
        if (poly?.points != null)
            foreach (var v in poly.points) pts.Add(new[] { R(v.x), R(v.y) });
        return pts;
    }

    static object MapSummary(MapModel map)
    {
        var paths = new List<float>();
        foreach (var p in map.paths)
        {
            if (p == null || !p.isActive || p.isHidden || p.points == null || p.points.Length == 0) continue;
            float length = 0;  // PointInfo.distance is not the running length (it read 0), so sum the segments
            for (var k = 1; k < p.points.Length; k++)
            {
                var a = p.points[k - 1].point;
                var b = p.points[k].point;
                length += (float)System.Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            }
            paths.Add(length);
        }
        var removables = new List<object>();
        if (map.removeables != null)
            foreach (var r in map.removeables)
                if (r != null) removables.Add(new { name = r.menuName, text = r.textKey, cost = r.removealCost });
        var behaviours = new List<string>();
        if (map.mapBehaviours != null)
            foreach (var b in map.mapBehaviours)
                if (b != null) behaviours.Add(b.GetIl2CppType().Name);
        // each event as "TriggerA+TriggerB > ActionA+ActionB", model type names without the Model suffix
        static string Names(IEnumerable<Il2CppSystem.Object> items) => items == null ? "" : string.Join("+",
            System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(items, x => x != null),
                x => x.GetIl2CppType().Name.Replace("Model", "")));
        var events = new List<string>();
        if (map.mapEvents != null)
            foreach (var e in map.mapEvents)
                if (e != null) events.Add($"{Names(e.triggers)} > {Names(e.actions)}");
        var regen = new List<string>();
        if (map.regenRemovables != null)
            foreach (var r in map.regenRemovables)
                if (r != null) regen.Add(r.GetIl2CppType().Name);
        if (map.simBehaviors != null)
            foreach (var b in map.simBehaviors)
                if (b != null) behaviours.Add(b.GetIl2CppType().Name);
        return new
        {
            paths, removables, speed = map.mapWideBloonSpeed, events,
            regenRemovables = regen,
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

    static Dictionary<string, string> lastModes = new();

    // Which difficulty each mode sits under: the mode screen keeps one button group per difficulty, each button names
    // its mode. Empty unless the mode screen has been loaded.
    static Dictionary<string, string> ModeDifficulty()
    {
        var modes = new Dictionary<string, string>();
        foreach (var ms in UnityEngine.Resources.FindObjectsOfTypeAll<Il2CppAssets.Scripts.Unity.UI_New.Main.ModeSelect.ModeScreen>())
            foreach (var (diff, group) in new[] { ("Easy", ms.easyModes), ("Medium", ms.mediumModes), ("Hard", ms.hardModes) })
                if (group != null)
                    foreach (var b in group.GetComponentsInChildren<Il2CppAssets.Scripts.Unity.UI_New.Main.ModeSelect.ModeButton>(true))
                        if (!string.IsNullOrEmpty(b.modeType) && b.modeType != diff)
                            modes[b.modeType] = modes.TryGetValue(b.modeType, out var d) && d != diff ? "" : diff;
        // Standard and Sandbox sit under every difficulty: not tied to one, left out
        foreach (var k in new List<string>(modes.Keys))
            if (modes[k] == "") modes.Remove(k);
        return modes;
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
        var modes = ModeDifficulty();
        if (modes.Count == 0) modes = lastModes;  // read by Auto while the mode screen was open
        if (modes.Count == 0 && File.Exists(path))  // mode screen never opened: keep the list from the last dump
        {
            var old = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            if (old.TryGetProperty("modeDifficulty", out var md))
                foreach (var p in md.EnumerateObject()) modes[p.Name] = p.Value.GetString();
        }
        File.WriteAllText(path, JsonSerializer.Serialize(
            new
            {
                version = Application.version, towerSet = towers, heroSet = heroes, heroScreen = screen, bloonProperties = props,
                towerSets = sets,
                modeDifficulty = modes
            },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped {towers.Count} towers, version {Application.version}");
        DumpAchievements(dir);
        DumpRankXp(dir);
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

    static void DumpRankXp(string dir)
    {
        var rankInfo = GameData.Instance.rankInfo;
        var ranks = rankInfo?.rankInfo;
        if (ranks == null || ranks.Count == 0)
        {
            ModHelper.Warning<Mod>("Rank XP table is unavailable");
            return;
        }

        var totalXpNeeded = new List<long>(ranks.Count);
        foreach (var rank in ranks)
            totalXpNeeded.Add(rank.totalXpNeeded);

        File.WriteAllText(Path.Combine(dir, "rank-xp.json"), JsonSerializer.Serialize(
            new { version = Application.version, totalXpNeeded, rankInfo.xpNeededPerVeteranRank },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped XP thresholds for {totalXpNeeded.Count} rank entries");
    }

    // Only inside a match (GetXpForRound fails in the menu): hero XP per round and the XP scale of each map difficulty.
    static void DumpInGame(string dir)
    {
        var sim = InGame.instance != null && InGame.instance.bridge != null ? InGame.instance.bridge.simulation : null;
        var model = sim?.model;
        if (model == null) return;
        var heroXp = new List<float>();
        for (var r = 0; r < 500; r++)  // index 0 is the XP for finishing round 1
            heroXp.Add(Simulation.GetXpForRound(r, model));
        // the static array is indexed by the MapDifficulty enum
        var scales = new Dictionary<string, float>();
        var table = Simulation.trackDifficultyXpScale;
        foreach (var v in System.Enum.GetValues(typeof(Il2CppAssets.Scripts.Data.MapSets.MapDifficulty)))
        {
            var i = System.Convert.ToInt32(v);
            if (table != null && i >= 0 && i < table.Length) scales[v.ToString()] = table[i];
        }
        File.WriteAllText(Path.Combine(dir, "xp.json"), JsonSerializer.Serialize(
            new
            {
                version = Application.version,
                heroXpPerRound = heroXp,
                trackDifficultyXpScale = scales,
                sample = new
                {
                    gameType = model.gameType.ToString(),
                    mapDifficulty = model.map?.mapDifficulty.ToString(),
                    model.selectedDifficultyRating,
                    model.endRound
                }
            },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped hero XP for {heroXp.Count} rounds and {scales.Count} map XP scales");
        DumpFreeplay(dir);
    }

    // freeplay.json: how bloons get tougher in late rounds. ProgressiveDifficultyManager keeps its thresholds in static
    // tables; GetHealthMultiplier/GetSpeedMultiplier take the round shown on screen, so the per-round lists come from
    // the game's own functions and stay right if a patch changes the formula.
    static void DumpFreeplay(string dir)
    {
        static List<float[]> Pairs(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Collections.Generic.KeyValuePair<int, float>> a)
        {
            var list = new List<float[]>();
            if (a != null)
                foreach (var p in a)
                    list.Add(new float[] { p.Key, p.Value });
            return list;
        }
        static List<T> Plain<T>(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<T> a) where T : unmanaged
        {
            var list = new List<T>();
            if (a != null)
                foreach (var v in a)
                    list.Add(v);
            return list;
        }
        var health = new List<float>();
        var speed = new List<float>();
        var stun = new List<float>();
        for (var r = 1; r <= 1000; r++)  // index 0 is round 1
        {
            health.Add(ProgressiveDifficultyManager.GetHealthMultiplier(r));
            speed.Add(ProgressiveDifficultyManager.GetSpeedMultiplier(r));
            stun.Add(ProgressiveDifficultyManager.GetStunResistance(r));
        }
        File.WriteAllText(Path.Combine(dir, "freeplay.json"), JsonSerializer.Serialize(
            new
            {
                version = Application.version,
                ProgressiveDifficultyManager.fromRound,
                ProgressiveDifficultyManager.fromRoundRogue,
                ProgressiveDifficultyManager.healthIncreaseMax,
                ProgressiveDifficultyManager.superCeramicAdditionalCash,
                roundBasedHealthIncrease = Pairs(ProgressiveDifficultyManager.roundBasedHealthIncrease),
                roundBasedSpeedIncrease = Pairs(ProgressiveDifficultyManager.roundBasedSpeedIncrease),
                bonusHealthIncreaseRounds = Pairs(ProgressiveDifficultyManager.bonusHealthIncreaseRounds),
                bonusSpeedIncreaseRounds = Pairs(ProgressiveDifficultyManager.bonusSpeedIncreaseRounds),
                stunResistanceRounds = Plain(ProgressiveDifficultyManager.stunResistanceRounds),
                stunResistanceAmounts = Plain(ProgressiveDifficultyManager.stunResistanceAmounts),
                healthMultiplierPerRound = health,
                speedMultiplierPerRound = speed,
                stunResistancePerRound = stun
            },
            new JsonSerializerOptions { WriteIndented = true }));
        ModHelper.Msg<Mod>($"Dumped freeplay scaling for {health.Count} rounds");
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
