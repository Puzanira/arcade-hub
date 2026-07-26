#!/usr/bin/env python3
"""
Self-test for contract_check.py.

Copies each synthetic fixture (healthy/, broken/) into a throwaway temp dir, makes
it a real git repo (so the Library/Temp/Obj tracking check is exercised), runs
contract_check.py against it, and asserts the expected colour:

  healthy  -> GREEN (exit 0)
  broken   -> RED   (exit 1), and injects a tracked Library/ file to also trip (f)

Run:  python3 kit/selftest/run_selftest.py
Exit 0 = self-test passed (checker behaves correctly on both fixtures).
"""

import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CHECKER = os.path.join(os.path.dirname(HERE), "contract_check.py")


def _git(repo, *args):
    subprocess.run(
        ["git", "-C", repo, *args],
        check=True,
        capture_output=True,
        text=True,
    )


def prepare(fixture, inject_tracked_library):
    src = os.path.join(HERE, fixture)
    tmp = tempfile.mkdtemp(prefix="arcade-selftest-%s-" % fixture)
    dst = os.path.join(tmp, "repo")
    shutil.copytree(src, dst)
    if inject_tracked_library:
        lib = os.path.join(dst, "Library")
        os.makedirs(lib, exist_ok=True)
        with open(os.path.join(lib, "ArtifactDB"), "w") as fh:
            fh.write("regenerable Unity cache that must never be tracked\n")
    _git(dst, "init", "-q")
    _git(dst, "config", "user.email", "selftest@example.com")
    _git(dst, "config", "user.name", "selftest")
    _git(dst, "add", "-A")
    _git(dst, "-c", "commit.gpgsign=false", "commit", "-q", "-m", "fixture")
    return dst


def run_checker(repo):
    proc = subprocess.run(
        [sys.executable, CHECKER, "--root", repo],
        capture_output=True,
        text=True,
    )
    return proc.returncode, proc.stdout + proc.stderr


def main():
    ok = True

    healthy_repo = prepare("healthy", inject_tracked_library=False)
    code, out = run_checker(healthy_repo)
    print("=== healthy fixture ===")
    print(out.strip())
    if code == 0:
        print("PASS: healthy fixture is GREEN\n")
    else:
        print("FAIL: healthy fixture should be GREEN but exit=%d\n" % code)
        ok = False

    broken_repo = prepare("broken", inject_tracked_library=True)
    code, out = run_checker(broken_repo)
    print("=== broken fixture ===")
    print(out.strip())
    if code == 1:
        print("PASS: broken fixture is RED\n")
    else:
        print("FAIL: broken fixture should be RED but exit=%d\n" % code)
        ok = False

    if ok:
        print("SELF-TEST PASSED")
        return 0
    print("SELF-TEST FAILED")
    return 1


if __name__ == "__main__":
    sys.exit(main())
