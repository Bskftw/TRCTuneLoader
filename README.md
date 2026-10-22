# TRC Tune Loader

A KSL mod for CarX Drift Racing Online, made for TRC (Touge Racing Community). It puts the official TRC spec tunes on your car in one click, gives every tune a short code the TRC Discord bot uses to verify leaderboard runs, and checks your car, and every car in the room, for anything that isn't TRC spec.

**Version 0.9.0** (Oct 22 2026). What changed in each version: [CHANGELOG.md](CHANGELOG.md).

## Installing

1. You need [KSL](https://github.com/trbflxr/ksl) (the CarX mod loader) installed.
2. Download `TRCTuneLoader.zip` from the [latest release](https://github.com/Bskftw/TRCTuneLoader/releases/latest) and unzip it into `CarX Drift Racing Online\kino\mods\`, so you end up with `kino\mods\TRCTuneLoader\TRCTuneLoader.ksm`.
3. Start CarX. KSL only looks for mods when the game starts.

After that, KSL keeps the mod up to date by itself: new versions install when you start the game.

The mod is private for now: only TRC testers whitelisted in KSL can load it.

## Opening it

- Press **Ctrl + T** (you can change this in the Options tab), or
- open the KSL menu and click **TRCTuneLoader**, then **Open the TRC Tune Loader window**.

The window has six tabs: **My car**, **Tune sets**, **Players**, **Mod check**, **Backups** and **Options**. While your mouse is over it, clicks don't go through to the game.

**First time?** The first time you reach the garage after installing, the window opens with a short tour that walks you through each tab. You can skip it, and take it again any time from Options.

## Features

### My car

- **Status bar**: a coloured line that always says whether your car is TRC spec right now. Green means spec, amber means not checked yet, red means not spec.
- **Tune set picker**: choose which official set to use for your car. TRC Revival is the default.
- **Apply**: puts the official tune on your car from the main garage screen. Your own tune is backed up first, and the car visibly settles on the new setup.
- **Load into the dyno**: when CarX's tuning screen is open, Apply becomes this button. It works in the garage **and inside online rooms**. It moves CarX's sliders for you; leave the tuning screen and choose **Yes** when CarX asks to save. In a room, CarX shares the new setup with everyone.
- **Check my tune**: checks everything below and lists every difference it finds.
- **Real numbers**: every setting of the tune the way CarX's tuning screen shows it (for example spring stiffness in N/mm), plus the settings the tune doesn't list, which must stay at the car's default.
- **Your code**: the tune code of your current setup, like `TRC-95F4-06D9`. It equals the official code only when your car really matches.
- **Engine & drivetrain line**: for example "Stock engine · RWD · engine, gearbox and drivetrain match CarX's own data".
- **Workshop line**: names any Steam Workshop body on your car and whether it moves the wheels.

### What "TRC spec" means

Check makes sure all of these are true:

| Check | What it looks at |
| --- | --- |
| Saved setup | Every setting matches an official tune exactly. Settings the tune doesn't list must be at the car's default. |
| Live car | Every physics value of the car you're driving (suspension, tyres, engine, gearbox, diff, brakes, weight, downforce) still matches what CarX produced from the official tune. This catches edits made with tools like Unity Explorer. Press Check in the garage once per session to record the reference. |
| Body kit stance | No body kit (including widebody kits) changes the track width, axle position or ride height compared with the stock body. Any change of 0.5 mm or more counts. |
| Workshop bodies | A Steam Workshop (or mod.io) body is fine if it only changes the looks. One that moves the wheels is not spec. |
| Engine | No engine swap: neither CarX's own engine swap nor a KINO engine swap. |
| Gearbox & drivetrain | Gear ratios, number of gears, final drive and drivetrain (RWD/AWD/4WD) match CarX's own data plus the official tune. This catches KINO gearbox swaps and other mods. |

Some settings never count: the wastegate and gearbox sounds (pick your own), your gearbox mode and clutch settings, and the diff your drivetrain doesn't use (the front diff on a RWD car).

### Tune sets

Five official sets are built into the mod. Players can't see or change the files.

| Set | Cars |
| --- | --- |
| **TRC Revival** (main, newest) | 58 |
| TRC V3.0 | 46 |
| TRC V2.6 | 73 |
| TRC V2.0 | 58 |
| TRC V1.0 | 69 |

The tab lists every car in a set. Click one to see its numbers. Some Revival tunes are identical to V2.0, so the mod says "same tune as TRC V2.0" where that's the case.

### Players

Works in any online room. For every player, including you:

- **Setup**: their setup, as CarX shares it with the room, checked against every official set. It names the set they match, or lists the differences.
- **Engine**: CarX engine swaps are named and flagged.
- **Body kit stance and Workshop bodies**: the same checks as for your own car. If they use a Workshop body your PC doesn't have, it shows in amber as "stance unknown". Subscribe to the item and the mod can check it.
- **Engine & drivetrain**: compared with CarX's own data. Another player's KINO swap only shows here if you run KINO too, because KINO is what puts their swap on their car on your PC.
- **TRC Tune Loader report**: players who also run the mod send their own check, KSL status and full mod list. These reports are self-reported, so treat them as hints for referees, not proof.

**Only problems** hides everyone who's fine and lists just the players worth a look: not TRC spec, not checkable, or their own report flags something (like Unity Explorer). The mod remembers which you picked.

Players see each other's reports only when they run the same report version (0.7 and newer talk to each other).

### Mod check

- Every mod KSL has loaded, with version and author.
- Whether KSL says all mods are verified, and whether KSL protected mode is on.
- Flags mods whose names look like game-editing tools (Unity Explorer, Runtime Unity Editor, anything called "cheat" or "trainer").

### Backups

Before Apply or Load into the dyno, the mod backs up your own tune (unless it's already official or already backed up). The Backups tab lists the backups for your current car and setup. **Restore** puts one back, from the main garage screen or into the dyno.

Backups are saved in `kino\mods\TRCTuneLoader\Backups`.

### Options

- **Shortcut**: press **Change shortcut**, then the keys you want (for example Ctrl + T or F8). Ctrl, Shift and Alt can be combined with any key. **Reset** goes back to Ctrl + T. The shortcut doesn't fire while you're typing in CarX's chat.
- **Window size**: 80% to 150%. Also sizes the badge and the pop-up.
- **TRC spec badge** (on by default): a small tag in a corner while you drive. Green "TRC SPEC" with your set, amber when the car you're driving hasn't been checked yet (press Check my tune in the garage once per session), red "NOT TRC SPEC". Pick the corner it sits in.
- **Check my car on the track** (on by default): a few seconds after you reach the track, and whenever your car changes while you drive, it's checked. A short message pops up only if something isn't TRC spec. Click it or press the shortcut for details.
- **Backups**: keep all of them, or only the last 5, 10 or 20 for each car and setup. Older ones are deleted when a new backup is saved. **Clean up now** shows how many would go and asks again before deleting.
- **Take the tour**: the first-run tour again.

Options are saved with KSL, so they stay when you restart the game or update the mod.

## Tune codes (for the Discord bot)

The code is made from the car's ID, its setup (for example the AE86's Ultimate setup) and every tune value rounded to 3 decimals, sorted by name. The same tune always gives the same code; change any value and the code changes. Sound-only settings don't count. It looks like `TRC-XXXX-XXXX`.

- `tools/tune_code.py` makes the same code in Python for the bot. It reads `.trctune` files, KINO `.knd` files and zips of them.
- The Tune sets tab shows each official tune's code, and every tune's code is written to `kino/output.log` when the mod starts.

## Honest limits

- No check that runs on a player's own PC is cheat-proof. Someone who edits the game can also edit this mod. Replays, the mod list and the setup CarX shares with the room are the backstops.
- Reports from other TRC Tune Loader users are self-reported.
- A KINO swap on another player's car only shows if you run KINO. A Workshop body you don't have can't be checked.
- KINO is closed code, so the mod sees what a KINO swap does to the car, not the swap's name.
- Taking a KINO swap off can leave values behind (seen with the clutch). Press Apply to put CarX's own values back.
- CarX's paint screen briefly changes some engine values, so a Check while it's open can show differences.

## Feature status

| Feature | Since | Tested in game |
| --- | --- | --- |
| Apply, Check saved setup, backups and restore, real numbers | 0.2 | Yes |
| Built-in tunes, Players page, shared reports between TRC Tune Loader users | 0.3 | Yes, with a second player |
| Five tune sets with a set picker | 0.4 | Yes |
| TRC window | 0.5 | Yes |
| Live car check | 0.3 | Partly |
| Body kit stance (widebody) check | 0.5 | Not yet |
| Load into the dyno, also in online rooms | 0.6 | Yes (found the V3.0 diff issue fixed in 0.6.1) |
| Unused diff skipped in the live check | 0.6.1 | Not yet |
| Workshop bodies | 0.7 | Not yet |
| Engine & drivetrain check | 0.7 | Yes: KINO gearbox swap, KINO drivetrain change (FWD and AWD) and KINO engine swap were all caught |
| First-run tour, Options tab, changeable shortcut | 0.8 | Not yet |
| TRC spec badge, track check pop-up, window size, backup clean-up, Players filter | 0.9 | Not yet |

