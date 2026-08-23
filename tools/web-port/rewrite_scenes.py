"""Rewrite the C# scenes into their GDScript equivalents.

    python tools/web-port/rewrite_scenes.py --out build/web-project/scenes
    python tools/web-port/rewrite_scenes.py --check      # what would change, no writing

Three things have to happen, and only three:

  1. every script `ExtResource` repointed from `res://scripts/Dice.cs` to
     `res://scripts/dice.gd`, and its `uid=` dropped — the `.gd` file gets its own
     when Godot imports it, and carrying the C# one over points at nothing;
  2. every exported property key renamed from the C# `AnimatedSprite` to the GDScript
     `animated_sprite`;
  3. nothing else touched at all — in particular the 5,040 `AtlasTexture` regions,
     which are the bulk of these files and are language-agnostic.

**Why this is a tool and not a hand edit.** `dice.tscn` is 3,400 lines of which 420 are
atlas regions; `d20.tscn` is 10,000 and 1,260. Nobody maintains a second copy of that.

**Why the rename is the dangerous part.** A scene that names a property the script does
not have loads *successfully*, with that export left null, and reports nothing. The
symptom arrives later and somewhere else — a null `AnimatedSprite` is a die that never
draws. So the mapping is derived from the C# sources rather than written down here, and
an exported key this tool cannot account for is a hard error rather than a passthrough.
"""

import argparse
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
SCENES = os.path.join(ROOT, "scenes")
SCRIPTS = os.path.join(ROOT, "scripts")

# `[Export] public AnimatedSprite2D AnimatedSprite;` and the `= default` form.
EXPORT = re.compile(
    r"\[Export\][^\n]*?\bpublic\s+[\w<>\[\],.?]+\s+(?P<name>[A-Z]\w*)\s*[;={]")

# A scene's script reference, with the uid Godot wrote for the .cs file.
SCRIPT_REF = re.compile(
    r'\[ext_resource type="Script"(?P<uid>\s+uid="[^"]*")?'
    r'\s+path="res://scripts/(?P<file>\w+)\.cs"')

# An exported property assignment: a PascalCase key at the start of a line.
PROPERTY = re.compile(r"^(?P<name>[A-Z]\w*) = ", re.MULTILINE)

# A method declared in the C# script, so a connection can be checked against something
# rather than renamed on faith.
METHOD = re.compile(
    r"(?:public|private|protected|internal)\s+"
    r"(?:static\s+|override\s+|virtual\s+|async\s+|sealed\s+|partial\s+)*"
    r"[\w<>\[\],.?]+\s+(?P<name>[A-Z]\w*)\s*\(")

# The *third* place a scene names something in the script, and the one that was missed
# first time round:
#
#     [connection signal="button_down" from="Button" to="." method="OnSpawnButton"]
#
# The Respawn button did nothing at all in the first web build because of this line. It
# is quieter than the others: the scene loads, the button draws, it just never calls
# anything.
CONNECTION = re.compile(r'(?P<head>\[connection [^\]]*?method=")(?P<name>[A-Z]\w*)(?P<tail>")')

# The *other* place a scene names an exported property, and the one that is easy to
# miss because it does not look like an assignment:
#
#     [node name="Dice" type="RigidBody2D" node_paths=PackedStringArray(
#         "AnimatedSprite", "CollisionShape")]
#
# It declares which of the node's exports are NodePaths. Rename the assignments below
# and leave this alone and the scene still loads, still reports nothing, and binds
# neither path — a die with no sprite and no collider.
NODE_PATHS = re.compile(r'node_paths=PackedStringArray\((?P<names>[^)]*)\)')


def snake(name):
    """`AnimatedSprite` -> `animated_sprite`, the way Godot's own bindings pair them.

    Two capitals in a row are one word, so `UiSkin` is `ui_skin` but `DiceHUD` would be
    `dice_hud` rather than `dice_h_u_d`.
    """
    out = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", name)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", out).lower()


def exports_by_script():
    """Every `[Export]` in scripts/, as {script stem: {C# name: gdscript name}}.

    Read out of the C# rather than listed here, so adding an export to a scene cannot
    silently outrun this tool.
    """
    found = {}
    for name in sorted(os.listdir(SCRIPTS)):
        if not name.endswith(".cs"):
            continue
        text = open(os.path.join(SCRIPTS, name), encoding="utf-8").read()
        stem = name[:-3]
        found[stem] = {
            "exports": {m.group("name"): snake(m.group("name"))
                        for m in EXPORT.finditer(text)},
            "methods": {m.group("name"): snake(m.group("name"))
                        for m in METHOD.finditer(text)},
        }
    return found


