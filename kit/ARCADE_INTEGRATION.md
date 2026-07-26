# Интеграция игры в аркадный автомат

> **Агент, читай этот файл в начале КАЖДОЙ сессии в этом репозитории и не нарушай
> его пункты.** Игра поедет в общий Unity-проект аркадного автомата вместе с
> шестью другими. Внутри своей игровой логики ты свободен полностью. Ограничения —
> только на границе интеграции. Эту границу проверяет `contract_check.py` на каждый
> push и PR (GitHub Actions, без Unity-лицензии): красный контракт — PR не мёржится.
> Не «чини» контракт правкой самого чек-скрипта или тестов — эталон живёт в
> репозитории автомата (`arcade-hub`), расхождение всплывёт при интеграции.

Этот файл и три соседних (`contract_check.py`, `arcade-kit.json`,
`.github/workflows/arcade-contract.yml`) — **интеграционный кит**. Он приносится в
репозиторий игры из `arcade-hub/kit/` и обновляется оттуда же.

---

## 1. Версии и рендер

- Unity **6000.5.x** (целевая версия автомата — 6000.5.3f1).
- Рендер — **URP** (Built-in не подходит: пайплайн в общем проекте один на всех).
  2D-игры используют 2D Renderer; 2.5D/3D — Universal (3D) Renderer из списка
  рендереров URP-ассета. Камера игры выбирает свой рендерер; смешивать типы
  рендереров в одной камере/стеке нельзя (игры работают по очереди).

## 2. Упаковка: игра = один самодостаточный пакет

- Весь контент игры — внутри **одной корневой папки** (например `Assets/<Game>/`):
  скрипты, сцены, префабы, арт, звук, тесты.
- В корне этой папки — **`package.json`** (UPM-манифест). `name` начинается с
  `com.aigamestudio.game-` (автомат подключает игру как пакет по git URL).
- Скрипты собираются **своим `asmdef`** (+ желателен свой namespace). Никакого кода
  в глобальной `Assembly-CSharp`.
- **Ничего игрового вне этой папки.** Никаких молчаливых правок глобальных настроек
  проекта (tags, layers, физика, качество, Input) — если что-то нужно, это
  согласуется с хабом (issue/PR), а не кладётся тихо. `Resources/`, если нужен, —
  только с префиксом игры в путях (коллизии имён между играми).

## 3. Манифест игры — `game.json`

В корне игровой папки, рядом с `package.json`:

```json
{
  "name": "Lady Bug",
  "version": "0.1.0",
  "entryScene": "Assets/LadyBug/Scenes/Main.unity",
  "controls": ["HeightA", "HeightB"],
  "launcherRain": null
}
```

- `name`, `version` — человекочитаемые название и версия.
- `entryScene` — путь entry-сцены от корня Unity-проекта. Сцена **обязана**
  существовать и быть включённой в `EditorBuildSettings.asset` (Build Settings).
- `controls` — логические контролы автомата, которые игра использует в геймплее
  (см. §4). Это НЕ маппинг запуска (каким контролом игру заводят в лаунчере) —
  маппинг живёт в хабе.
- `launcherRain` — опциональный объект-«дождь» для экрана зарядки лаунчера
  (можно `null`).

## 4. Управление — только через `arcade-controls`

Пакет `arcade-controls` даёт 8 логических контролов автомата: `Crank`, `RedButton`,
`GreenButton`, `BangButton`, `HeightA`, `HeightB`, `Joystick`, `MenuButton`.

- Игровой код читает ввод **только** через `ArcadeInput.*`
  (`ArcadeInput.HeightA.Value`, `ArcadeInput.RedButton.IsHeld`,
  `ArcadeInput.Joystick.Vector`, событие `ArcadeInput.MenuButton.Pressed`).
- **Прямые обращения к вводу запрещены** и ловятся чек-скриптом:
  - **любой** член `UnityEngine.Input` (`Input.GetKey*`, `Input.GetAxis*`,
    `Input.GetButton*`, `Input.GetMouseButton*`, `Input.mousePosition`,
    `Input.touchCount`, `Input.acceleration` и т.д.);
  - **любое** использование `UnityEngine.InputSystem` (в т.ч. сама `using`-директива)
    и device-акцессоры `Keyboard.current`, `Mouse.current`, `Gamepad.current`,
    `Touchscreen.current`, `Pointer.current`, `Joystick.current`;
  - `using`-алиасы вида `using X = UnityEngine.Input;` — нарушение сами по себе;
  - пробельные трюки (`Input . GetKey`) не помогают — скан нормализует whitespace.

  На время разработки без железа пакет даёт клавиатурную симуляцию — маппинг клавиш
  в его конфиге, менять игровой код при смене бэкенда не нужно.

