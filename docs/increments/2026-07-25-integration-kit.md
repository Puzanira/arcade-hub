# Increment: integration-kit (этап ④, эталон)

Status: GATE 1 APPROVED (основательница 2026-07-25: «инструкции + тесты на контроллеры,
чтобы не пушили ломающее»). Ночной прогон.

## Что делаем
1. Эталон кита в хабе, `kit/`: ARCADE_INTEGRATION.md (адаптация контракта, читается
   агентами авторов), contract-check скрипт (файловые проверки БЕЗ Unity-лицензии:
   package.json/asmdef валидны, контент в одной папке, нет сырого ввода мимо
   ArcadeInput в .cs игры, game.json валиден, entry-сцена в Build Settings),
   GitHub Actions workflow (contract-check на push/PR, ubuntu, без Unity) +
   опциональная ступень GameCI-инструкцией.
2. Применить кит commit-ом в готовую ветку external/lady_bug (arcade-contract-repack).
   Сизиф/factory — после завершения их ночных owner-ов (не сталкиваться в файлах).
3. Скрипт проверок обязан ПАДАТЬ на нарушении: self-test на синтетике (сломанный
   пример → красный, здоровый → зелёный).

## Done-контракт
1. kit/ в хабе полный (дока+скрипт+workflow); 2. self-test скрипта (2 кейса);
3. lady_bug ветка несёт кит и contract-check проходит на ней локально;
4. отчёт: что проверяется, что отложено в GameCI-ступень.

## Прогресс (owner, ночной прогон 2026-07-25)

Статус: **сделано, ждёт машинного гейта Maintainer-а** (в arcade-hub НЕ коммичу).

Состав кита `projects/arcade-hub/kit/`:
- `ARCADE_INTEGRATION.md` — финальная дока для чужих репо (RU, адресована агентам,
  раздел про опциональный полный CI с Unity-тестами через GameCI + секрет
  `UNITY_LICENSE`).
- `contract_check.py` — stdlib-only, Unity-free. Проверки (a) package.json+имя-префикс,
  (b) game.json + entry-сцена существует и включена в EditorBuildSettings,
  (c) asmdef под папкой игры, (d) нет сырого ввода мимо ArcadeInput (allowlist для
  легаси-долга), (e) нет ассетов вне папки игры (whitelist), (f) Library/Temp/Obj не
  затрекана. Конфиг — `arcade-kit.json` в корне репо игры.
- `arcade-kit.example.json`, `templates/arcade-contract.yml` (GitHub Actions,
  ubuntu, без Unity), `selftest/` (healthy+broken + run_selftest.py).

Self-test: `python3 kit/selftest/run_selftest.py` → healthy GREEN (exit 0),
broken RED (7 нарушений по a–f), SELF-TEST PASSED.

Применение в lady_bug (ветка `arcade-contract-repack`, 2 коммита, НЕ запушено):
- `61bedb0` kit (доку+скрипт+arcade-kit.json+workflow, пути под UnityProject/);
- `c265380` package.json (`com.aigamestudio.game-lady-bug`) + game.json
  (entry Main.unity, controls HeightA/HeightB) + сгенерённые .meta.
- `contract_check.py` локально → GREEN. Batch 6000.5.3f1 → чистая компиляция
  (json импортится как TextAsset, TextScriptImporter — компиляцию не трогает).

Решение по (d): lady_bug ещё не мигрирован на arcade-controls → 7 файлов с сырым
вводом внесены в `rawInputAllowlist` как явный технический долг. Новый сырой ввод в
любом другом файле по-прежнему уронит контракт. Полная миграция ввода — отдельный
инкремент (MVP-этап 5), НЕ входит в этот.

Отложено: GameCI-ступень с Unity-тестами (нужен секрет `UNITY_LICENSE`) —
задокументирована в ARCADE_INTEGRATION.md §8, включается по готовности лицензии;
контрактных EditMode-тестов (`Tests/Integration/`) кит пока не приносит — их эталон
поедет отдельным обновлением кита.

## Скептик-раунд (2026-07-25, все 4 находки закрыты)

1. Regex сырого ввода ужесточён: whitespace-нормализация (`Input . GetKey` не
   проходит), `using X = UnityEngine.Input(System)` — нарушение само по себе,
   ЛЮБОЙ член `Input.*`, любое `UnityEngine.InputSystem`, `.current` девайсы
   (Keyboard/Mouse/Gamepad/Touchscreen/Pointer/Joystick).
2. rawInputAllowlist → baseline-фиксация: `{file, baselineCount}`; строк больше
   baseline → красный (долг заморожен, не прощён); меньше → note «понизь baseline».
3. package.json: `version` semver-подобный + непустой `displayName` обязательны.
4. game.json `controls`: массив строк из 8 логических контролов; "Keyboard" падает.

Self-test расширен: broken +5 файлов (по одному на класс обхода + over-baseline
легаси), healthy +легаси-файл ровно на baseline (зелёный). Прогон: healthy GREEN,
broken RED (21 нарушение), SELF-TEST PASSED. lady_bug: реальные baseline посчитаны
(7/4/1/3/2/1/5), contract-check GREEN; негативная проба (baseline 7→6) даёт RED и
откатана. Fixup-коммит `8b70f83` в ветку arcade-contract-repack (не запушено).