def rewrite(text, exports, source):
    """One scene's text, rewritten. Raises on anything unaccounted for."""
    scripts_used = []

    def fix_script(m):
        scripts_used.append(m.group("file"))
        # The uid is dropped rather than replaced: Godot mints one for the .gd on
        # import, and a stale uid pointing at a .cs that is not in the tree resolves
        # to nothing.
        return ('[ext_resource type="Script" path="res://scripts/%s.gd"'
                % snake(m.group("file")))

    text = SCRIPT_REF.sub(fix_script, text)
    if not scripts_used:
        raise SystemExit("%s: no C# script reference found — already rewritten?" % source)

    # Only the exports of scripts this scene actually attaches can legitimately appear.
    allowed = {}
    callable_methods = {}
    for stem in scripts_used:
        if stem not in exports:
            raise SystemExit("%s: references scripts/%s.cs, which does not exist"
                             % (source, stem))
        allowed.update(exports[stem]["exports"])
        callable_methods.update(exports[stem]["methods"])

    named = {m.group("name") for m in PROPERTY.finditer(text)}
    for m in NODE_PATHS.finditer(text):
        named.update(re.findall(r'"([^"]+)"', m.group("names")))
    unknown = sorted(named - set(allowed))
    if unknown:
        raise SystemExit(
            "%s: exported key(s) %s are not [Export]s of %s.\n"
            "A scene naming a property the script lacks loads silently with that export\n"
            "left null, so this is a hard error rather than a passthrough."
            % (source, ", ".join(unknown), "/".join(scripts_used)))

    # Connections, checked against the script's own methods rather than renamed on
    # faith. A connection to a method that is not there is the quietest failure in this
    # whole file: the scene loads, the control draws, and pressing it does nothing.
    unknown_methods = sorted({m.group("name") for m in CONNECTION.finditer(text)}
                             - set(callable_methods))
    if unknown_methods:
        raise SystemExit(
            "%s: connection(s) call %s, which %s does not declare.\n"
            "A connection to a missing method leaves a control that draws and does\n"
            "nothing, so this is a hard error rather than a passthrough."
            % (source, ", ".join(unknown_methods), "/".join(scripts_used)))
    text = CONNECTION.sub(
        lambda m: "%s%s%s" % (m.group("head"), callable_methods[m.group("name")],
                              m.group("tail")), text)

    text = PROPERTY.sub(lambda m: "%s = " % allowed[m.group("name")], text)
    text = NODE_PATHS.sub(
        lambda m: 'node_paths=PackedStringArray(%s)' % re.sub(
            r'"([^"]+)"', lambda n: '"%s"' % allowed[n.group(1)], m.group("names")),
        text)
    return text, allowed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", help="directory to write the rewritten scenes into")
    ap.add_argument("--check", action="store_true",
                    help="report what would change and write nothing")
    args = ap.parse_args()
    if not args.out and not args.check:
        ap.error("pass --out, or --check to see what would happen")

    exports = exports_by_script()
    total = sum(len(v["exports"]) for v in exports.values())
    methods = sum(len(v["methods"]) for v in exports.values())
    print("%d [Export]s and %d methods across %d C# scripts"
          % (total, methods, len(exports)))

    if args.out:
        os.makedirs(args.out, exist_ok=True)

    print("\n%-14s%10s%10s%s" % ("scene", "lines", "renamed", "  properties"))
    for name in sorted(os.listdir(SCENES)):
        if not name.endswith(".tscn"):
            continue
        text = open(os.path.join(SCENES, name), encoding="utf-8").read()
        out, allowed = rewrite(text, exports, name)
        renamed = sorted({m.group("name") for m in PROPERTY.finditer(text)})
        wired = sorted({m.group("name") for m in CONNECTION.finditer(text)})
        print("%-14s%10d%10d  %s" % (name, text.count("\n") + 1, len(renamed) + len(wired),
                                     ", ".join(["%s->%s" % (r, allowed[r])
                                                for r in renamed]
                                               + ["%s()" % w for w in wired])))
        if args.out:
            with open(os.path.join(args.out, name), "w",
                      encoding="utf-8", newline="\n") as f:
                f.write(out)

    if args.out:
        print("\nwritten to %s" % os.path.normpath(args.out))
    else:
        print("\n(--check: nothing written)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
