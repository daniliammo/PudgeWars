# План переноса Pudge Wars на Unity

Источник: `com.Ikrut.PudgeWars_3.2.xapk` (Android). Цель — Unity-версия с рабочим мультиплеером в локальной сети и через интернет по IP.

## Текущее состояние (2026-10-24)

| | |
|---|---|
| ✅ Извлечение | Пайплайн `Tools/AssetExtraction` (CUE4Parse + свой `.usmap` из `libUnreal.so` + Blender → FBX). Перенесены Pudge, Toy Pudge, 5 хуков, текстура цепи |
| ✅ Тестовая арена | `Assets/_Project/Scenes/Arena.unity` (собирается меню **Pudge Wars → Build Test Arena**), 28×28 м, яма 6 м: игрок и 4 бота по разные стороны; хук ловит цель, тащит, цель падает в яму и умирает; боты тоже кидают хук в игрока (случайно с упреждением или в текущую позицию + ошибка прицела); счёт «убийства / смерти», респавн через 2.5 с; попадание обнуляет перезарядку; пойманный теряет свой хук. Игрок и боты — префабы `Prefabs/Characters/Pudge_Player`, `Pudge_Bot`; движение и поворот как у Pudge в Dota 2. Золото: 250 за убийство тому, чей хук утащил жертву; магазин (B) с Aether Lens (2275, +225 к дальности) |
| ✅ Тесты | 9 PlayMode-тестов `Assets/_Project/Tests/PlayMode` (игрок хукает бота → яма → счёт → респавн; бот хукает игрока → смерть → респавн; хук не «зависает» после смерти владельца посреди броска; магазин: покупка Aether Lens и дальность хука 15.25 м; золото за убийство; прицел бота с упреждением и без; поворот по правилам Dota перед движением и кастом; пойманный теряет свой хук; промах; граница стороны) — проходят в batchmode |
| ⏸ Звуки (отложено) | Слоты звуков хука привязаны по схеме Blueprint, клипов нет: звук в паках — **BINKA = Bink Audio 2** (RAD), ffmpeg его не декодирует (даёт шум). Wine из PortProton уже подхватывается `sounds.py`; не хватает только закрытого декодера RAD `binkadec.exe` (идёт с FModel) → `python3 Tools/AssetExtraction/sounds.py --binkadec <путь> --wine` |

---

## 1. Анализ оригинала

| | |
|---|---|
| Движок | Unreal Engine 5.3 (`++UE5+Release-5.3-CL-29314046`), только arm64 |
| Логика | Почти полностью Blueprint; из C++ только `APudgePlayerController` |
| Контент | `assets/main.obb.png` — zip с IoStore (`PudgeWars-Android_ETC2.utoc/.ucas`, `.pak`, `global.*`), ~70 МБ |
| Шифрование | Нет (GUID ключа нулевой) |
| Сжатие | Oodle (5183 блока сжато, 892 — raw) |
| Пакетов игры | ~500 в `/Game/PudgeWars/...` |
| Сеть оригинала | UE listen-server: подключение по IP (`IP_HOST`, дефолт `127.0.0.1`) или `FindSessions`/`JoinSession` (`LOCALHOST`) — по сути только LAN |
| Свойства | Unversioned properties → нужен `.usmap`; генерируется из рефлексии движка в `libUnreal.so` (`usmapgen.py`) |
| Звук | `BINKA` (UEBA-контейнер, Bink Audio 2), 44 SoundWave; SoundCue — Random по WavePlayer'ам |
| Прочее | Google Play Billing, реклама (AD_ID), Play Games |

### Структура контента

