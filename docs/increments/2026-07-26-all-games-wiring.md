# Increment: all-games-wiring («зашить все игры»)

Status: GATE 1 APPROVED (раскладка основательницы 2026-07-26; приоритет «зашить все,
тонкая настройка контролов в геймплее — позже»).

## Что делаем
1. Хаб подключает ТРИ внешние игры file:-зависимостями на локальные клоны веток:
   Сизиф (external/EndlessSisyphusUnity, ветка arcade-input), Lady Bug
   (external/lady_bug, arcade-contract-repack), Factory (external/factory_game,
   arcade-contract-repack). Сизифу и Factory добавить в их ветки минимальные
   package.json+game.json (kit-паттерн lady_bug) отдельными коммитами.
2. launcher-config.json — раскладка основательницы: Crank=Сизиф(installed),
   Red=Factory(installed), Green=LifeChoices(installed), Bang=ArcadePrototype(soon),
   Joystick=Медитация(soon), HeightA=LadyBug(installed), HeightB=HomeAlone(installed).
   TestGame из конфига убрать (сцена и тест-риг остаются).
3. Тех-долг совместимости: lady_bug на старом Input Manager → хабу
   activeInputHandler=Both; управление внешних игр пока РОДНОЕ клавиатурное
   (перевод на ArcadeInput — отдельные инкременты позже, решение основательницы).
   Возврат в меню везде через LauncherReturn-сторожок.
4. Сцены игр в Build Settings; Сизиф код-генерённый — его Main пустая, вход через
   Bootstrap (проверить, что грузится из лаунчера).
5. Тесты: config-тест на новую раскладку (5 installed/2 soon, без TestGame);
   PlayMode: вход/возврат для каждой installed (5 циклов); hub-v0 инстант-селект
   тесты обновить под новую раскладку или снять, если инстант-селект мешает —
   зафиксировать решение в §прогресса.
6. Скриншоты: меню 5×[PLAY] + по одному из каждой новой игры, запущенной из хаба.

## Done-контракт
1. 5 installed-игр запускаются из лаунчера и возвращаются (PlayMode); 2. config
соответствует раскладке; 3. suite хаба зелёный; 4. скриншоты проверены Maintainer;
5. ветки Сизифа/Factory дополнены манифестами (их батчи зелёные); 6. гейт 2 —
плейтест основательницы.

## Прогресс / решения (owner, 2026-07-26)

Реализация зашита; батч-прогоны зелёные (EditMode 40/40, PlayMode 11/11). Финальный
suite — за Maintainer.

**Раскладка (launcher-config.json).** 7 слотов в порядке основательницы: Crank=Endless
Sisyphus, Red=«Последняя смена (Factory)», Green=Life Choices, Bang=Arcade Prototype
(soon), Joystick=Медитация (soon), HeightA=Lady Bug, HeightB=Home Alone. TestGame из
конфига убран (сцена+риг остаются: их гоняет HubFlow через инъекцию конфига).

**file:-пакеты (hub manifest.json).** Три внешние игры подключены file:-зависимостями на
локальные клоны веток. Сцены игр — в Build Settings по package-путям (Sisyphus Main,
Factory Boot+3 уровня, Lady Bug Main; Home Alone Apartment и Life Choices ThanksNoThanks
уже были).

**Коллизия имён сцен «Main».** У Сизифа и Lady Bug сцена называется одинаково — «Main».
`SceneManager.LoadScene("Main")` был бы неоднозначен, поэтому в конфиге их entryScene —
полный package-путь (`Packages/com.aigamestudio.game-*/Scenes/Main`), а не короткое имя.

**Сизиф (код-генерённый) — правка на его ветке (arcade-input).** Его `Bootstrap` через
одноразовый `RuntimeInitializeOnLoadMethod` (а) построил бы игру поверх меню лаунчера на
старте плей-мода и (б) НЕ построил бы её, когда сцену грузят из лаунчера позже. Bootstrap
переведён на подписку `sceneLoaded` + гейт «строить только в своей сцене» (путь содержит
`sisyphus`, регистронезависимо — устойчиво к package-пути `endless-sisyphus`). Плюс
добавлен `EndlessSisyphus.Runtime.asmdef` (без него скрипты пакета не компилируются в
проекте-потребителе). Это выходит за рамки «только манифесты» — вынесено в отдельный
коммит и требует внимания основательницы/Maintainer. Factory-код НЕ трогали.

**Совместимость ввода.** `activeInputHandler=2` (Both) — для Lady Bug на старом Input
Manager (единственная разрешённая правка ProjectSettings сверх Build Settings). Внешние
игры пока играются РОДНЫМ вводом.

**Возврат в меню для «родного-ввода» игр (nativeInput).** Sisyphus / Home Alone / Life
Choices сами качают ArcadeInput (их сцены/адаптеры зовут `ArcadeInput.Update`), поэтому
сторожок `LauncherReturn` видит их MenuButton. Lady Bug (старый Input) и Factory (сырой
новый Input System) ArcadeInput НЕ качают — сторожок читал бы замороженный MenuButton.
Решение: в конфиг добавлен флаг `nativeInput`, и для таких слотов `LauncherReturn.ArmFor`
несёт собственный `ArcadeInputRunner` (DontDestroyOnLoad, единственный качальщик в сцене
такой игры). ArcadeInput-native игры флага не имеют — их нельзя качать дважды.

