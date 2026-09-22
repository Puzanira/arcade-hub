#!/usr/bin/env bash
# hub-games.sh — чем хаб подключает игры: ЖИВОЙ ПАПКОЙ или ЗАФИКСИРОВАННОЙ ВЕРСИЕЙ.
#
# ЗАЧЕМ. Живая папка означает, что автомат собирается из того, что ПРЯМО СЕЙЧАС
# лежит под руками у чужой сессии. Пока сессия дописывает код, автомат не
# компилируется — целиком, вместе с лаунчером и всеми остальными играми.
# За 2026-09-21..22 это случилось трижды.
#
# Зафиксированная версия — это «взять игру с GitHub, вот этот коммит». Такая
# игра не может сломаться от чужой недописанной строчки: она уже отправлена и
# уже собиралась.
#
# ЦЕНА. У зафиксированной игры правки появляются в автомате НЕ мгновенно: сессия
# должна отправить работу, а потом кто-то двигает пин (`pin <игра>`). Поэтому
# игру, с которой прямо сейчас работают, разумно держать живой — осознанно и
# по одной.
#
#   bin/hub-games.sh                 таблица: что живое, что зафиксировано
#   bin/hub-games.sh pin <игра>      зафиксировать на последней ОТПРАВЛЕННОЙ версии
#   bin/hub-games.sh pin --all       зафиксировать все
#   bin/hub-games.sh live <игра>     вернуть живую папку (на время работы над игрой)
#   bin/hub-games.sh live --all      вернуть все живые папки
#
# Скрипт правит только Packages/manifest.json ветки main и НИЧЕГО не пушит.
# Витрина для оператора (ветка handout) живёт отдельно — bin/wave-handout.sh.
set -euo pipefail

HUB_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STUDIO_ROOT="$(cd "$HUB_ROOT/../.." && pwd)"
MANIFEST="$HUB_ROOT/components/unity-game/Packages/manifest.json"
GH_OWNER="${GH_OWNER:-Puzanira}"

# игра | dep-name | путь от STUDIO_ROOT | ветка | github-репо | путь пакета внутри репо
ROWS=(
  "home-alone|com.aigamestudio.game-home-alone|projects/home-alone|main|home-alone|/components/unity-game/Assets/_Project"
  "life-choices|com.aigamestudio.game-life-choices|projects/life-choices|main|life-choices|/components/unity-game/Assets/_Project"
  "meditation|com.aigamestudio.game-meditation|projects/meditation|main|meditation|/components/unity-game/Assets/_Project"
  "sisyphus|com.aigamestudio.game-endless-sisyphus|external/EndlessSisyphusUnity|arcade-input|EndlessSisyphusUnity|/Assets/EndlessSisyphus"
  "factory|com.aigamestudio.game-factory|external/factory_game|arcade-contract-repack|factory_game|/Assets/FactoryGame"
  "lady-bug|com.aigamestudio.game-lady-bug|external/lady_bug-v2|arcade-contract-repack-v2|lady_bug|/UnityProject/Assets/LadyBug"
)

row_field() { echo "$1" | cut -d'|' -f"$2"; }

