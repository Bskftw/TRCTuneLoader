# Release notes

All versions of TRC Tune Loader, newest first. "Not tested in game yet" means the version builds and its automatic tests pass, but nobody has tried it in CarX yet.

## 0.9.0 (Oct 22 2026)

Not tested in game yet. New options, picked by TRC.

**New**
- **TRC spec badge** while driving: a small tag in a corner of the screen. Green "TRC SPEC" with your set, amber when the car you're driving hasn't been checked yet, red "NOT TRC SPEC". On by default; turn it off or pick its corner in Options.
- **Check my car on the track**: a few seconds after you reach the track, and whenever your car changes while you drive, it's checked. If something isn't TRC spec, a short message pops up saying what (click it or press the shortcut for details). Nothing pops up when you're spec. On by default.
- **Window size** in Options: 80% to 150%, for the window, the badge and the pop-up.
- **Backup clean-up** in Options: keep all backups, or only the last 5, 10 or 20 for each car and setup. Older ones are deleted when a new backup is saved. Clean up now shows how many would go and asks again before deleting. Keeping all stays the default.
- **Only problems** filter on the Players page: lists just the players worth a look (not TRC spec, not checkable, or their own report flags something like Unity Explorer). The choice is remembered.

**Changed**
- On the Players page, a player with a CarX engine swap is now red instead of grey, and a player whose report has flagged mods says so in their row.

## 0.8.0 (Oct 22 2026)

Not tested in game yet.

**New**
- **First-run tour**: the first time you reach the garage after installing, the window opens with a short tour of every tab: what the status bar means, how to apply a tune (also in the dyno), what Check looks at, what your tune code is for, and what Players, Mod check, Backups and Options do. Skip it any time; take it again from Options.
- **Options tab**:
  - **Change shortcut**: press the keys you want to open the window with (Ctrl, Shift and Alt plus any key). Reset goes back to Ctrl + T.
  - **Take the tour** again.
  - Options are saved with KSL, so they stay after a restart or an update.

**Changed**
- The shortcut is now set in the Options tab instead of KSL's keybinds, and it works with either Ctrl key. It doesn't fire while you're typing in CarX's chat.
- After a Check that finds engine or gearbox changes in the garage, the mod reminds you that taking a KINO swap off can leave values behind, and that Apply puts CarX's own values back. In the 0.7 test, KINO left its clutch value (10000 instead of 9000) after the swap was set back to stock.

## 0.7.0 (Oct 22 2026)

Tested in game: the engine & drivetrain check caught a KINO gearbox swap (4 gears instead of 6, different ratios and final drive), KINO drivetrain changes (FWD and AWD) and a KINO 2JZ engine swap (which only changed the clutch value; engine power stayed the same). Workshop bodies aren't tested yet. Includes the 0.6.1 fix.

**New: engine & drivetrain check**
- Your car's engine, gearbox and drivetrain are compared with CarX's own data plus your tune. Anything else that changed them, like a KINO engine or gearbox swap or another mod, makes the car not TRC spec and is listed value by value.
- A CarX engine swap now also makes your own car not TRC spec (before, only the Players page flagged it). The swap engine is named.
- CarX's own drivetrain setting (RWD/AWD/4WD) is named, with the car's stock drivetrain next to it when it's changed.
- My car shows a line like "Stock engine · RWD · engine, gearbox and drivetrain match CarX's own data".
- The Players page has an "Engine & drivetrain" section for every player. Another player's KINO swap only shows there if you run KINO too.
- Every Check writes the physics engine's own values to `kino/output.log`, to confirm in game how KINO swaps work.

**New: Steam Workshop car bodies**
- My car and the Players page name every Steam Workshop (or mod.io) body.
- A body that only changes the looks is fine. One that moves the wheels (track width, axle position, ride height) is not TRC spec, the same rule as widebody kits.
- Other players' Workshop bodies are found even when your PC doesn't show them. If you have the item, the mod works out its stance from its parts. If you don't, the player shows in amber as "stance unknown".

**Changed**
- Reports between TRC Tune Loader users now include the engine & drivetrain check (report version 4). Players on 0.6 or older won't see reports from 0.7, and the other way round, so everyone should update.

## 0.6.1 (Oct 22 2026)

