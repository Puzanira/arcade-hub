#!/usr/bin/env python3
"""
Arcade integration contract check (Unity-free).

Runs on any machine with python3 (stdlib only) and, ideally, git. It verifies the
*integration boundary* of a game repository against the arcade cabinet contract
(see ARCADE_INTEGRATION.md). It does NOT open Unity and does NOT need a license.

Configuration lives in `arcade-kit.json` at the repository root. Run from the repo
root:

    python3 contract_check.py            # uses ./arcade-kit.json
    python3 contract_check.py --root DIR # check a different repo root

Exit code 0 = all checks pass (green). Exit code 1 = at least one violation (red).

Checks (letters match the increment spec):
  (a) exactly one game root folder with a valid package.json: `name` starts with the
      arcade game prefix (default `com.aigamestudio.game-`), `version` is
      semver-like, `displayName` is present.
  (b) game.json is valid, declares an entry scene that exists on disk AND is listed
      (enabled) in ProjectSettings/EditorBuildSettings.asset; `controls` is an array
      of valid logical arcade control names.
  (c) at least one .asmdef under the game folder (scripts build in their own assembly).
  (d) no raw input in the game's .cs files: any member access on UnityEngine.Input
      (Input.GetKey/GetAxis/GetButton/mousePosition/touchCount/...), any
      UnityEngine.InputSystem usage, device `.current` accessors, and using-aliases
      of UnityEngine.Input(System). Whitespace tricks (`Input . GetKey`) are
      normalized away before matching. ArcadeInput.* is allowed. Files listed in
      `rawInputAllowlist` carry a pinned `baselineCount`: the check fails if the
      number of raw-input lines GROWS beyond the baseline (legacy debt is frozen,
      not exempted).
  (e) no game assets under Assets/ outside the game folder (except the configurable
      whitelist: URP auto-assets, Settings/, StreamingAssets/).
  (f) Library/, Temp/, Obj/ are not tracked by git.
"""

import json
import os
import re
import subprocess
import sys

PACKAGE_NAME_PREFIX_DEFAULT = "com.aigamestudio.game-"

# Names allowed to live directly under Assets/ outside the game folder.
DEFAULT_ASSET_WHITELIST = [
    "DefaultVolumeProfile.asset",
    "UniversalRenderPipelineGlobalSettings.asset",
    "Settings",
    "StreamingAssets",
]

# Logical arcade controls a game may declare in game.json `controls`.
VALID_CONTROLS = {
    "Crank",
    "RedButton",
    "GreenButton",
    "BangButton",
    "HeightA",
    "HeightB",
    "Joystick",
    "MenuButton",
}

# Raw-input patterns forbidden in game .cs files. They run against a NORMALIZED
# line (whitespace around dots removed, runs of whitespace collapsed), so spacing
# tricks like `Input . GetKeyDown` do not bypass the scan. Word boundaries keep
# ArcadeInput.* / MyInput.* from matching the bare `Input` token.
RAW_INPUT_PATTERNS = [
    re.compile(r"\bUnityEngine\.Input\b"),      # fully-qualified legacy input
    re.compile(r"\bUnityEngine\.InputSystem\b"),  # new Input System (any usage)
    re.compile(r"\bInput\.\w+"),                # ANY member access on Input
    re.compile(r"\b(Keyboard|Mouse|Gamepad|Touchscreen|Pointer|Joystick)\.current\b"),
]

# `using X = UnityEngine.Input;` / `... = UnityEngine.InputSystem...;` is a
# violation by itself: it exists only to smuggle raw input past a textual scan.
RAW_INPUT_ALIAS = re.compile(r"\busing\s+\w+\s*=\s*UnityEngine\.Input(System)?\b")


class Report:
    def __init__(self):
        self.failures = []
        self.notes = []

    def fail(self, check, msg):
        self.failures.append((check, msg))

    def note(self, msg):
        self.notes.append(msg)

    def ok(self):
        return not self.failures


def load_config(repo_root):
    cfg_path = os.path.join(repo_root, "arcade-kit.json")
    if not os.path.isfile(cfg_path):
        raise SystemExit(
            "FATAL: arcade-kit.json not found at repo root: %s" % cfg_path
        )
    try:
        with open(cfg_path, "r", encoding="utf-8") as fh:
            cfg = json.load(fh)
    except (ValueError, OSError) as exc:
        raise SystemExit("FATAL: arcade-kit.json is not valid JSON: %s" % exc)

    cfg.setdefault("unityProjectPath", ".")
    cfg.setdefault("packageNamePrefix", PACKAGE_NAME_PREFIX_DEFAULT)
    cfg.setdefault("rawInputAllowlist", [])
    cfg.setdefault("assetWhitelist", list(DEFAULT_ASSET_WHITELIST))
    if "gameFolder" not in cfg:
        raise SystemExit("FATAL: arcade-kit.json must set `gameFolder`.")
    for entry in cfg["rawInputAllowlist"]:
        if (
            not isinstance(entry, dict)
            or not isinstance(entry.get("file"), str)
            or not isinstance(entry.get("baselineCount"), int)
            or entry["baselineCount"] < 0
        ):
            raise SystemExit(
                "FATAL: rawInputAllowlist entries must be objects "
                '{"file": "<path relative to gameFolder>", "baselineCount": N} '
                "with N >= 0; got %r" % (entry,)
            )
    return cfg