# внешние игры лежат на уровень выше projects/ — отсюда разная глубина ../
file_url_for() {
  local path="$1"
  case "$path" in
    projects/*) echo "file:../../../../${path#projects/}" ;;
    *)          echo "file:../../../../../$path" ;;
  esac
}

# куда смотрит ремоут с нашей копией игры: у своих origin, у внешних fork
remote_for() {
  local path="$1"
  case "$path" in projects/*) echo origin ;; *) echo fork ;; esac
}

current_dep() { python3 - "$MANIFEST" "$1" <<'PY'
import json, sys
print(json.load(open(sys.argv[1]))["dependencies"].get(sys.argv[2], ""))
PY
}

set_dep() { python3 - "$MANIFEST" "$1" "$2" <<'PY'
import json, sys, collections
p, dep, val = sys.argv[1], sys.argv[2], sys.argv[3]
m = json.load(open(p), object_pairs_hook=collections.OrderedDict)
m["dependencies"][dep] = val
open(p, "w").write(json.dumps(m, indent=2, ensure_ascii=False) + "\n")
PY
}

pushed_sha() {  # последний коммит, который РЕАЛЬНО лежит на GitHub
  local path="$1" branch="$2" remote="$3"
  git -C "$STUDIO_ROOT/$path" fetch "$remote" "$branch" --quiet 2>/dev/null || true
  git -C "$STUDIO_ROOT/$path" rev-parse "$remote/$branch" 2>/dev/null || echo ""
}

status() {
  printf "%-13s %-9s %s\n" "ИГРА" "РЕЖИМ" "ПОДРОБНОСТИ"
  printf -- "----------------------------------------------------------------------------------\n"
  local live_count=0
  for row in "${ROWS[@]}"; do
    local game dep path branch remote cur
    game=$(row_field "$row" 1); dep=$(row_field "$row" 2)
    path=$(row_field "$row" 3); branch=$(row_field "$row" 4)
    remote=$(remote_for "$path")
    cur=$(current_dep "$dep")
    if [[ "$cur" == file:* ]]; then
      live_count=$((live_count+1))
      local dirty head pushed note
      dirty=$(git -C "$STUDIO_ROOT/$path" status --short -- . 2>/dev/null | wc -l | tr -d ' ')
      head=$(git -C "$STUDIO_ROOT/$path" rev-parse --short HEAD 2>/dev/null || echo '?')
      pushed=$(pushed_sha "$path" "$branch" "$remote")
      note="локально $head"
      [ "$dirty" -gt 0 ] && note="$note, НЕЗАКОММИЧЕНО $dirty файл(ов) — может уронить сборку хаба"
      [ -n "$pushed" ] && [ "${pushed:0:7}" != "${head:0:7}" ] && note="$note, не отправлено на GitHub"
      printf "%-13s %-9s %s\n" "$game" "ЖИВАЯ" "$note"
    else
      printf "%-13s %-9s %s\n" "$game" "пин" "${cur##*#}" | cut -c1-100
    fi
  done
  printf -- "----------------------------------------------------------------------------------\n"
  if [ "$live_count" -eq 0 ]; then
    echo "все игры зафиксированы — чужая недописанная правка сборку хаба не уронит"
  else
    echo "живых папок: $live_count (их недописанный код валит компиляцию ВСЕГО автомата)"
    echo "зафиксировать: bin/hub-games.sh pin <игра>   или   pin --all"
  fi
}

apply() {
  local mode="$1" target="$2" touched=0
  for row in "${ROWS[@]}"; do
    local game dep path branch repo pkg remote
    game=$(row_field "$row" 1); dep=$(row_field "$row" 2)
    path=$(row_field "$row" 3); branch=$(row_field "$row" 4)
    repo=$(row_field "$row" 5); pkg=$(row_field "$row" 6)
    remote=$(remote_for "$path")
    [ "$target" != "--all" ] && [ "$target" != "$game" ] && continue
    touched=$((touched+1))

    if [ "$mode" = live ]; then
      set_dep "$dep" "$(file_url_for "$path")"
      echo "  $game -> ЖИВАЯ ПАПКА ($path)"
    else
      local sha
      sha=$(pushed_sha "$path" "$branch" "$remote")
      if [ -z "$sha" ]; then
        echo "  $game -> ПРОПУЩЕНА: не вижу ветку $branch на GitHub (ремоут $remote)" >&2
        continue
      fi
      local head
      head=$(git -C "$STUDIO_ROOT/$path" rev-parse HEAD 2>/dev/null || echo '')
      if [ -n "$head" ] && [ "$head" != "$sha" ]; then
        echo "  $game: ВНИМАНИЕ — локально есть коммиты новее отправленных; фиксирую на отправленный" >&2
      fi
      set_dep "$dep" "https://github.com/$GH_OWNER/$repo.git?path=$pkg#$sha"
      echo "  $game -> пин ${sha:0:9}"
    fi
  done
  [ "$touched" -eq 0 ] && { echo "нет такой игры: $target" >&2; echo "есть: $(for r in "${ROWS[@]}"; do printf '%s ' "$(row_field "$r" 1)"; done)" >&2; exit 2; }
  echo
  echo "manifest.json правлен. Unity подхватит при следующем открытии проекта"
  echo "(зафиксированные игры он скачает с GitHub — первый раз это занимает время)."
  echo "Коммит и пуш — руками, скрипт этого не делает."
}

case "${1:-status}" in
  status|"") status ;;
  pin)  [ $# -ge 2 ] || { echo "укажи игру или --all" >&2; exit 2; }; apply pin  "$2" ;;
  live) [ $# -ge 2 ] || { echo "укажи игру или --all" >&2; exit 2; }; apply live "$2" ;;
  *) echo "не знаю команду «$1»; бывают: status, pin, live" >&2; exit 2 ;;
esac
