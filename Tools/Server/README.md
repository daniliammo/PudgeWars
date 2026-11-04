# Linux Dedicated Server

Цель — **Linux x86_64 Server**, Unity **6000.6.0f1**, KCP/UDP. Сервер сам загружает
Boot → Arena и запускает матч, свободные места занимают боты. Меню не загружается,
GPU-прогрев и клиентские сервисы интерфейса/звука отключены. При обработке сцен для
серверной сборки удаляются HUD, камеры, свет и офлайн-герои; TerrainData, препятствия,
коллайдеры, сетевые герои и серверная симуляция сохраняются. Тела-рэгдоллы — локальный
эффект клиентов, на сервере они не создаются. Сохранённые сцены не изменяются.

## Сборка

В Unity Hub нужен модуль **Linux Dedicated Server Build Support** для 6000.6.0f1.
На текущей машине он установлен. Закрыть редактор, открывший этот проект, затем
из корня проекта выполнить:

```bash
~/Unity/Hub/Editor/6000.6.0f1/Editor/Unity \
  -batchmode -nographics -quit -projectPath "$PWD" \
  -buildTarget Linux64 -standaloneBuildSubtarget Server \
  -executeMethod PudgeWars.EditorTools.CiBuild.BuildLinuxServer \
  -ciOutputPath Build/LinuxServer/PudgeWarsServer.x86_64 \
  -logFile /tmp/pudgewars-linux-server-build.log
cp Tools/Server/run-server.sh Tools/Server/pudgewars.service Tools/Server/README.md Build/LinuxServer/
chmod +x Build/LinuxServer/PudgeWarsServer.x86_64 Build/LinuxServer/run-server.sh
```

Метод сборки явно устанавливает `StandaloneBuildSubtarget.Server`. Пересобирать сцены
пунктами Build Test Arena/Build Main Menu не требуется. В сборку входят сохранённые
Boot и Arena. `CiBuild.Build` продолжает собирать обычные игровые клиенты.
Способ выбора серверной цели соответствует [документации Unity](https://docs.unity.com/en-us/engine/6000.5/manual/platform-specific/dedicated-server/build).

GitHub Actions после успешных PlayMode-тестов собирает отдельный artifact
`linux-server-x86_64`: архив `PudgeWars-linux-server-x86_64.tar.gz`, включая файлы
запуска. CI запускает настоящий бинарник без графики: ждёт готовности арены с шестью
героями, затем ещё три секунды проверяет, что процесс жив и в логе нет исключений.
Проверка не заменяет подключение реального клиента.

## Запуск

Распаковать **весь** архив на Linux x86_64. Рядом с executable должны остаться
`PudgeWarsServer_Data`, `UnityPlayer.so`, `MonoBleedingEdge` и остальные файлы билда.
Запускать под обычным пользователем с доступным для записи HOME.

```bash
tar -xzf PudgeWars-linux-server-x86_64.tar.gz -C /path/to/server
cd /path/to/server
./run-server.sh -port 7777 -teamSize 3 -bots 1 -botDifficulty 1
```

| Аргумент | По умолчанию | Значения |
| --- | --- | --- |
| `-port` | `7777` | UDP-порт, 1–65535 |
| `-teamSize` | `3` | Героев на каждой стороне, 1–5 |
| `-bots` | `1` | `1` — заполнять ботами, `0` — без ботов |
| `-botDifficulty` | `1` | 0.25–3, дробная часть через точку |

Число подключений ограничено `2 × teamSize`. Клиенты распределяются по командам,
замещают ботов, а после отключения место снова занимает бот. Лобби на сервере нет,
поэтому смена команды через лобби во время этого матча недоступна. Сетевой обмен —
60 отправок в секунду; серверная частота кадров ограничивается этим же значением.
Неверные аргументы завершают процесс с кодом `2`; ошибка старта — с кодом `1`.

В логе должна появиться строка:

```text
[Server] Ready: scene=Arena, UDP port=7777, heroes=6
```

В клиенте: в меню «Подключиться» ввести `IP_СЕРВЕРА:7777`. Нужна клиентская сборка
из той же версии проекта с тем же каталогом предметов. Разрешить выбранный **UDP**
порт в firewall сервера и правилах облака; для сервера за роутером пробросить этот
UDP-порт. Открывать TCP 7777 для KCP не требуется. Поиск LAN работает только в
локальной сети; для сервера в интернете использовать IP.

Логи идут в stdout (`-logFile -`); остановка — Ctrl+C или SIGTERM. Для записи в файл
передать в конце `-logFile /path/to/Player.log`. Проверка готового билда вручную:

```bash
python3 Tools/Server/smoke_test.py Build/LinuxServer/PudgeWarsServer.x86_64
```

## systemd

Скопировать билд в `/opt/pudgewars`, создать отдельного пользователя и установить
unit. Команды выполняются администратором **на целевом сервере**:

```bash
sudo useradd --system --user-group --home-dir /var/lib/pudgewars --shell /usr/sbin/nologin pudgewars
sudo install -m 644 /opt/pudgewars/pudgewars.service /etc/systemd/system/pudgewars.service
sudo systemctl daemon-reload
sudo systemctl enable --now pudgewars
sudo journalctl -u pudgewars -f
```

Пользователю `pudgewars` нужны права чтения файлов и выполнения скрипта/бинарника.
systemd создаёт доступный для записи `/var/lib/pudgewars`, туда направлен HOME.
Для изменения порта/команд/ботов отредактировать `ExecStart` установленного unit,
затем `daemon-reload` и `restart pudgewars`. При сбое сервер перезапускается;
код `2` означает неправильные настройки и исключён из автоматических перезапусков.

## Проверки перед выкладкой

В Test Runner: `ServerOptionsTests` (аргументы), `NetworkTests` (включая сервер без
локального клиента), `MenuTests` (обычный клиент). После сборки — smoke test выше
и подключение отдельного клиента по IP: движение, хук, урон, смерть/возрождение,
покупка предметов, отключение/повторный вход. Серверный билд должен стартовать без
DISPLAY и без графического устройства.