def _read_json(path):
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def check_package_json(rep, game_dir, prefix):
    """(a) one game folder with a valid package.json and a game-prefixed name."""
    if not os.path.isdir(game_dir):
        rep.fail("a", "game folder does not exist: %s" % game_dir)
        return
    pkg_path = os.path.join(game_dir, "package.json")
    if not os.path.isfile(pkg_path):
        rep.fail("a", "package.json missing in game folder: %s" % pkg_path)
        return
    try:
        pkg = _read_json(pkg_path)
    except (ValueError, OSError) as exc:
        rep.fail("a", "package.json is not valid JSON: %s" % exc)
        return
    name = pkg.get("name", "")
    if not name.startswith(prefix):
        rep.fail(
            "a",
            "package.json name %r must start with %r" % (name, prefix),
        )
    version = pkg.get("version", "")
    if not isinstance(version, str) or not re.match(r"^\d+\.\d+\.\d+", version):
        rep.fail(
            "a",
            "package.json `version` %r must be semver-like (e.g. 0.1.0)" % (version,),
        )
    display = pkg.get("displayName", "")
    if not isinstance(display, str) or not display.strip():
        rep.fail("a", "package.json must have a non-empty `displayName`")


def _enabled_build_scenes(build_settings_path):
    """Return the set of enabled scene paths from EditorBuildSettings.asset."""
    scenes = set()
    if not os.path.isfile(build_settings_path):
        return scenes
    with open(build_settings_path, "r", encoding="utf-8") as fh:
        text = fh.read()
    # Each scene entry: a line "enabled: N" followed by a line "path: <p>".
    enabled = None
    for line in text.splitlines():
        s = line.strip()
        m = re.match(r"- enabled:\s*(\d)", s)
        if m:
            enabled = m.group(1) == "1"
            continue
        m = re.match(r"enabled:\s*(\d)", s)
        if m:
            enabled = m.group(1) == "1"
            continue
        m = re.match(r"path:\s*(\S.*)$", s)
        if m and enabled:
            scenes.add(m.group(1).strip())
            enabled = None
    return scenes


def check_game_json(rep, game_dir, unity_project, game_folder_rel):
    """(b) game.json valid; entry scene exists and is enabled in Build Settings."""
    gj_path = os.path.join(game_dir, "game.json")
    if not os.path.isfile(gj_path):
        rep.fail("b", "game.json missing in game folder: %s" % gj_path)
        return
    try:
        gj = _read_json(gj_path)
    except (ValueError, OSError) as exc:
        rep.fail("b", "game.json is not valid JSON: %s" % exc)
        return

    entry = gj.get("entryScene")
    if not entry:
        rep.fail("b", "game.json has no `entryScene`")
        return
    for field in ("name", "version", "controls"):
        if field not in gj:
            rep.fail("b", "game.json is missing required field %r" % field)

    controls = gj.get("controls")
    if controls is not None:
        if not isinstance(controls, list) or not all(
            isinstance(c, str) for c in controls
        ):
            rep.fail("b", "game.json `controls` must be an array of strings")
        else:
            for c in controls:
                if c not in VALID_CONTROLS:
                    rep.fail(
                        "b",
                        "game.json control %r is not a logical arcade control %s"
                        % (c, sorted(VALID_CONTROLS)),
                    )

    scene_abs = os.path.join(unity_project, entry)
    if not os.path.isfile(scene_abs):
        rep.fail("b", "entry scene file does not exist: %s" % scene_abs)

    build_settings = os.path.join(
        unity_project, "ProjectSettings", "EditorBuildSettings.asset"
    )
    enabled = _enabled_build_scenes(build_settings)
    if entry not in enabled:
        rep.fail(
            "b",
            "entry scene %r not enabled in EditorBuildSettings.asset (enabled: %s)"
            % (entry, sorted(enabled)),
        )


def check_asmdef(rep, game_dir):
    """(c) at least one .asmdef under the game folder."""
    for _root, _dirs, files in os.walk(game_dir):
        if any(f.endswith(".asmdef") for f in files):
            return
    rep.fail("c", "no .asmdef found under game folder: %s" % game_dir)


def _is_comment_line(stripped):
    return (
        stripped.startswith("//")
        or stripped.startswith("*")
        or stripped.startswith("/*")
    )


def _normalize_line(line):
    """Collapse whitespace so `Input . GetKey` / `UnityEngine . Input` can't hide."""
    line = re.sub(r"\s*\.\s*", ".", line)
    return re.sub(r"\s+", " ", line)


def _raw_input_hits(path, rep):
    """Return [(lineno, reason)] raw-input violations in one .cs file."""
    hits = []
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            lines = fh.readlines()
    except OSError as exc:
        rep.fail("d", "cannot read %s: %s" % (path, exc))
        return hits
    for i, line in enumerate(lines, 1):
        stripped = line.lstrip()
        if _is_comment_line(stripped):
            continue
        norm = _normalize_line(line)
        if RAW_INPUT_ALIAS.search(norm):
            hits.append((i, "using-alias of UnityEngine.Input/InputSystem"))
            continue
        for pat in RAW_INPUT_PATTERNS:
            if pat.search(norm):
                hits.append((i, pat.pattern))
                break
    return hits


