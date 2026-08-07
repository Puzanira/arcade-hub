#!/usr/bin/env bash
# wave-handout.sh — сверка «что у нас в дереве» с «что получит человек с улицы».
#
# Ветка handout репозитория arcade-hub — это витрина: её Packages/manifest.json
# тянет arcade-controls и шесть игр прямо с GitHub по ФИКСИРОВАННЫМ коммитам.
# Пока пины не двинули, внешний человек играет в старую волну, даже если у нас
# в дереве всё свежее.
#
# Что делает скрипт:
#   (без ключей)  печатает таблицу «репо / пин в handout / локальный HEAD /
#                 что лежит на GitHub» и говорит, где мы отстаём;
#   --bump        переписывает пины в манифесте ветки handout на текущие
#                 локальные HEAD-ы рабочих веток (через временный git worktree,
#                 рабочее дерево main НЕ трогается) и печатает готовую пачку
#                 строк «git push …» для основательницы.
#
# Скрипт НИКОГДА не пушит сам: пуши в этом проекте делает только основательница.
#
# Использование:
#   bin/wave-handout.sh
#   bin/wave-handout.sh --bump
#   bin/wave-handout.sh --bump --no-commit   # только правка файла, без коммита

set -euo pipefail

HUB_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STUDIO_ROOT="$(cd "$HUB_ROOT/../.." && pwd)"
HANDOUT_BRANCH="${HANDOUT_BRANCH:-handout}"   # переопределяется для проверок скрипта
MANIFEST_PATH="components/unity-game/Packages/manifest.json"

# repo_key | dep-name в манифесте | локальный путь (от STUDIO_ROOT) | рабочая ветка | github-репо | путь пакета внутри репо
ROWS=(
  "arcade-controls|com.aigamestudio.arcade-controls|projects/arcade-controls|main|arcade-controls|/components/unity-game/Packages/com.aigamestudio.arcade-controls"
  "home-alone|com.aigamestudio.game-home-alone|projects/home-alone|main|home-alone|/components/unity-game/Assets/_Project"
  "life-choices|com.aigamestudio.game-life-choices|projects/life-choices|main|life-choices|/components/unity-game/Assets/_Project"
  "meditation|com.aigamestudio.game-meditation|projects/meditation|main|meditation|/components/unity-game/Assets/_Project"
  "sisyphus|com.aigamestudio.game-endless-sisyphus|external/EndlessSisyphusUnity|arcade-input|EndlessSisyphusUnity|/Assets/EndlessSisyphus"
  "factory|com.aigamestudio.game-factory|external/factory_game|arcade-contract-repack|factory_game|/Assets/FactoryGame"
  "lady-bug|com.aigamestudio.game-lady-bug|external/lady_bug-v2|arcade-contract-repack-v2|lady_bug|/UnityProject/Assets/LadyBug"
)

# arcade-controls пинуется ТЕГОМ (пакет версионируется), остальные — коммитом.
TAG_PINNED_DEP="com.aigamestudio.arcade-controls"

BUMP=0
COMMIT=1
CHECK_REMOTE=1
for arg in "$@"; do
  case "$arg" in
    --bump)        BUMP=1 ;;
    --no-commit)   COMMIT=0 ;;
    --offline)     CHECK_REMOTE=0 ;;
    -h|--help)     sed -n '2,25p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "неизвестный ключ: $arg (см. --help)" >&2; exit 2 ;;
  esac
done

git -C "$HUB_ROOT" rev-parse --verify --quiet "$HANDOUT_BRANCH" >/dev/null || {
  echo "нет ветки '$HANDOUT_BRANCH' в $HUB_ROOT" >&2; exit 1; }

MANIFEST_JSON="$(git -C "$HUB_ROOT" show "$HANDOUT_BRANCH:$MANIFEST_PATH")"

pin_of() { # dep-name -> «revision» после '#', или пусто
  printf '%s' "$MANIFEST_JSON" | python3 -c '
import json,sys
dep=sys.argv[1]
url=json.load(sys.stdin)["dependencies"].get(dep,"")
print(url.split("#",1)[1] if "#" in url else "")
' "$1"
}

short() { local v="${1:-}"; [[ -z "$v" ]] && { printf -- '-'; return; }; printf '%.9s' "$v"; }

declare -a PUSH_LINES=()
declare -a BUMP_PAIRS=()
declare -a TABLE=("РЕПО|ПИН|ЛОКАЛЬНО|НА GITHUB|СОСТОЯНИЕ")
declare -a NOTES=()
BEHIND=0
BROKEN=0

# Таблица печатается питоном: ljust по СИМВОЛАМ, иначе кириллица/«—» ломают колонки.
print_table() {
  printf '%s\n' "${TABLE[@]}" | python3 -c '
import sys
rows=[l.split("|") for l in sys.stdin.read().splitlines() if l]
w=[max(len(r[i]) for r in rows) for i in range(len(rows[0]))]
def line(r): return "  ".join(r[i].ljust(w[i]) for i in range(len(r))).rstrip()
print()
print(line(rows[0]))
print("-"*min(120,sum(w)+2*(len(w)-1)))
for r in rows[1:]: print(line(r))
print("-"*min(120,sum(w)+2*(len(w)-1)))
'
}

