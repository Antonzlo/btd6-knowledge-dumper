# Knowledge Dumper

A Bloons TD 6 developer tool. It writes game facts that the Mod Helper export lacks to files in the game folder, for the BTD6 tech wiki. Not a gameplay mod.

| Keys | Where | What it writes |
| --- | --- | --- |
| F6 | Monkey Knowledge screen | tree layout and knowledge icons, shop and hero order, achievements to `KnowledgeLayout/` |
| Ctrl+Shift+F6 | main menu | all of the above plus the difficulty of each mode, rank XP, hero XP per round and freeplay bloon scaling (`freeplay.json`), by opening the screens and a sandbox match itself |
| F7 | any screen | game icons to `GameIcons/` |
| F9 | in a match | the current map (paths, areas, obstacles, placed towers) to `maps.json` and `maps_geo.json` |
| Ctrl+Shift+F9 | main menu | every map, one sandbox match each; press again to stop |

## Requirements

- [MelonLoader](https://github.com/LavaGang/MelonLoader)
- [BTD6 Mod Helper](https://github.com/gurrenm3/BTD-Mod-Helper)

## Install

Put `KnowledgeDumper.dll` into the game's `Mods` folder.

## Build

```
dotnet build -c Release
```

The project looks for the game in `~/.local/share/Steam/steamapps/common/BloonsTD6`. Pass another folder with `-p:BloonsTD6=<path>`.

---

# Knowledge Dumper (русский)

Инструмент разработчика для Bloons TD 6. Записывает в папку игры данные, которых нет в экспорте Mod Helper, для техвики BTD6. На игровой процесс не влияет.

| Клавиши | Где | Что записывает |
| --- | --- | --- |
| F6 | экран знаний обезьян | раскладку дерева и иконки знаний, порядок магазина и героев, достижения в `KnowledgeLayout/` |
| Ctrl+Shift+F6 | главное меню | всё перечисленное, сложность каждого режима, опыт уровней аккаунта, опыт героев по раундам и рост шаров во фриплее (`freeplay.json`): мод сам открывает экраны и матч в песочнице |
| F7 | любой экран | иконки игры в `GameIcons/` |
| F9 | в матче | текущую карту (пути, зоны, препятствия, поставленные башни) в `maps.json` и `maps_geo.json` |
| Ctrl+Shift+F9 | главное меню | все карты, по одному матчу в песочнице на каждую; повторное нажатие останавливает |

## Установка

Нужны MelonLoader и BTD6 Mod Helper. Положите `KnowledgeDumper.dll` в папку `Mods` игры.