```
PudgeWars/
  Map/            Game_Map (боты), Multiplayer_Map, Menu_PW
  Multiplayer/    GameMode_PudgeWars, PlayerState_PudgeWars, PudgeWarsPlayerController,
                  HUD_Multiplayer, Player/Player_Pudge, Player/Arrow,
                  Menu/(Play_PvP, IP_HOST, LOCALHOST), Widget_Tab/(MagazinGame, MenuGame, TopInfoPlayers, ...)
  Blueprints/     Player_PW, Player_PW_Child (Toy), GM_PW, PC_PW, HUD_PW(Local),
                  AI/Base_Bot (BaseBot_PW, Pudge_BOT, BaseBot_AIController_PW),
                  Spal/Overgrowth, SaveGame/SaveGame_PW, Widget/(Chat, Pause, Shop, PuckSpal, ...)
  Assets/         Pudge, Toy, Mirana, Treant, Hook/*, FX/(Chain, Puck), Landscape, Sounds, UI, Widget, Touch, Target
  Menu/           Главное меню, гардероб (пьедестал, RenderTexture), ранги, рекорды, «База знаний»
```

## 2. Ассеты

Статус: ✅ Pudge (меш, 15 анимаций, текстуры, материалы + `Lotar`), ✅ Toy Pudge (8 анимаций), ✅ хуки Standart/Dragonclaw/SorrowfulPrey/ScorchingTalon/Toy, ✅ текстура цепи `ZepStandart`; ⛔ звук (см. «Текущее состояние»); остальное — не начато.

- **Персонажи:** Pudge (скелет, физассет), скин Toy Pudge, Mirana, Treant (для Overgrowth).
- **Анимации Pudge:** Idle, Walk, Run ×3, StartHook/EndHook (+ «Dendy»), Popadanie (попадание), Stun, Death_Hook, Spawn, Porojenie, Nasmechka (насмешка). Toy — аналогичный набор; Mirana — Idle/Run/Jump/Arrow/Respawn.
- **Хуки (гардероб):** Standart, Dragonclaw, Hook of the Sorrowful Prey (OTSP), Scorching Talon, Toy — у каждого меш + своя цепь (Niagara `Chain*_Niagara`, материалы `M_Chain`/`DK_Chain`).
- **Карта:** статичный меш `MapPW`, скалы `SM_Rock_Mountain_1`, руины `SM_Ruins_4`, деревья.
- **FX:** взрыв Puck (Cascade `P_ky_burstBall1`), цепи, маркер цели `Target_M`/`Target_M_Red`.
- **Звук:** хук (запуск/возврат/попадание/промах), реплики Pudge, серия убийств (First Blood → … → Holy Shit), Puck, Mirana, UI/магазин, музыка меню.
- **UI:** иконки предметов (Linza, Octarine, Lotar, Tp_1/Tp_2, Arrow, Aganim, Overgrowth + Gold-варианты), кулдауны `KD_*`, сенсорные джойстики (движение / хук / стрела), 8 рангов, фоны меню.

> ⚠️ Модели Pudge/Mirana/Treant и хуки — ассеты Dota 2 (Valve); игра — работа автора Ikrut. Для личного/учебного проекта — ок, для публикации в магазинах нужны свои модели и звук. Архитектура позволяет заменить их через `HeroDef`/`HookDef`.

## 3. Механики (по name map'ам Blueprint'ов)

✅ — видно явно, ❔ — вывод, требует проверки.

- **Управление:** два стика — левый движение, правый прицел + отпускание хука; отдельный стик для стрелы ✅. Камера на пружинной штанге, тряска (`Shake_Camera_PW`) ✅.
- **Хук:** полёт → пересечение → подтягивание цели; попадание = убийство (анимация `Death_Hook`) ✅ → респавн ✅. Промах — свой звук (`NePopal`) ✅. Упоминается стена (`Stena`) ❔ — отскок от стен или граница.
- **Параметры из CDO Blueprint'ов** ✅ (единицы UE — см; как именно скорости используются в графе, ❔):
  - `Player_PW` (одиночная): `DistanceHook` 1300, `HookStartSpeed` 1500, `HookReturnSpeed` 150, `HookReturnAttachSpeed` 45, `StartHook_DelayAnimations` 0.29 с (Toy — 0.4 с), `MaxWalkSpeed` 200.
  - `Player_Pudge` (сеть): `SpeedHookStart` 1000, `SpeedHookEnd` 3000, `SpeedHookPudge` 1200, `AnimDelay` 0.29 с, `MaxWalkSpeed` 300.
  - Капсула 61 × 82 см; камера: штанга 975.9 см, наклон −55.5°. Меш повёрнут в компоненте (Yaw −90, Roll 90), у Toy — Yaw −90 и масштаб 0.5.
  - Звуки хука: `Hook_Chain` → `Chain/Hook`, `Hook_ChainReturn` → `Chain/ReturnHook`, попадание → `PopalSound` + cue `Hook_Popodanie` (Pud_1/2/3, TargetHook), промах → cue `Returne_Sound` (Pud_anger, Pud_1). У Toy — свой набор (`Toy/*`).