for row in "${ROWS[@]}"; do
  IFS='|' read -r key dep repodir branch repo pkgpath <<<"$row"
  local_dir="$STUDIO_ROOT/$repodir"
  pkgdir="${pkgpath#/}"          # путь пакета внутри репо, без ведущего слэша
  pin="$(pin_of "$dep")"

  if ! git -C "$local_dir" rev-parse --git-dir >/dev/null 2>&1; then
    TABLE+=("$key|$(short "$pin")|-|-|НЕТ локального дерева: $local_dir")
    BROKEN=$((BROKEN+1))
    continue
  fi

  head_sha="$(git -C "$local_dir" rev-parse "$branch" 2>/dev/null || echo '')"
  head_disp="$(short "$head_sha")"

  # Главная ловушка выдачи: файл есть на диске, но не в гите. Локально по file:-пути
  # всё компилируется, а свежий клон пина падает на CS0246. Так уже случилось с
  # KeyboardHints.cs в arcade-controls v0.4.0 и со скриптами звука в meditation.
  untracked_code="$(git -C "$local_dir" status --porcelain --untracked-files=all -- "$pkgdir" \
                    | grep '^??' | grep -Ec '\.(cs|asmdef|shader|hlsl|inputactions)$' || true)"

  remote_sha=""
  if (( CHECK_REMOTE )); then
    remote_sha="$(git ls-remote "https://github.com/Puzanira/$repo.git" "refs/heads/$branch" 2>/dev/null | awk '{print $1}')"
  fi
  remote_disp="$( (( CHECK_REMOTE )) && short "$remote_sha" || printf '?' )"

  # чему должен быть равен пин
  if [[ "$dep" == "$TAG_PINNED_DEP" ]]; then
    # пин — тег; резолвим тег на GitHub, чтобы понять, на какой он коммит
    tag_sha=""
    if (( CHECK_REMOTE )) && [[ -n "$pin" ]]; then
      tag_sha="$(git ls-remote "https://github.com/Puzanira/$repo.git" "refs/tags/$pin^{}" "refs/tags/$pin" 2>/dev/null | head -1 | awk '{print $1}')"
    fi
    branch_note=""
    if (( CHECK_REMOTE )) && [[ "$remote_sha" != "$head_sha" ]]; then
      branch_note="; локальный HEAD не на GitHub"
      PUSH_LINES+=("git -C $local_dir push origin $branch")
    fi
    if (( ! CHECK_REMOTE )); then
      state="пин = тег $pin (--offline, не проверен)"
    elif [[ -z "$tag_sha" ]]; then
      state="ТЕГА $pin НЕТ НА GITHUB — пин не резолвится у человека с улицы"
      BROKEN=$((BROKEN+1))
      PUSH_LINES+=("git -C $local_dir push origin $pin   # тег пакета")
    elif [[ "$tag_sha" == "$head_sha" ]]; then
      state="ок (тег $pin = HEAD)"
    else
      state="ОТСТАЁТ: тег $pin старее HEAD → нужен новый тег версии пакета"
      BEHIND=$((BEHIND+1))
    fi
    state="$state$branch_note"
    if (( untracked_code > 0 )); then
      state="$state; $untracked_code файл(ов) кода не в гите"
      NOTES+=("$key: $untracked_code неотслеживаемых .cs/.shader/.asmdef в $pkgdir — их не будет у человека с улицы")
    fi
    TABLE+=("$key|${pin:--}|$head_disp|$remote_disp|$state")
    continue
  fi

  if [[ "$pin" == "$head_sha" ]]; then
    state="ок"
  elif [[ -z "$pin" ]]; then
    state="ПИНА НЕТ в манифесте handout"
    BEHIND=$((BEHIND+1)); BUMP_PAIRS+=("$dep|$head_sha|$repo|$pkgpath")
  else
    ahead="$(git -C "$local_dir" rev-list --count "$pin..$branch" 2>/dev/null || echo '?')"
    state="ОТСТАЁТ на $ahead коммит(ов)"
    BEHIND=$((BEHIND+1)); BUMP_PAIRS+=("$dep|$head_sha|$repo|$pkgpath")
  fi

  # что ещё не уехало на GitHub — без этого пин по SHA не резолвится у человека с улицы
  if (( CHECK_REMOTE )); then
    if [[ -z "$remote_sha" ]]; then
      state="$state; НЕТ ВЕТКИ НА GITHUB — пин не резолвится у человека с улицы"
      BROKEN=$((BROKEN+1))
      PUSH_LINES+=("git -C $local_dir push -u origin $branch")
    elif [[ "$remote_sha" != "$head_sha" ]]; then
      state="$state; локальный HEAD не на GitHub"
      PUSH_LINES+=("git -C $local_dir push origin $branch")
    fi
  fi

  if (( untracked_code > 0 )); then
    state="$state; $untracked_code файл(ов) кода не в гите"
    NOTES+=("$key: $untracked_code неотслеживаемых .cs/.shader/.asmdef в $pkgdir — их не будет у человека с улицы")
  fi

  TABLE+=("$key|$(short "$pin")|$head_disp|$remote_disp|$state")
