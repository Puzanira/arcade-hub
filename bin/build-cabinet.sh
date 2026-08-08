#!/usr/bin/env bash
# Сборка автомата в готовый исполняемый билд.
#
# На компьютере стойки Unity нет — туда едет папка с .exe. Хаб и все игры лежат
# в одном Unity-проекте (игры подключены пакетами), поэтому билд ОДИН и содержит
# всё: лаунчер + шесть игр.
#
#   bin/build-cabinet.sh                 # Windows (то, что едет на стойку)
#   bin/build-cabinet.sh mac             # macOS — проверить билд на своей машине
#   bin/build-cabinet.sh windows ~/путь  # своя папка вывода
#
# Требует установленного модуля Windows Build Support (Mono) в редакторе Unity
# нужной версии (Unity Hub → редактор → Add modules → Windows Build Support).
set -euo pipefail

TARGET="${1:-windows}"
PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/components/unity-game"
OUTPUT="${2:-$PROJECT/Builds/$TARGET}"

case "$TARGET" in
  windows) METHOD="AiGameStudio.ArcadeHub.Editor.BuildCabinet.Windows"; ENGINE="WindowsStandaloneSupport" ;;
  mac)     METHOD="AiGameStudio.ArcadeHub.Editor.BuildCabinet.Mac";     ENGINE="MacStandaloneSupport" ;;
  *) echo "Не знаю цель «$TARGET» — бывают windows и mac." >&2; exit 2 ;;
esac

VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT/ProjectSettings/ProjectVersion.txt")"
UNITY="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"

[ -x "$UNITY" ] || { echo "Нет редактора Unity $VERSION по пути $UNITY" >&2; exit 3; }
[ -d "/Applications/Unity/Hub/Editor/$VERSION/PlaybackEngines/$ENGINE" ] || {
  echo "В редакторе $VERSION не установлен модуль $ENGINE." >&2
  echo "Поставить: Unity Hub → Installs → $VERSION → Add modules." >&2
  exit 4
}

# Редактор с этим же проектом держит блокировку — батч-сборка молча встанет в очередь
# и провисит до таймаута. Проверяем заранее, чтобы сказать это человеческим языком.
if [ -f "$PROJECT/Temp/UnityLockfile" ] && lsof "$PROJECT/Temp/UnityLockfile" >/dev/null 2>&1; then
  echo "Проект открыт в редакторе Unity — закрой его, сборка берёт проект целиком." >&2
  exit 5
fi

LOG="$PROJECT/Builds/build-$TARGET.log"
mkdir -p "$(dirname "$LOG")"

echo "Собираю $TARGET из $PROJECT"
echo "Вывод: $OUTPUT"
echo "Лог:   $LOG"

set +e
"$UNITY" -quit -batchmode -nographics \
  -projectPath "$PROJECT" \
  -executeMethod "$METHOD" \
  -buildOutput "$OUTPUT" \
  -logFile "$LOG"
CODE=$?
set -e

if [ $CODE -ne 0 ]; then
  echo "=== СБОРКА НЕ СЛОЖИЛАСЬ (код $CODE). Хвост лога: ===" >&2
  grep -E "error|Error|Exception|BuildCabinet" "$LOG" | tail -30 >&2
  exit $CODE
fi

echo "=== ГОТОВО ==="
grep "BuildCabinet" "$LOG" | tail -3
du -sh "$OUTPUT"