- **Команды:** `Team` в PlayerState/GameMode, спавн по тегам, случайная точка ✅. Размер команд ❔.
- **Экономика матча:** золото за убийство (`Add_Gold_Kill`), внутриматчевый магазин, слот предмета ✅.
  - Linza — +дальность ❔
  - Octarine — снижение кулдауна ❔
  - Lotar — невидимость (полупрозрачные материалы `blinn*_Lotar`) ✅
  - Tp_1/Tp_2 — телепорт ❔
  - Aganim ❔
- **Доп. способности:** стрела Mirana (стан, `arrow_Stun`) ✅, Overgrowth (корни Treant) ✅, Puck (шар с возвратом `Puck_Return`, зарядка `PuckSpal`) ✅.
- **PlayerState:** имя, команда, убийства/хуки, смерти, золото, предметы, «покинул игру»; репликация через `OnRep_` ✅.
- **Прочее:** чат с быстрыми фразами, пауза, табло `TopInfoPlayers` ✅.
- **Боты:** `RandomReachablePointInRadius` + `MoveTo`, сфера обнаружения игрока → хук ✅.
- **Мета (SaveGame):** имя, ранг 1–8, опыт/уровень, счётчик Rampage, гардероб (герой, хук), рекорды, «База знаний» ✅.
- **Неизвестно:** кулдауны, цены, условия победы, точная семантика скоростей хука → разобрать графы Blueprint (байткод в `UberGraphFrame`/функциях) или замерить в оригинале.

## 4. Технологии Unity

Проект: Unity 6000.6, URP 17.6, Input System, AI Navigation, uGUI, Test Framework, Visual Studio (VS Code) — уже есть. Лишнее из шаблона удалено (Timeline, Visual Scripting, Version Control, Rider и неиспользуемые встроенные модули: Terrain, 2D-физика, Tilemap, Video, XR, Cloth, Vehicles, Wind, Umbra, Analytics, UnityWebRequest и др.); нужные зависимости Package Manager держит сам.

Добавить:

- **Mirror** (последняя версия с GitHub) + транспорт **KCP** (UDP)
- `NetworkDiscovery` (Mirror) — поиск игр в LAN
- Cinemachine 3 — камера, тряска
- On-Screen Stick (Input System) — джойстики

### Почему Mirror

- Встроенный поиск в LAN (`NetworkDiscovery`) и подключение по IP — ровно наши сценарии.
- Без внешних сервисов, MIT, зрелый, headless-сервер на Linux.
- Понятия почти 1:1 с UE:

| UE | Mirror |
|---|---|
| Replicated / `OnRep_` | `[SyncVar(hook = ...)]` |
| Server RPC (`Server_Linza`) | `[Command]` |
| Multicast (`Arrow_Multicast`) | `[ClientRpc]` |
| Listen-server | Host mode |
| Dedicated server | Server-only / headless build |

## 5. Сетевая архитектура

### Режимы

1. **Локальная сеть** — один игрок Host, остальные находят через `NetworkDiscovery` или вводят локальный IP.
2. **Интернет по IP** — headless-сервер (дома с белым IP или на VPS), подключение по IP/домену, порт `7777/UDP`.
3. **Одиночная игра с ботами** — тот же Host без подключений, боты — серверные игроки. Один код на все режимы (в оригинале логика раздвоена: `MenuGameLocal`/`MenuGame`, `HUD_PWLocal`/`HUD_Multiplayer`).
4. *(Позже, по необходимости)* relay-транспорт (Edgegap / EOS / Light Reflective Mirror), чтобы телефон мог хостить через интернет. Меняется только транспорт.

