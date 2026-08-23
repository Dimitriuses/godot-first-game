"""Run the web tree locally: assemble, import, and drive every harness in it.

    python tools/web-port/check.py                 # the whole thing
    python tools/web-port/check.py --quick         # skip the re-import
    python tools/web-port/check.py --serve         # serve an exported build, if there is one

This is the local half of ROADMAP 9d. CI runs the same command, so a red build here is a
red build there and there is nothing to reproduce.

**Why this exists at all.** The web build is produced by CI, which is the right place for
it — it fetches its own export templates and it proves the deployment rather than an
approximation of it. But waiting for CI to find a port bug is a slow way to find a port
bug, and most of them are not deployment bugs: a mistyped property, a signal wired to
nothing, a scene key the rewriter missed. All of those are visible from a headless run of
the assembled project, which needs no export templates at all — running a GDScript
project needs none, only exporting does.

So: everything except the export is checkable here, in about fifteen seconds.

Exit code is 0 only if every harness passed, so this can be a gate rather than a report.
"""

import argparse
import os
import subprocess
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
HERE = os.path.dirname(__file__)
BUILD = os.path.join(ROOT, "build", "web-project")
EXPORTED = os.path.join(ROOT, "build", "web")


def godot_binary(given):
    """The STANDARD engine. The .NET one would also run this project, but using it here
    would quietly prove the wrong thing: the whole point of the GDScript tree is that it
    does not need .NET, and a check that passes only under the .NET build has not checked
    that."""
    found = given or os.environ.get("GODOT_STANDARD") or "godot"
    result = subprocess.run([found, "--version"], capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(
            "cannot run '%s'.\nSet GODOT_STANDARD to the standard (non-.mono) engine, "
            "or pass --godot." % found)
    version = result.stdout.strip().splitlines()[-1]
    if ".mono" in version:
        raise SystemExit(
            "'%s' is the .NET build (%s).\nThis tree must be checked with the standard "
            "engine — see the note in godot_binary()." % (found, version))
    return found, version


def harnesses():
    """Every *.tscn under web/tests/, which is what the assembler puts in tests/."""
    folder = os.path.join(ROOT, "web", "tests")
    if not os.path.isdir(folder):
        return []
    return sorted("res://tests/%s" % n for n in os.listdir(folder)
                  if n.endswith(".tscn"))


def parse_check(godot):
    """Parse every script explicitly. Returns the number that failed.

    **The import step is not enough, and trusting it cost a false pass here.** Godot
    re-parses a script on import only when it thinks the script has changed; once the
    cache is warm, a file with a genuine parse error can import "clean" and the harnesses
    then pass because nothing they touch loads it. `dice_hud.gd` did exactly that — it
    failed on one run, was reported clean on the next with the error still in it, and was
    only caught by asking the engine about the file directly.

    `--check-only --script` gives a straight answer per file and does not consult the
    cache, so this is the check that actually means something. It costs about a second
    per script.
    """
    scripts = sorted(n for n in os.listdir(os.path.join(ROOT, "web", "scripts"))
                     if n.endswith(".gd"))
    print("\n--- parse (%d scripts) ---" % len(scripts))
    failed = 0
    for name in scripts:
        result = subprocess.run(
            [godot, "--headless", "--path", BUILD,
             "--check-only", "--script", "res://scripts/%s" % name],
            capture_output=True, text=True)
        bad = [line for line in (result.stdout + result.stderr).splitlines()
               if "Parse Error" in line or "Failed to load script" in line]
        if bad:
            failed += 1
            print("  FAIL  %s" % name)
            for line in bad[:4]:
                print("        %s" % line.strip())
    if failed:
        print("\n%d script(s) do not parse" % failed)
    else:
        print("  all %d parse" % len(scripts))
    return failed


def serve():
    """Serve an exported build for a look in a real browser.

    Only useful once CI has produced one and it has been unpacked into build/web/ —
    nothing here can export, because the standard engine's web templates are a separate
    ~1 GB download and CI fetches its own.

    Plain `http.server` is enough **because the build is single-threaded**: that export
    does not use SharedArrayBuffer and so needs none of the COOP/COEP headers. That is
    the same property that lets it work on GitHub Pages, which cannot set headers either
    — so if it runs here it will run there, and this is not a lenient local approximation.
    """
    if not os.path.isdir(EXPORTED):
        raise SystemExit(
            "no exported build at %s.\nCI produces it; download the Pages artifact and "
            "unpack it there, then run this again." % EXPORTED)
    import http.server
    import socketserver
    os.chdir(EXPORTED)
    with socketserver.TCPServer(("", 8060), http.server.SimpleHTTPRequestHandler) as srv:
        print("serving %s at http://localhost:8060/  (ctrl-c to stop)" % EXPORTED)
        srv.serve_forever()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--godot", default="")
    ap.add_argument("--quick", action="store_true",
                    help="skip the re-import; wrong after assets or scripts change")
    ap.add_argument("--serve", action="store_true",
                    help="serve build/web/ instead of running anything")
    args = ap.parse_args()

    if args.serve:
        return serve()

    godot, version = godot_binary(args.godot)
    print("engine   %s" % version)

    print("\n--- assemble ---")
    if subprocess.run([sys.executable, os.path.join(HERE, "assemble.py")],
                      cwd=ROOT).returncode != 0:
        return 1

    if not args.quick:
        print("\n--- import ---")
        result = subprocess.run([godot, "--headless", "--path", BUILD, "--import"],
                                capture_output=True, text=True)
        bad = [line for line in (result.stdout + result.stderr).splitlines()
               if "SCRIPT ERROR" in line or "Parse Error" in line]
        if bad:
            print("\n".join(bad[:20]))
            print("\n%d parse/script error(s) — the port does not load" % len(bad))
            return 1
        print("imported clean")

    if parse_check(godot):
        return 1

    checks = harnesses()
    if not checks:
        print("\nno harnesses in web/tests/ — nothing to check")
        return 1

    print("\n--- %d harness(es) ---" % len(checks))
    failed = []
    for scene in checks:
        result = subprocess.run(
            [godot, "--headless", "--path", BUILD, scene, "--quit-after", "8000"],
            capture_output=True, text=True)
        output = result.stdout + result.stderr
        # The harnesses print their own detail; echo it so a failure is diagnosable from
        # the log alone, which is all CI leaves behind.
        for line in output.splitlines():
            if line.startswith("  ") or "checks passed" in line or "FAILED" in line:
                print(line)
        if result.returncode != 0 or "FAIL" in output:
            failed.append(scene)
        print()

    if failed:
        print("FAILED: %s" % ", ".join(failed))
        return 1
    print("all harnesses passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