Never released on its own; included in 0.7.0.

**Fixed**
- After loading a TRC V3.0 tune, the live check on track said "frontDiffPreload: 100 (expected 500)". V3.0 is the only set that uses the clutch-type diff. CarX copies the rear diff's settings onto the front diff, and on a RWD car the unused front diff is reset on track. The live check now skips the diff your drivetrain doesn't use: the front diff on RWD cars, the rear diff on FWD cars. AWD and 4WD cars still check both.

## 0.6.0 (Oct 22 2026)

Tested in game: loading in an online room works (that test found the V3.0 issue fixed in 0.6.1).

**New**
- **Load tunes into CarX's tuning screen (dyno), also inside online rooms.** When the tuning screen is open, Apply becomes "Load into the dyno". It moves the sliders for you; leave the tuning screen and choose Yes to save, and in a room CarX shares the new setup with everyone.
- Restoring a backup also works in the dyno.

## 0.5.0 (Oct 22 2026)

Tested in game: the new window.

**New**
- **The TRC window**: its own look (dark with sakura pink and the TRC logo), with tabs My car, Tune sets, Players, Mod check and Backups.
- Open it with **Left Ctrl + T** (rebindable in KSL's keybinds) or from the KSL menu. Clicks over the window don't go through to the game, and the mouse cursor stays visible while it's open.
- **Status bar** on My car: always shows whether your car is TRC spec right now.
- **Body kit stance check**: a body kit (like a widebody kit) that changes the track width, axle position or ride height by 0.5 mm or more makes the car not TRC spec. Checked for your car and for every car in the room.

**Fixed**
- After Apply, the garage car now keeps its body kit's stance (it was briefly shown on the stock wheel positions).

**Changed**
- Report version 3 between TRC Tune Loader users (adds the stance check).

## 0.4.0 (Oct 22 2026)

Tested in game.

**New**
- **Five official sets built in**: TRC Revival (main and newest, 58 cars), TRC V3.0 (46), TRC V2.6 (73), TRC V2.0 (58) and TRC V1.0 (69).
- Pick which set to apply. Check, the Players page and shared reports all say which set a car matches.
- Tunes that are identical in two sets say so ("same tune as TRC V2.0").

**Changed**
- The V2.1 test set is now called **TRC Revival**, so it isn't confused with the older V3.0.
- Report version 2 between TRC Tune Loader users (adds the matched set).

## 0.3.0 (Oct 22 2026)

Tested in game with a second player: the Players page showed their car, setup check, mods and version, and flagged their changed values.

**New**
- **Official tunes built into the mod.** Players don't import or see tune files any more; the import screen and the tunes folder are gone.
- **Live car check**: Check also compares every physics value of the car you're driving (including downforce, mass, weight balance and tyre stretch) with what CarX produced from the official tune. This catches edits made with tools like Unity Explorer.
- **Players in this room**: every player's shared setup checked against the official tune.
- **Reports between TRC Tune Loader users**: mod lists, KSL status and each player's own check, shared through KSL.
- The settings a tune doesn't list must stay at the car's default.

**Fixed**
- After Apply, the garage car didn't visibly change until you went in and out of the tuning screen. It now settles on the new setup right away.
- Pressing Apply several times no longer makes duplicate backups.

## 0.2.0 (Oct 21 2026)

Tested in game: Apply, Check and backups worked.

**New**
- **Connected to CarX**: finds your car and its setup, in the garage and on track.
- **Apply** the official tune from the main garage screen, using CarX's own save. Your own tune is backed up first.
- **Check** your saved setup against the official tune, anywhere.
- **Real numbers**: every setting the way CarX's tuning screen shows it.
- **Backups** of your own tunes, with Restore.
- Reads KINO `.knd` tune files exactly, so TRC keeps tuning in game with KINO's Tune tab.

**Changed**
- Tune codes now cover the car, the setup and every value, without the sound-only settings. Codes from 0.1 changed (the example tune is now `TRC-91D4-D44C`).

## 0.1.0 (Oct 21 2026)

First build that loads in CarX.

**New**
- The mod loads in KSL as **TRCTuneLoader** and shows the example tune with its code.
- Tune codes (`TRC-XXXX-XXXX`), with an identical Python version for the Discord bot.
