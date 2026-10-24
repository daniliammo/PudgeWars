# Извлечение ассетов Pudge Wars (UE 5.3 → Unity)

Пайплайн, которым получены `Assets/_Project/Art/Characters/{Pudge,PudgeToy}` и `Assets/_Project/Art/Hooks/*`.
Нативный Oodle не нужен: CUE4Parse распаковывает через управляемый OodleSharp.

## Шаги

1. **Маппинги свойств.** Пакеты игры cooked с unversioned properties, поэтому CUE4Parse нужен `.usmap`.
   Он генерируется из рефлексии движка в `libUnreal.so` (arm64, stripped) — разбор таблиц `UECodeGen_Private::F*Params`:
   ```bash
   python3 -I elfimg.py <apk>/lib/arm64-v8a/libUnreal.so img.pkl   # ELF + упакованные релокации APS2
   python3 -I usmapgen.py img.pkl pudge.usmap 0                    # ~6500 схем, 1300 enum'ов
   ```
2. **Выгрузка** (`Extractor/`, .NET 10, CUE4Parse 1.2.2): меши/скелеты/анимации/материалы → JSON, текстуры → сырые RGBA.
   ```bash
   USMAP=pudge.usmap dotnet run --project Extractor -- <obb>/PudgeWars/Content/Paks out \
       PudgeWars/Content/PudgeWars/Assets/Pudge/ PudgeWars/Content/PudgeWars/Assets/Toy/ PudgeWars/Content/PudgeWars/Assets/Hook/
   python3 -I tex2png.py out          # PNG; у normal map восстанавливается B и инвертируется G (DirectX → OpenGL)
   ```
3. **Раскладка и FBX** (Blender 5.x):
   ```bash
   python3 -I stage.py out stage
   blender -b --factory-startup -P build_fbx.py -- stage/blender_job.json
   ```
   `preview.py` рендерит позу из JSON для визуальной проверки.
4. **Unity:** скопировать `stage/Assets/...` в проект, `stage/unity_manifest.json` — сюда, затем
   меню **Pudge Wars → Setup Imported Assets** (`Assets/_Project/Editor/PudgeAssetImporter.cs`):
   настройки текстур, URP-материалы, ремап материалов FBX, Generic-риг, клипы, Animator Controller и префабы.

5. **Звук.** `SoundDumper/` выгружает SoundWave как есть в `SoundsRaw/` (уже лежат здесь, 44 файла).
   Это **BINKA = Bink Audio 2** (RAD): ffmpeg (`-f binka`) разбирает контейнер, но декодирует шумом — кодек новее.
   Нужен декодер RAD `binkadec` (FModel: `Output/.data/binkadec.exe`, на Linux — через wine):
   ```bash
   python3 sounds.py --binkadec /path/binkadec.exe --wine        # wine из PortProton, свой префикс; звуки хука → Assets/_Project/Audio/Pudge
   ```
   Затем в Unity: **Pudge Wars → Assign Hook Sounds** (или пересобрать арену).

## Что важно знать

- **Оси.** UE (левая СК, Z вверх, см) → Unity (Y вверх, м). Модели смотрят по +Z, хуки направлены по +Z.
  FBX экспортируются без bake space transform; в Unity включён `bakeAxisConversion` и `preserveHierarchy`
  (иначе в анимационных FBX арматура становится корнем и пути кривых не совпадают с моделью).
- **Поворот меша в Blueprint.** В `Player_PW`/`Player_Pudge` компонент меша повёрнут (Yaw −90, Roll 90), и корневые
  треки анимаций рассчитаны на это. Bind-поза и анимации поэтому конвертируются разными преобразованиями
  (см. шапку `build_fbx.py`). Toy (`Player_PW_Child`): Yaw −90 и масштаб 0.5 — масштаб запечён в FBX.
- **Обрезка циклов.** `Pudge_Run`, `Pudge_Run2`, `Walk` в оригинале — 64-секундные клипы из повторяющегося
  1-секундного цикла (у `Pudge_Run1` период 3.5 с) с постепенно нарастающим дрейфом позы. В Unity лежит первый цикл
  (замыкается с ошибкой ~1e-4). Полные данные — в JSON выгрузки.
- **Материалы.** В оригинале все материалы `Unlit` (цвет + нормаль). В Unity сделаны URP/Lit с BaseMap + NormalMap;
  варианты `Lotar` (невидимость) — URP/Unlit, аддитивные, цвет × 0.5, как в UE.
- **Дубликаты.** `Pudge/Texture/Skin/Set_Base` = `Pudge/Texture/Textere`, `Pudge/Hook/HookPudge` = `Hook/Hook_Standart` —
  взята одна копия. `pudge_Anim` (статичная поза импорта) не переносилась.
- Animator Controller содержит по состоянию на клип без переходов: логика AnimBP оригинала в этот проход не входила.