def check_raw_input(rep, game_dir, allowlist):
    """(d) no raw input in game .cs files.

    `allowlist` maps abspath -> baselineCount for legacy files: those fail only if
    the number of raw-input LINES grows beyond the pinned baseline. Everything else
    fails on the first hit.
    """
    for root, _dirs, files in os.walk(game_dir):
        for fname in files:
            if not fname.endswith(".cs"):
                continue
            path = os.path.join(root, fname)
            hits = _raw_input_hits(path, rep)
            apath = os.path.abspath(path)
            if apath in allowlist:
                baseline = allowlist[apath]
                if len(hits) > baseline:
                    new = ", ".join("line %d (%s)" % h for h in hits)
                    rep.fail(
                        "d",
                        "%s has %d raw-input lines > baselineCount %d — new raw "
                        "input added to a legacy file; migrate to ArcadeInput.* "
                        "instead (hits: %s)" % (path, len(hits), baseline, new),
                    )
                elif len(hits) < baseline:
                    rep.note(
                        "%s has %d raw-input lines < baselineCount %d — lower the "
                        "baseline in arcade-kit.json" % (path, len(hits), baseline)
                    )
                continue
            for i, reason in hits:
                rep.fail(
                    "d",
                    "raw input %r in %s:%d (read input via ArcadeInput.*)"
                    % (reason, path, i),
                )


def check_assets_outside(rep, unity_project, game_folder_rel, whitelist):
    """(e) nothing game-related under Assets/ outside the game folder."""
    assets_dir = os.path.join(unity_project, "Assets")
    if not os.path.isdir(assets_dir):
        rep.fail("e", "Assets/ folder not found at %s" % assets_dir)
        return
    # game_folder_rel is relative to the Unity project, e.g. "Assets/LadyBug".
    parts = game_folder_rel.replace("\\", "/").split("/")
    if len(parts) < 2 or parts[0] != "Assets":
        rep.fail(
            "e",
            "gameFolder %r must live under Assets/" % game_folder_rel,
        )
        return
    game_top = parts[1]  # top-level folder name under Assets/
    allowed = set(whitelist)
    allowed.update(w + ".meta" for w in whitelist)
    allowed.add(game_top)
    allowed.add(game_top + ".meta")
    for entry in sorted(os.listdir(assets_dir)):
        if entry in allowed:
            continue
        rep.fail(
            "e",
            "asset outside game folder: Assets/%s (whitelist it in arcade-kit.json "
            "if it is a hub-owned/URP auto asset)" % entry,
        )


def check_git_ignored(rep, repo_root):
    """(f) Library/, Temp/, Obj/ must not be tracked by git."""
    try:
        out = subprocess.run(
            ["git", "-C", repo_root, "ls-files"],
            capture_output=True,
            text=True,
            check=True,
        ).stdout
    except (OSError, subprocess.CalledProcessError):
        rep.note(
            "check (f) skipped: not a git repo / git unavailable at %s" % repo_root
        )
        return
    bad = re.compile(r"(^|/)(Library|Temp|Obj)/")
    hits = [ln for ln in out.splitlines() if bad.search(ln)]
    if hits:
        rep.fail(
            "f",
            "Library/Temp/Obj must not be tracked; tracked: %s"
            % ", ".join(hits[:10]),
        )


def run(repo_root):
    cfg = load_config(repo_root)
    unity_project = os.path.normpath(
        os.path.join(repo_root, cfg["unityProjectPath"])
    )
    game_folder_rel = cfg["gameFolder"].replace("\\", "/")
    game_dir = os.path.normpath(os.path.join(unity_project, game_folder_rel))

    allowlist = {
        os.path.abspath(os.path.join(game_dir, e["file"])): e["baselineCount"]
        for e in cfg.get("rawInputAllowlist", [])
    }

    rep = Report()
    check_package_json(rep, game_dir, cfg["packageNamePrefix"])
    check_game_json(rep, game_dir, unity_project, game_folder_rel)
    check_asmdef(rep, game_dir)
    check_raw_input(rep, game_dir, allowlist)
    check_assets_outside(rep, unity_project, game_folder_rel, cfg["assetWhitelist"])
    check_git_ignored(rep, repo_root)
    return rep


def main(argv):
    repo_root = "."
    if "--root" in argv:
        repo_root = argv[argv.index("--root") + 1]
    repo_root = os.path.abspath(repo_root)

    rep = run(repo_root)

    print("arcade contract check @ %s" % repo_root)
    for msg in rep.notes:
        print("  note: %s" % msg)
    if rep.ok():
        print("RESULT: GREEN — all contract checks passed.")
        return 0
    print("RESULT: RED — %d violation(s):" % len(rep.failures))
    for check, msg in rep.failures:
        print("  [%s] %s" % (check, msg))
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