> Легаси-долг: если репозиторий ещё не мигрирован на `arcade-controls`, файлы с
> сырым вводом перечислены в `rawInputAllowlist` в `arcade-kit.json` **с фиксацией
> baseline**: у каждого файла записано текущее число строк с сырым вводом
> (`baselineCount`). Проверка падает, как только строк становится БОЛЬШЕ baseline —
> долг заморожен, а не прощён: новый сырой ввод нельзя добавить даже в легаси-файл.
> Стало меньше (мигрировал часть) — понизь `baselineCount`. Не добавляй файлы в
> allowlist, чтобы «протолкнуть» PR — мигрируй ввод на `ArcadeInput`.

## 5. Жизненный цикл в автомате

- Игра стартует загрузкой её entry-сцены лаунчером. Никакой логики «я единственная
  сцена в билде», никаких `Application.Quit()`.
- По `ArcadeInput.MenuButton.Pressed` игра обязана корректно завершиться: остановить
  корутины/таймеры, не оставлять `DontDestroyOnLoad`-объектов и статического
  состояния, мешающего повторному запуску. Повторная загрузка entry-сцены = чистый
  новый запуск.

## 6. Репозиторий всегда запускаем

- Свежий `git clone` открывается в Unity и играется: все сцены, префабы, ассеты и
  `.meta` закоммичены; entry-сцена — в Build Settings.
- `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/` — в `.gitignore` (не трекать).
- Обновления для автомата публикуются **git-тегами** (автомат пинует версию тега).

---

## 7. Что проверяет `contract_check.py` (без Unity, без лицензии)

Конфиг — `arcade-kit.json` в корне репозитория:

```json
{
  "unityProjectPath": ".",
  "gameFolder": "Assets/YourGame",
  "packageNamePrefix": "com.aigamestudio.game-",
  "rawInputAllowlist": [
    { "file": "Scripts/LegacyThing.cs", "baselineCount": 3 }
  ],
  "assetWhitelist": ["DefaultVolumeProfile.asset",
                     "UniversalRenderPipelineGlobalSettings.asset",
                     "Settings", "StreamingAssets"]
}
```

- `unityProjectPath` — путь от корня репо до корня Unity-проекта (`.` если проект
  лежит в корне; `UnityProject` если во вложенной папке).
- `gameFolder` — путь от корня Unity-проекта до корневой папки игры.
- `rawInputAllowlist` — легаси-файлы с сырым вводом (пути относительно
  `gameFolder`); `baselineCount` = замороженное число строк с сырым вводом
  (семантика — §4). Больше baseline → красный.
- `assetWhitelist` — что разрешено лежать прямо под `Assets/` вне папки игры.

Проверки (буквы соответствуют инкремент-спеке):

- **(a)** папка игры содержит валидный `package.json`: `name` начинается на
  `packageNamePrefix`, `version` — semver-подобный, `displayName` непустой.
- **(b)** `game.json` валиден; `entryScene` существует на диске и включён в
  `EditorBuildSettings.asset`; `controls` — массив строк из множества
  {Crank, RedButton, GreenButton, BangButton, HeightA, HeightB, Joystick,
  MenuButton}.
- **(c)** под папкой игры есть хотя бы один `.asmdef`.
- **(d)** в `.cs` игры нет сырого ввода мимо `ArcadeInput` (полный список — §4);
  легаси-файлы из `rawInputAllowlist` не могут вырасти выше `baselineCount`.
- **(e)** под `Assets/` нет игровых ассетов вне папки игры (кроме `assetWhitelist`).
- **(f)** `Library/`, `Temp/`, `Obj/` не затрекана git-ом.

Запуск локально: `python3 contract_check.py` из корня репо (нужен только python3,
stdlib; для проверки (f) — git). Exit 0 = зелёно, exit 1 = красно с перечнем
нарушений.

## 8. CI

`.github/workflows/arcade-contract.yml` гоняет `contract_check.py` на `ubuntu-latest`
на каждый push и PR. Unity-лицензия не нужна — это чисто файловые проверки.

### Опционально: полный CI с Unity-тестами (GameCI)

Файловый чек не запускает Unity, поэтому не гоняет контрактные/собственные
EditMode-тесты. Когда захочешь полный CI:

1. Заведи Unity Personal/Pro лицензию и добавь секрет репозитория **`UNITY_LICENSE`**
   (контент `.ulf`-файла; активацию см. в доке GameCI). Для Pro/Plus — плюс
   `UNITY_EMAIL` и `UNITY_PASSWORD`.
2. Добавь в workflow ступень на базе GameCI `game-ci/unity-test-runner` (образ под
   `6000.5.3f1`), которая прогоняет EditMode-тесты (в т.ч. контрактные из
   `Tests/Integration/`, когда они появятся) и собственные тесты игры.
3. Эта ступень тяжёлая (тянет Unity-образ) — держи её отдельной job'ой; быстрый
   файловый `contract-check` остаётся обязательным гейтом на каждый push, а
   Unity-ступень включается по готовности лицензии.

Пока `UNITY_LICENSE` не заведён, кит останавливается на файловом контракте — этого
достаточно, чтобы не пропускать структурно-ломающие PR-ы.