done

# сам хаб: витрину надо не только собрать, но и отправить на GitHub
if (( CHECK_REMOTE )); then
  hub_local="$(git -C "$HUB_ROOT" rev-parse "$HANDOUT_BRANCH")"
  hub_remote="$(git ls-remote "https://github.com/Puzanira/arcade-hub.git" "refs/heads/$HANDOUT_BRANCH" 2>/dev/null | awk '{print $1}')"
  if [[ -z "$hub_remote" ]]; then
    TABLE+=("arcade-hub ($HANDOUT_BRANCH)|-|$(short "$hub_local")|-|ВЕТКИ НЕТ НА GITHUB — клонировать нечего")
    BROKEN=$((BROKEN+1))
    PUSH_LINES+=("git -C $HUB_ROOT push -u origin $HANDOUT_BRANCH")
  elif [[ "$hub_remote" != "$hub_local" ]]; then
    TABLE+=("arcade-hub ($HANDOUT_BRANCH)|-|$(short "$hub_local")|$(short "$hub_remote")|витрина не на GitHub")
    BROKEN=$((BROKEN+1))
    PUSH_LINES+=("git -C $HUB_ROOT push origin $HANDOUT_BRANCH")
  else
    TABLE+=("arcade-hub ($HANDOUT_BRANCH)|-|$(short "$hub_local")|$(short "$hub_remote")|ок")
  fi
fi

print_table
if (( BEHIND == 0 && BROKEN == 0 )); then
  echo "витрина handout совпадает с деревом — выдавать нечего."
else
  (( BEHIND )) && echo "отстающих пинов: $BEHIND   (обновить: bin/wave-handout.sh --bump)" || true
  (( BROKEN )) && echo "проблем с доступностью: $BROKEN   (см. пачку ниже)" || true
fi

if (( ${#NOTES[@]} > 0 )); then
  echo
  echo "КОД НА ДИСКЕ, НО НЕ В КОММИТЕ — пуш это не чинит, нужен коммит владельца инкремента."
  echo "(так уже ломалось: KeyboardHints.cs в arcade-controls v0.4.0, звук+подсказки в meditation)"
  printf '  • %s\n' "${NOTES[@]}"
fi

if (( BUMP )); then
  if (( ${#BUMP_PAIRS[@]} == 0 )); then
    echo; echo "--bump: нечего обновлять."
  else
    WT="$(mktemp -d "${TMPDIR:-/tmp}/wave-handout-XXXXXX")/wt"
    git -C "$HUB_ROOT" worktree add --quiet "$WT" "$HANDOUT_BRANCH"
    trap 'git -C "$HUB_ROOT" worktree remove --force "$WT" >/dev/null 2>&1 || true' EXIT

    printf '%s\n' "${BUMP_PAIRS[@]}" | python3 -c '
import json,sys,collections
path=sys.argv[1]
d=json.load(open(path), object_pairs_hook=collections.OrderedDict)
dep=d["dependencies"]
for line in sys.stdin.read().splitlines():
    if not line.strip(): continue
    name,sha,repo,pkgpath=line.split("|")
    dep[name]=f"https://github.com/Puzanira/{repo}.git?path={pkgpath}#{sha}"
    print("  пин:", name, "->", sha[:9])
open(path,"w").write(json.dumps(d, indent=2, ensure_ascii=False)+"\n")
' "$WT/$MANIFEST_PATH"

    if (( COMMIT )); then
      git -C "$WT" add "$MANIFEST_PATH"
      if git -C "$WT" diff --cached --quiet; then
        echo "  манифест не изменился — коммита нет."
      else
        git -C "$WT" commit --quiet -m "handout: пины волны на текущие HEAD-ы"
        echo "  коммит в ветке $HANDOUT_BRANCH: $(git -C "$WT" rev-parse --short HEAD)"
      fi
    else
      echo "  --no-commit: манифест правлен, коммита нет (worktree будет убран, правка потеряется)"
    fi
    PUSH_LINES+=("git -C $HUB_ROOT push origin $HANDOUT_BRANCH")
  fi
fi

if (( ${#PUSH_LINES[@]} > 0 )); then
  echo
  echo "ПАЧКА ДЛЯ ОСНОВАТЕЛЬНИЦЫ (скрипт сам не пушит — выполнить руками):"
  # порядок важен: сначала игры и пакет, ветка handout хаба — последней
  printf '%s\n' "${PUSH_LINES[@]}" | awk '!seen[$0]++ {print "! " $0}'
fi