### Ограничение NAT

| Кто хостит | LAN | Интернет по IP |
|---|---|---|
| Телефон в Wi-Fi | ✅ | ❌ нужен проброс порта |
| Телефон на мобильном интернете | — | ❌ CGNAT оператора |
| ПК с белым IP / проброшенным портом | ✅ | ✅ |
| Выделенный сервер на VPS | — | ✅ |

### Сервер авторитетен

| UE (Blueprint) | Unity |
|---|---|
| `GameMode_PudgeWars`, `GM_PW` | `MatchManager` (сервер): команды, спавн, респавн, счёт, конец матча |
| `PlayerState_PudgeWars` | `PlayerStats : NetworkBehaviour` с `SyncVar` (Team, Kills, Deaths, Gold, Items) |
| `Player_Pudge`, `Player_PW` | префаб `Pudge`: `PudgeMotor`, `HookAbility`, `AbilityCaster`, `Inventory`, `Health/State` |
| `Arrow`, `Overgrowth`, Puck | `AbilityDef` (ScriptableObject) + серверные снаряды |
| `MagazinGame` | `ShopService` (покупка через `[Command]`) + `ItemDef` |
| AnimBP + BlendSpace1D | Сейчас: контроллер с состоянием на каждый клип + `PudgeAnimation` (CrossFade по имени). Позже: 1D blend tree Idle/Walk/Run + триггеры HookStart/End, Stun, Death, Spawn, Taunt |
| Niagara-цепь | ✅ LineRenderer с тайлингом текстуры `ZepStandart` (alpha clip, как `M_Chain`) |
| SoundCue (Random) | Audio Random Container |
| SaveGame | JSON в `persistentDataPath` |
| AIController | `BotBrain` на NavMesh, только сервер |

### Сетевая модель хука

1. Клиент сразу рисует предсказанный хук и шлёт `CmdCastHook(dir, clientTick)`.
2. Сервер проверяет кулдаун/состояние и создаёт компактное состояние `HookState {origin, dir, speed, range, startTick, phase, targetId}`. Клиенты считают позицию хука аналитически — трансформ каждый кадр не синхронизируется.
3. Попадание считает сервер с **компенсацией задержки**: история позиций игроков ~500 мс, откат целей на RTT/2 + задержку интерполяции стреляющего (своя реализация, ~150–200 строк).
4. Попадание → `phase = Pull`, цель в состоянии `Hooked`, позицию ведёт сервер вдоль цепи → у Pudge: убийство, золото, серия убийств, таймер респавна, `[ClientRpc]` для звука и FX.
5. Движение (MVP): владелец двигает себя сам, сервер проверяет скорость и границы зоны команды; остальные интерполируются (`NetworkTransform`). Позже при необходимости — серверное движение с предсказанием.
6. Невидимость (Lotar): скрывать объект от противников на уровне видимости сети (interest management), а не только рендером.
7. `sendRate` ~30 Гц.

## 6. Хостинг headless-сервера на домашнем ПК

1. Узнать WAN-IP в роутере (`http://192.168.0.1`) и сравнить с внешним IP (`curl -4 ifconfig.me` **с выключенным VPN**). Совпадают — белый IP; `100.64–127.x.x` / `10.x` / `172.16–31.x` / `192.168.x` — CGNAT.
2. Белый IP:
   - зарезервировать локальный IP ПК в DHCP роутера;
   - проброс `UDP 7777` → локальный IP ПК;
   - `sudo ufw allow 7777/udp` (если ufw включён);
   - DDNS (DuckDNS / No-IP / встроенный в роутер), т.к. IP скорее всего динамический;
   - сборка **Linux Dedicated Server**, запуск: `./PudgeServer.x86_64 -batchmode -nographics -port 7777`;
   - `NetworkManager.headlessStartMode = AutoStartServer`;
   - systemd-сервис для автозапуска и перезапуска.