**Esc-конфликт Factory (разбор).** Factory на новом Input System читает Esc сам:
`GameInput.EscapePressed` → `LevelManager` ставит паузу (`SetPaused(true)`); сторожок
хаба на том же Esc (через MenuButton, после «увиденного» релиза) грузит HubMenu. При
реальном нажатии Esc в Factory оба срабатывают в одном кадре, но `LauncherReturn` делает
`LoadScene(HubMenu)` и сносит сцену Factory — возврат в хаб побеждает, пауза Factory
шунтируется. Для v0 это ОК (приоритет — «зашить все + возврат везде»; их код не трогаем;
тонкая раскладка/пер-игровая пауза — позже). В хабе ничего под это не подкручивали.

**Инстант-селект ВЫПИЛЕН (гейт 2, решение основательницы 2026-07).** Вживую: нажатие Э (Red)
мгновенно запускало ПОДСВЕЧЕННУЮ строку (курсор по умолчанию — Сизиф) вместо зарядки слота
Factory. Решение закрыло отложенный вопрос сосуществования: запуск ТОЛЬКО через
hold-to-launch — любой контрол (включая Red) заряжает свой слот ~5 с. Джойстик остаётся
навигацией; подсветка строки — чисто информационная. Из `HubMenuController` удалён Red-select
путь целиком (Choose, placeholder «COMING SOON» — фидбек planned-слота теперь только «СКОРО»
оверлей hold-to-launch); подсказка в шапке: «Джойстик вверх/вниз — листать • зажми контрол
своей игры на 5 сек, чтобы запустить • MENU (Esc) — выход». Тесты: входы во все 5 игр
переведены на зарядку родным контролом слота; новый регрессионный тест
`RedPress_DoesNotInstantLaunch_ItChargesTheFactorySlot` (ровно сценарий бага: Red ~1 с →
ничего не запускается мгновенно, заряжается слот 1/Factory, отпустил — распад в 0).
TestGame-риг: его цикл переведён на hold-путь — конфиг с TestGame-на-Crank инжектится в
НАСТОЯЩИЙ `HoldToLaunchController` сцены (`InitializeWith`), кран до полного → запуск, MENU →
возврат (решение: рига-специфичного механизма запуска больше нет, один путь для всех).
ARCHITECTURE.md обновлён (меню = навигация, hold-to-launch = единственный путь, стейдж ④).

**DDOL-janitor хаба (фикс по скептику, major 2).** Factory паркует в DDOL свои синглтоны:
`GameManager` (Boot → Ensure) и `ArduinoInputBridge` (ГЛОБАЛЬНЫЙ RuntimeInitializeOnLoadMethod —
спавнится в любой стартовой сцене, включая наше меню). Их код не трогаем. Решение на стороне
хаба: `HubDdolJanitor` (Scripts/Hub) вызывается из `HubMenuController.Start` при КАЖДОМ входе
в меню (RuntimeInit отрабатывает до Start — первый запуск тоже накрыт) и сносит из DDOL-сцены
корневые объекты с MonoBehaviour вне белого списка неймспейсов (`AiGameStudio*`, `Unity*`
вкл. test-runner, `TMPro`); скрипт без неймспейса в DDOL = чужак. Повторный вход в Factory
работает — их Boot зовёт `GameManager.Ensure()`. Нюанс: `ArduinoInputBridge` после сноса не
пересоздастся (одноразовый RuntimeInit), но клавиатурный путь Factory (`GameInput`) от него
не зависит — деградирует только необязательный Arduino-мост; для кабинета v0 внешние игры и
так на родной клавиатуре. Тест: цикл Factory→меню → в DDOL нет LastShift-объектов, повторный
вход живой, выход снова через сторожок.

**Честные nativeInput-тесты возврата (фикс по скептику, major 1).** Раньше тест убивал ВСЕ
`ArcadeInputRunner` (включая насос сторожка) и качал `ArcadeInput.Update` сам — зелёный тест
не доказывал возврат Lady Bug/Factory в продакшен-режиме. Теперь для nativeInput-игр exit идёт
через `ExitToMenuViaWatchdogRunner`: тест подменяет ТОЛЬКО бэкенд на FakeBackend, а прокачку
делает живой runner на объекте `LauncherReturn` (ровно продакшен-путь; тест ассертит его
наличие и не зовёт `ArcadeInput.Update` сам). `TakeOverInput` теперь принципиально не трогает
runner сторожка. Для ArcadeInput-нативных игр (Sisyphus/Home Alone/Life Choices) — как было:
тест качает сам, замещая насос самой игры.

**Известные мелочи v0.** Серийный ридер Factory деградирует без железа. Скриншоты (0 мадженты,
не пустые): hub-menu-5play, hub-into-sisyphus, hub-into-factory, hub-into-ladybug
(+ homealone/lifechoices) в scratchpad.
