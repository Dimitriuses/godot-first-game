"""Assemble a complete, runnable GDScript project from the two trees.

    python tools/web-port/assemble.py                    # into build/web-project
    python tools/web-port/assemble.py --run              # ...and run it headless

The C# tree is canonical (ROADMAP 9). `web/` holds only what genuinely differs — the
project file and the hand-written `.gd` scripts — and everything else is shared:

    web/project.godot   ->  project.godot
    web/scripts/*.gd    ->  scripts/
    scenes/*.tscn       ->  scenes/      through rewrite_scenes.py
    assets/             ->  assets/      verbatim, .import sidecars included
    shaders/            ->  shaders/     verbatim; .gdshader is language-agnostic
    icon.svg            ->  icon.svg

The output is a build artifact and is gitignored. Committing a second copy of `assets/`
is the thing this arrangement exists to avoid: it is 32 MB of spritesheets, and the 2026
cleanup was largely about undoing exactly that kind of duplication.

**The `.import` sidecars are not incidental.** They carry `compress/mode=1` — the lossy
WebP setting that takes the pack from 32.62 MB to 11.07 MB. Leave them behind and Godot
re-imports from scratch at the lossless default, and the web build silently triples the
one number the web build exists to keep down.
"""

import argparse
import os
import shutil
import subprocess
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
WEB = os.path.join(ROOT, "web")
DEFAULT_OUT = os.path.join(ROOT, "build", "web-project")

# Copied across untouched. `assets/` brings its `.import` sidecars with it; `shaders/`
# needs no port at all.
VERBATIM = ["assets", "shaders"]
VERBATIM_FILES = ["icon.svg"]


def copy_tree(src, dst):
    """Copy, skipping files already present and no older than the source.

    `assets/` is 32 MB across a few hundred files and is copied on every assemble;
    re-copying what has not changed turns a second into ten.
    """
    copied = skipped = 0
    for folder, _dirs, files in os.walk(src):
        rel = os.path.relpath(folder, src)
        target = os.path.join(dst, rel) if rel != "." else dst
        os.makedirs(target, exist_ok=True)
        for name in files:
            a, b = os.path.join(folder, name), os.path.join(target, name)
            if os.path.exists(b) and os.path.getmtime(b) >= os.path.getmtime(a):
                skipped += 1
                continue
            shutil.copy2(a, b)
            copied += 1
    return copied, skipped


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--run", action="store_true",
                    help="run the assembled project headless afterwards")
    ap.add_argument("--scene", default="", help="with --run, the scene to run")
    ap.add_argument("--godot", default=os.environ.get("GODOT_STANDARD", "godot"),
                    help="the STANDARD engine, not the .NET one: the assembled "
                         "project contains no C# and the .NET build is not needed")
    args = ap.parse_args()
    out = os.path.abspath(args.out)

    scripts = [n for n in sorted(os.listdir(os.path.join(WEB, "scripts")))
               if n.endswith(".gd")]
    if not scripts:
        raise SystemExit("web/scripts/ holds no .gd files — nothing to assemble")

    os.makedirs(out, exist_ok=True)
    print("assembling into %s\n" % out)

    shutil.copy2(os.path.join(WEB, "project.godot"), os.path.join(out, "project.godot"))
    print("%-16s%s" % ("project.godot", "from web/"))

    # The export preset has to live inside the project it exports, so it is assembled in
    # rather than pointed at. CI overrides the output path on the command line.
    preset = os.path.join(WEB, "export_presets.cfg")
    if os.path.exists(preset):
        shutil.copy2(preset, os.path.join(out, "export_presets.cfg"))
        print("%-16s%s" % ("export_presets.cfg", "from web/"))

    os.makedirs(os.path.join(out, "scripts"), exist_ok=True)
    for name in scripts:
        shutil.copy2(os.path.join(WEB, "scripts", name),
                     os.path.join(out, "scripts", name))
    print("%-16s%d file(s) from web/scripts/" % ("scripts/", len(scripts)))

    # web/scenes/ is for scenes that exist only in this tree (the smoke scene). The
    # ported ones come from the C# tree through the rewriter, never by hand.
    web_scenes = os.path.join(WEB, "scenes")
    if os.path.isdir(web_scenes):
        n, _ = copy_tree(web_scenes, os.path.join(out, "scenes"))
        print("%-16s%d file(s) from web/scenes/" % ("scenes/", n))

    # The parity harnesses (ROADMAP 9d). They are assembled in rather than run from
    # web/ because they drive the *rewritten* scenes, which only exist here.
    web_tests = os.path.join(WEB, "tests")
    if os.path.isdir(web_tests):
        n, _ = copy_tree(web_tests, os.path.join(out, "tests"))
        print("%-16s%d file(s) from web/tests/" % ("tests/", n))

    result = subprocess.run(
        [sys.executable, os.path.join(ROOT, "tools", "web-port", "rewrite_scenes.py"),
         "--out", os.path.join(out, "scenes")],
        capture_output=True, text=True, cwd=ROOT)
    if result.returncode != 0:
        sys.stderr.write(result.stdout + result.stderr)
        raise SystemExit("the scene rewriter failed — see above")
    rewritten = sum(1 for line in result.stdout.splitlines() if ".tscn" in line)
    print("%-16s%d rewritten from scenes/" % ("", rewritten))

    for folder in VERBATIM:
        copied, skipped = copy_tree(os.path.join(ROOT, folder),
                                    os.path.join(out, folder))
        print("%-16s%d copied, %d already current" % (folder + "/", copied, skipped))
    for name in VERBATIM_FILES:
        shutil.copy2(os.path.join(ROOT, name), os.path.join(out, name))
        print("%-16s%s" % (name, "verbatim"))

    if not args.run:
        print("\nrun it with:  %s --headless --path %s" % (args.godot, out))
        return 0

    cmd = [args.godot, "--headless", "--path", out]
    if args.scene:
        cmd.append(args.scene)
    else:
        cmd += ["--quit-after", "240"]
    print("\n$ %s\n" % " ".join(cmd))
    return subprocess.run(cmd).returncode


if __name__ == "__main__":
    sys.exit(main())