3. CGNAT — варианты: купить белый IP у провайдера; туннель WireGuard через дешёвый VPS; playit.gg (UDP); Tailscale/ZeroTier (только для друзей). Ngrok и Cloudflare Tunnel не подходят (нет произвольного UDP).
4. **VPN на сервере ломает входящие подключения:** ответы уходят в `tun0` с чужим адресом. Решения:
   - выключить VPN / split tunneling для сервера;
   - или policy routing + привязка сервера к локальному адресу:
     ```bash
     sudo ip route add default via 192.168.0.1 dev wlo1 table 100
     sudo ip rule add from <локальный_IP_ПК> lookup 100
     ```
   - или держать сервер на VPS.
5. Изнутри домашней сети подключаться по локальному IP (NAT loopback на роутере может не работать).
6. Сервер — не от root; все `[Command]` валидировать на сервере (кулдауны, дальность, золото).
7. Перед сборкой игры проверить доступность порта тестовым UDP-эхо-сервером.

## 7. Этапы

### Этап 0 — полная распаковка ассетов
- CUE4Parse / FModel (UE 5.3): меши и скелеты → glTF/PSK, анимации, текстуры (ETC2 → PNG), звук → WAV.
- Дамп Blueprint'ов в JSON → числовые параметры и порядок логики.
- Blender: чистка, масштаб (UE — см, Unity — м), экспорт FBX → Unity (Humanoid/Generic).
- ✅ Пайплайн готов (`Tools/AssetExtraction`): Oodle — управляемый OodleSharp, `.usmap` генерируется из `libUnreal.so`. Перенесены Pudge, Toy Pudge (меши, анимации, текстуры, материалы), 5 хуков, текстура цепи. Параметры персонажа/хука/камеры/звуков взяты из CDO Blueprint'ов (раздел 3).
- ⏸ Звук: сырые BINKA выгружены в `Tools/AssetExtraction/SoundsRaw`; для WAV нужен `binkadec.exe` (Bink Audio 2), запуск через wine из PortProton уже настроен.
- ☐ Карта (`MapPW`, скалы, руины, деревья), FX (Puck, маркеры), UI-иконки, Mirana/Treant.
- ☐ Логика Blueprint (графы): кулдаун хука, отскок от стен, экономика.

### Этап 1 — оффлайн-прототип (2–3 недели)
- ✅ Тестовая арена (примитивы, 28×28 м, яма 6 м): две стороны, ограничение движения своей стороной.
- ✅ Pudge с движением и анимациями (Idle/Walk/Run, бросок и возврат хука, попадание, смерть, спавн), камера сверху.
- ✅ Движение и поворот как в Dota 2: скорость 280 → 2.8 м/с (1 юнит Dota = 1 см: дальность хука оригинала 1300 = Meat Hook), turn rate 0.7 рад / 0.03 с (180° ≈ 0.13 с); герой идёт и кастует, только когда цель в пределах 11.5° перед ним — сначала разворот на месте.
- ✅ Игрок и боты — префабы (`Pudge_Player`, `Pudge_Bot`): модель, маркер команды, голова хука и цепь внутри (при старте отсоединяются).
- ✅ Хук: задержка анимации 0.29 с, полёт 15 м/с на 13 м, захват первого врага, подтягивание 12 м/с; над ямой цель срывается, падает и умирает; счёт; респавн ботов. Цепь, маркеры команд.
- ✅ Перезарядка хука 1 с (значение-заглушка, оригинальный `Hook_KD` не разобран); попадание обнуляет её (`resetCooldownOnHit`).
- ✅ Боты: блуждают по своей стороне и кидают хук в ближайшего врага в радиусе: с вероятностью `leadChance` (0.5) — с упреждением по скорости цели, иначе в текущую позицию; ошибка ±7°, пауза 1.5–4 с. Пойманный персонаж теряет свой хук, его цель отпускается.
- ✅ Звуковые слоты хука по схеме Blueprint; ⏸ сами клипы отложены до появления `binkadec.exe`.
- ✅ Управление ПК: WASD + ЛКМ/Пробел (прицел курсором); клик по UI хук не бросает.
- ✅ Экономика (начало): `HeroInventory` (золото, 6 слотов), 250 золота за убийство (значение задано вручную, оригинальное `Add_Gold_Kill` не разобрано), `ItemDef` (ScriptableObject), магазин uGUI по B: Aether Lens — 2275, +225 к дальности (хук 13 → 15.25 м), не стакается.
- ✅ Попадание хука проверяется по всему пути за кадр (при просадке FPS хук не пролетает сквозь цель).
- ☐ Настоящая карта (меш + коллизии + NavMesh по зонам команд), двухстиковое мобильное управление, кулдаун в UI, маркер цели (`Target_M`), отскок хука от стен, Cinemachine, сложность ботов.

