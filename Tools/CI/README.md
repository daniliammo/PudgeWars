# GitHub Actions: тесты и сборки Unity

Workflow: `.github/workflows/unity-ci.yml`. Версия редактора берётся из
`ProjectSettings/ProjectVersion.txt` (сейчас 6000.6.0f1). CI собирает сохранённые в Git
Boot → Menu → Arena; сборщики сцен и внешние исходники Dota/Blender в CI не требуются.

## Однократная настройка Unity Personal

Открыть [секреты репозитория](https://github.com/daniliammo/PudgeWars/settings/secrets/actions)
и добавить **Repository secrets**:

| Имя | Значение |
| --- | --- |
| `UNITY_LICENSE` | Содержимое файла лицензии `.ulf`, полученного после активации Personal через Unity Hub |
| `UNITY_EMAIL` | Адрес Unity ID, которым активирована лицензия |
| `UNITY_PASSWORD` | Пароль этого Unity ID |

Значения вводятся только в GitHub Secrets, не в YAML, Git, логи или чат.
На Linux стандартное расположение файла —
`~/.local/share/unity3d/Unity/Unity_lic.ulf`; на macOS —
`/Library/Application Support/Unity/Unity_lic.ulf`.

Следовать актуальной [инструкции активации GameCI](https://game.ci/docs/github/activation/).
Unity Personal активируется через Hub. Наличие лицензии в интерфейсе Hub ещё не означает,
что он создал `.ulf`: если файла нет, не подставлять токены Hub вместо него и не рассчитывать
на старую ручную активацию `.alf` через сайт. Сначала проверить поддерживаемый способ
активации для этой версии Hub/Unity. Если GameCI не удаётся активировать с текущим Personal,
альтернатива — собственный runner с редактором, уже активированным через Hub; этот workflow
настроен для GitHub-hosted runners и требует успешной активации GameCI.

Для репозитория должны быть разрешены GitHub Actions и используемые сторонние actions.
Workflow уже использует минимальные `contents: read`; собственный PAT не нужен.

## Что запускается

- Push в `main`: полный PlayMode-прогон, затем сборки трёх платформ.
- Pull request из ветки этого же репозитория: полный PlayMode-прогон.
- Actions → **Unity tests and builds** → **Run workflow**: тесты и все сборки выбранной ветки.

Fork PR не получает секреты, поэтому его Unity job пропускается. Для проверки внешнего
вклада сначала просмотреть изменения, перенести их в доверенную ветку этого репозитория
и запустить workflow. `pull_request_target` с запуском чужого кода не используется.

Тесты выполняются в Linux-контейнере GameCI с OpenGL, без `-nographics`. Графика на hosted
runner программная; она проверяет код/отрисовку, но не производительность конкретного GPU.
`check_test_results.py` дополнительно требует непустой NUnit XML, отсутствие Failed/Skipped
и наличие тестов загрузки, GPU-прогрева и текста. Отсутствующий графический контекст,
неподдержанный рендер-запрос или отсутствующая сцена не превращают пропущенный тест в успех.
Редакторские PlayMode-тесты не запускают Android/macOS приложение на устройстве.

Результаты тестов и логи доступны в artifact `playmode-results`, даже при падении.
Сборки стартуют только после успешного тестового job; build matrix выполняется последовательно,
чтобы её jobs не активировали одну Personal лицензию одновременно. Внутри каждой платформы
используется собственный кэш Library. Ненужные SDK на Linux-хосте удаляются до загрузки контейнера;
Android SDK/NDK/JDK, необходимые Unity, находятся внутри образа GameCI.

## Результаты сборок

| Artifact | Runner | Содержимое |
| --- | --- | --- |
| `android-apk` | Ubuntu 24.04 | `PudgeWars.apk` |
| `linux-x86_64` | Ubuntu 24.04 | `PudgeWars-linux-x86_64.tar.gz` с игрой и исполняемыми правами |
| `macos` | macOS 15 Intel | `PudgeWars-macos.zip` с `.app`, упакованным через ditto |

Это обычные игровые клиенты, не Dedicated Server. `CiBuild.Build` явно выбирает Player,
проверяет сцены и результат BuildPipeline; Mono, архитектуры Android, качество и материалы
сохраняются из проекта. macOS архитектура берётся из Player Settings. Текущая Android
конфигурация проекта — Mono/ARMv7; переход на ARM64/IL2CPP здесь не производится.
Сборка не публикует релиз, не загружает приложение в магазин и не делает git push.
Artifacts хранятся 7 дней. Для долгого хранения скачать результат или отдельно настроить Releases.

APK использует стандартную debug-подпись Unity, если в проекте не задан свой keystore.
Это сборка для проверки, не для Google Play; ключ на новом runner может отличаться от
ключа установленной версии. Стабильный Android keystore и Apple Developer signing/notarization
в этот workflow не добавлены. macOS `.app` не нотарифицирован; Gatekeeper может потребовать
разрешить открытие приложения. Успешная компиляция не заменяет проверку запуска на телефоне/Mac.

## Первый запуск и диагностика

После коммита/push workflow появится во вкладке Actions. Добавить секреты, затем выполнить
Run workflow. Никаких секретов автоматически с рабочего компьютера не копируется.
Первая загрузка редактора/импорт заметно дольше последующих запусков. Нужны доступные
GameCI-образы именно для версии из ProjectVersion.txt и доступные Unity модули платформ.

- `Missing repository secret`: добавить указанный secret в настройках Actions.
- Ошибка активации: проверить Unity ID, содержимое `.ulf` и актуальные ограничения Personal;
  пароль/лицензию не вставлять в issue или публичный лог.
- Docker manifest not found: проверить, опубликован ли GameCI образ для точной версии Unity.
- `No space left on device`: проверить место на runner и размер кэшей Library.
- NUnit XML содержит Failed/Skipped: смотреть `playmode-results`; сборки в этом случае не запускаются.
- Ошибка компиляции/модуля платформы: смотреть лог Unity; не менять версию редактора автоматически.

Локальная проверка кода сборщика без закрытия редактора:
`python3 Tools/Check/compile.py /tmp/pudgewars-ci-check`.
Сам workflow, активация и три сборки проверяются только реальным запуском GitHub Actions.

Источники: [GameCI — тесты](https://game.ci/docs/github/test-runner/),
[GameCI — сборки](https://game.ci/docs/github/builder/),
[Unity — активация](https://docs.unity.com/en-us/engine/6000.6/manual/get-started/install-and-upgrade/licenses-and-activation),
[GitHub — hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
