using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BTD_Mod_Helper;
using BTD_Mod_Helper.Extensions;
using Il2CppAssets.Scripts.Unity;
using Il2CppNinjaKiwi.Common.ResourceUtils;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace KnowledgeDumper;

// F7: saves game icons for the tech wiki to <game>/GameIcons/<kind>/<id>.png.
// Model icons (towers, upgrades, bloons, powers) load through the game's own loader.
// Icons without a model reference (buffs, modes, UI) come from every sprite atlas loaded right now,
// so pressing F7 on several screens (main menu, mode select, in a game) collects more. Existing files are skipped.
public static class Icons
{
    static string Safe(string s) => Regex.Replace(s, "[^A-Za-z0-9_-]", "_");

    public static IEnumerator Dump()
    {
        var root = Path.Combine(MelonEnvironment.GameRootDirectory, "GameIcons");
        var model = Game.instance.model;
        var refs = new List<(string kind, string id, SpriteReference sprite)>();
        foreach (var t in model.towerSet)
        {
            var tower = model.GetTowerFromId(t.towerId);
            if (tower != null) refs.Add(("towers", t.towerId, tower.portrait));
        }
        foreach (var h in model.heroSet)
        {
            var hero = model.GetTowerFromId(h.towerId);
            if (hero != null) refs.Add(("heroes", h.towerId, hero.portrait));
        }
        // ability icons of every tower and hero model, saved by sprite GUID (the export has the same GUID)
        var seen = new HashSet<string>();
        foreach (var tower in model.towers)
            foreach (var b in tower.behaviors)
            {
                var a = b.TryCast<Il2CppAssets.Scripts.Models.Towers.Behaviors.Abilities.AbilityModel>();
                if (a?.icon is null || string.IsNullOrEmpty(a.icon.guidRef) || !seen.Add(a.icon.guidRef)) continue;
                refs.Add(("abilities", a.icon.guidRef, a.icon));
            }
        foreach (var u in model.upgrades) refs.Add(("upgrades", u.name, u.icon));
        foreach (var b in model.bloons) refs.Add(("bloons", b.id, b.icon));
        foreach (var p in model.powers) refs.Add(("powers", p.name, p.icon));
        // achievement icons by achievement id (achievements.json has the same ids)
        foreach (var a in Il2CppAssets.Scripts.Data.GameData.Instance.achievements.achievements)
            refs.Add(("achievements", a.achievementId.ToString(), a.achievementIcon));
        // buff indicator icons: the game's buffId -> sprite table, so heroes you do not own are included
        foreach (var b in Il2CppAssets.Scripts.Data.GameData.Instance.buffIconSprites.buffIconSprites)
            refs.Add(("buffs", b.buffId, b.icon));

        int saved = 0, failed = 0;
        const int batch = 40;
        for (int i = 0; i < refs.Count; i += batch)
        {
            var pending = new List<(string path, Image image, GameObject go)>();
            for (int j = i; j < System.Math.Min(i + batch, refs.Count); j++)
            {
                var (kind, id, sprite) = refs[j];
                var path = Path.Combine(root, kind, Safe(id) + ".png");
                if (sprite is null || string.IsNullOrEmpty(sprite.guidRef) || File.Exists(path)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var go = new GameObject("IconDump");
                var image = go.AddComponent<Image>();
                ResourceLoader.LoadSpriteFromSpriteReference(sprite, image, false);
                pending.Add((path, image, go));
            }
            for (int frame = 0; frame < 120 && pending.Exists(p => p.image.sprite == null); frame++)
                yield return null;
            foreach (var (path, image, go) in pending)
            {
                if (image.sprite != null && image.sprite.TrySaveToPNG(path)) saved++;
                else failed++;
                Object.Destroy(go);
            }
        }

        var sprites = Path.Combine(root, "sprites");
        Directory.CreateDirectory(sprites);
        int fromAtlas = 0;
        foreach (var atlas in Resources.FindObjectsOfTypeAll<SpriteAtlas>())
        {
            var all = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Sprite>(atlas.spriteCount);
            atlas.GetSprites(all);
            foreach (var s in all)
            {
                if (s == null) continue;
                var path = Path.Combine(sprites, Safe(s.name.Replace("(Clone)", "")) + ".png");
                if (File.Exists(path)) continue;
                if (s.TrySaveToPNG(path)) fromAtlas++;
            }
            yield return null;
        }
        // standalone sprites outside atlases (mode and boss buttons): only the ones loaded on the current screen
        int loose = 0;
        foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (s == null || string.IsNullOrEmpty(s.name)) continue;
            var path = Path.Combine(sprites, Safe(s.name.Replace("(Clone)", "")) + ".png");
            if (File.Exists(path)) continue;
            if (s.TrySaveToPNG(path)) loose++;
            if (loose % 50 == 49) yield return null;
        }
        fromAtlas += loose;
        ModHelper.Msg<Mod>($"Icons: {saved} from models ({failed} failed), {fromAtlas} from atlases, to {root}");
    }
}