### Этап 2 — сеть (3–4 недели)
- Mirror + KCP, `NetworkManager` с режимами Host / Join по IP / поиск в LAN, лобби с выбором команды.
- Перенос хука на серверную модель (раздел 5), компенсация задержки, интерполяция.
- Табло, чат, выход и переподключение.
- Headless-сборка, запуск дома/на VPS.
- Тесты с искусственной задержкой и потерями пакетов, 2–4 клиента одновременно.
- Android: `WifiManager.MulticastLock` + разрешение `CHANGE_WIFI_MULTICAST_STATE`, иначе поиск в LAN не видит хост.

### Этап 3 — полный матч (2–3 недели)
- Золото, магазин, предметы (Linza, Octarine, Lotar, Tp, Aganim), стрела Mirana, Overgrowth, Puck.
- Серия убийств с озвучкой, условие победы, экран итогов.
- Боты как серверные игроки, режим «с ботами» через локальный Host.

### Этап 4 — мета и UI (2 недели)
- Главное меню, гардероб с пьедесталом (RenderTexture), скин Toy, скины хуков.
- Ранги и опыт, рекорды, «База знаний», пауза, настройки.

### Этап 5 — релиз (1–2 недели)
- Оптимизация под Android (URP Mobile, ETC2/ASTC, батчинг).
- Выделенный сервер в production (systemd или Docker на VPS).
- Аналитика; при необходимости — покупки и реклама.

## 8. Структура проекта Unity

Сейчас (✅ — есть):

```
Assets/_Project/
  Art/
    Characters/Pudge, PudgeToy   ✅ FBX, Animations/<Модель>@<клип>.fbx, Textures, Materials, .controller, .prefab
    Hooks/<скин>/                ✅ FBX, текстуры, материалы
    FX/Chain/                    ✅ текстура цепи
    Arena/Materials/             ✅ материалы тестовой арены и цепи
  Audio/Pudge/                   ⏸ WAV после декодирования BINKA (<Папка>_<Имя>.wav)
  Prefabs/Characters/            ✅ Pudge_Player, Pudge_Bot
  ScriptableObjects/Items/       ✅ AetherLens
  Art/UI/Items/                  ✅ иконки предметов
  Scenes/Arena.unity             ✅ тестовая арена (первая в Build Settings)
  Scripts/Gameplay/              ✅ сборка PudgeWars.Gameplay: Arena, Hookable, HookAbility, PudgeMotor,
                                    PudgeAnimation, PlayerInputController, BotBrain, TopDownCamera, ScoreHud
  Editor/                        ✅ PudgeAssetImporter (настройка импорта), ArenaSceneBuilder (сборка арены)
  Tests/PlayMode/                ✅ ArenaHookTests
Tools/AssetExtraction/           ✅ экстрактор, генератор .usmap, Blender-скрипты, SoundsRaw + sounds.py
```

Дальше (план):

```
Assets/_Project/
  Prefabs/        Projectiles, UI
  Scenes/         Boot, MainMenu, Lobby
  ScriptableObjects/  HeroDef, HookDef, AbilityDef, остальные ItemDef, MatchRules
  Scripts/
    Core/         конфиги, сохранения, сервисы
    Net/          NetworkManager, Discovery, Lobby, LagCompensation
    AI/           BotBrain (NavMesh, атака)
    UI/           HUD, Shop, Scoreboard, Chat, Menus
```
