# Asset provenance

The MIT licence in `LICENSE` covers the **code and project files only**. The artwork below
came from elsewhere and keeps its own terms. This file records what each asset is, where it
came from, and how that was established — by measurement where possible.

## `assets/kenney-boardgame/` — CC0, no restrictions

| File | Source |
|---|---|
| `chip-red-white.png` | Boardgame pack v2 by Kenney Vleugels — www.kenney.nl |
| `piece-black-border04.png` | same pack |

The pack's own licence file ships alongside them as `LICENSE.txt`:

> CC0 License (Creative Commons Zero) — You may use these graphics in personal and
> commercial projects. Credit (Kenney or www.kenney.nl) would be nice but is not mandatory.

Credit is given here voluntarily. **Thanks to Kenney.**

A second Kenney pack, *Board Game Icons (1.1)* (also CC0), was committed to this repository
in 2025 and has been removed — not one of its 1,543 files was referenced by any scene.

## `assets/petixel-prototype/` — origin not established

| File | Used for |
|---|---|
| `tileset-prototype-b.png` | the board floor and walls |
| `prototype-characters.png` | the figures around the edge of the board |

These came from a pack named *"Petixel Prototype 48x48"*. **No licence file was included
with the copy in this repository**, and the PNGs carry no metadata identifying the author or
terms. The pack was not identified with confidence, so no claim is made about its licence.
If you are the author, please open an issue and it will be credited or removed.

## `assets/dice/` — derived from a third-party sticker pack, terms unknown ⚠️

The eight spritesheets that make up the die (`dice_1_sprites.png` … `dice_6_sprites.png`,
`dice_idle0_sprites.png`, `dice_idle1_sprites.png`, 18.6 MB in total, 5120×5120 each) are
**derived from someone else's artwork and are not covered by this repository's MIT
licence.**

They were **reworked by this repository's author from a custom Telegram sticker pack** — a
user-made pack, not Telegram's own stock stickers, containing images that do not appear in
Telegram itself. The underlying pack's author and licence are not known.

The conversion pipeline is recorded in the files themselves. Their PNG `tEXt` chunks read:

```
Comment   PNG converted with https://ezgif.com/gif-to-sprite
Software  ezgif.com
```

and the intermediate GIF committed alongside them (`dice_1.gif`, since removed) carried:

```
GIF converted with https://ezgif.com/tgs-to-gif
```

`.tgs` is Telegram's animated-sticker format, so the chain was **custom sticker pack → GIF
→ spritesheet**, both conversions done on ezgif.com, with the author's own rework on top.

**This is a disclosure, not a licence.** Nobody granted redistribution rights over the
source artwork, and publishing this repository redistributes a derivative of it. The frames
are kept here at the repository owner's decision, with this notice.

**The intended fix is to redraw them, not to substitute them.** This animation is the
visual centrepiece of the project — a tumbling die with a 3D look and deliberately
exaggerated rotation — and dropping in a flat CC0 dice sprite would lose the thing that
makes the toy worth looking at. Regenerating the animation from scratch is tracked as its
own item in [../ROADMAP.md](../ROADMAP.md); it is deferred because doing it properly is
expensive, not because the provenance question is settled.

## `icon.svg`

The stock Godot Engine project icon, unmodified.
