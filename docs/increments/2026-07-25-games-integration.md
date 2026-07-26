# Increment: games-integration (этап ② финал)

Status: LANDED (см. хеш в handoff; гейт 2 основательницы «ок, коммить всё» 2026-07-26).
стартует ТОЛЬКО после того, как arcade-packaging(home-alone), arcade-packaging
(life-choices) и hold-to-launch(hub) пройдут скептиков. Гейт 2 утром.

## Что делаем
1. Хаб подключает пакеты обеих игр file:-зависимостями (пути их Assets/_Project).
2. launcher-config.json: слоты home-alone и life-choices → installed, entry-сцены
   из их game.json; сцены игр в Build Settings хаба.
3. Hold-to-launch запускает их по-настоящему; MenuButton возвращает в меню
   (контрактный чистый выход уже реализован упаковкой).
4. Тесты: PlayMode — вход из меню в каждую игру и чистый возврат (по циклу
   TestGame-теста), config-тест на 3 installed-слота.

## Done-контракт
1. Обе игры запускаются из шкалы и возвращаются по MenuButton (PlayMode-тесты);
2. suite хаба зелёный целиком; 3. скриншоты меню с 3×[PLAY] и каждой игры,
запущенной из хаба (проверяет Maintainer); 4. гейт 2 утром: один заход —
шкала → игра → Esc → шкала → вторая игра.

## Прогресс (owner, ночной прогон 2026-07-25)

Статус: **сделано, ждёт машинного гейта Maintainer-а** (в arcade-hub НЕ коммичу).
Построено ПОВЕРХ hold-to-launch без изменения его логики; hub-v0 контракт цел.

**Пакеты (Packages/manifest.json):** две file:-зависимости на пакеты игр
(`...game-home-alone` → `home-alone/.../Assets/_Project`, `...game-life-choices` →
`life-choices/.../Assets/_Project`). Batch-резолв 6000.5.3f1 — чистый: обе игры
подключились, arcade-controls резолвится ОДНОЙ общей копией (нет конфликта версий;
направление «пакет 6000.5.1 старше хоста 6000.5.3» безопасно), компиляция без
ошибок (game runtime asmdef'ы тянут только Unity.InputSystem/UGUI/ArcadeControls —
всё есть в хабе). Тесты игр НЕ подтянуты (пакеты не в `testables`).

**Build Settings:** обе entry-сцены добавлены по package-путям после HubMenu/TestGame
(`Packages/com.aigamestudio.game-.../Scenes/{Apartment,ThanksNoThanks}.unity`) —
idempotent-скриптом `HubEditorTools.AddGameScenesToBuildSettings`. Итого 4 сцены.

**launcher-config.json:** Home Alone (RedButton) → installed `Apartment`; Life Choices
(GreenButton) → installed `ThanksNoThanks`; Test Game (Crank) остался installed. 3
installed-слота, остальные planned.

**Возврат в лаунчер (`LauncherReturn.cs`):** упакованные игры по MenuButton
сбрасываются НА МЕСТЕ (свой standalone-контракт: freeze, без DontDestroyOnLoad/статик),
но НЕ знают сцену меню хаба — поэтому возврат берёт на себя ЛАУНЧЕР. Перед загрузкой
внешней игры армится watchdog (DontDestroyOnLoad, читает только ArcadeInput.MenuButton),
жест выхода = «увиденное отпускание + кадр прогрева, затем свежее нажатие» (совпадает с
собственным контрактом home-alone; для life-choices — обычный edge — тоже проходит).
Армится из обоих путей запуска (Fire + Choose), для self-returning TestGame — пропуск,
поэтому hub-v0 PlayMode-цикл не тронут.

**Тесты (headless 6000.5.3f1):**
- EditMode 40/40 (было 39 + новый config-тест: shipped-конфиг = ровно 3 installed с
  entry-сценами Apartment/ThanksNoThanks).
- PlayMode 7/7 (было 4 + 3 новых: вход в Home Alone (Red) и Life Choices (заряд Green) из
  меню, контроллер жив (GameController/GameDriver), чистый возврат по MenuButton через
  watchdog, повторный вход чистый (1 контроллер, без выжившего меню); + capture-тест меню).
  Существующий planned-slot тест перенацелен с Green/Life-Choices (теперь installed) на
  Bang/Factory-Game (остался planned).

**Скриншоты (batch WITH graphics, 0 мадженты, в scratchpad):**
`hub-menu-3play.png` (Test Game/Home Alone/Life Choices = [PLAY], остальные [soon]),
`hub-into-homealone.png` (Apartment: комнаты, кот, таймер 0:45, HUD «СУЕТА» — запущен из
хаба), `hub-into-lifechoices.png` (карточка жизни «Взять ипотеку на 25 лет?» + HUD
возраст/деньги/здоровье/энергия/отношения/таймер — поза `DebugPreviewArcadeShot`).

**Коэкзистенс-замечание:** hub-v0 instant-RED-select остаётся (утренний вопрос — снимать
ли его). Home Alone привязан к RedButton, который меню потребляет как instant-select, так
что вход в Home Alone идёт этим путём (выделить строку → Red), а Life Choices — настоящим
5-сек зарядом hold-to-launch по Green (меню Green не трогает). Оба пути армят watchdog и
возвращаются одинаково. Полное разведение шкала-vs-instant — отдельное утреннее решение.
