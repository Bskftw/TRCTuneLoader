# Release notes

All versions of TRC Tune Loader, newest first. "Not tested in game yet" means the version builds and its automatic tests pass, but nobody has tried it in CarX yet.

## 0.10.0 (Oct 23 2026)

Partly tested in game: the course builder and a time attack on Project Dream worked, and KINO's air suspension and hydraulics were caught by the live check. The rest is not tested yet. The first part of TRC ranked battles: courses and time attack.

**New: Time attack tab**
- **Courses here**: every course on the map you're driving. Official TRC courses come with the mod; maps can also carry their own start, finish and path objects; and you can make your own practice courses. Each course has a course code, the same for everyone on the same course.
- **Time attack**: press Time attack, stop behind the start line, and a 3-2-1-GO countdown starts. Moving before GO is a jump start. The clock starts at GO and stops at the finish line, timed to the thousandth. The start and finish lines show on the road when you're within 100 m.
- **Teleport to start**: puts your car just behind the start line with CarX's own placement and gets the run ready again. Same rules as CarX's respawn.
- **Timer**: just the time, with a thin line for how far along you are. Drag it anywhere while the window is open; Options puts it back at the top.
- A run is stopped for a shortcut, leaving the course, resetting the car, changing car or changing the game speed.
- A time counts as TRC spec when your car is TRC spec at the start and the finish and no mod is flagged.
- **My times**: your best time on each course for each car and TRC tune set (the same car on two sets has two times), plus how many runs you've done. Every run is saved in `kino\mods\TRCTuneLoader\TimeAttack\runs.csv` with the tune set it used.
- **Course builder**, one step at a time: set the start line, drive the course, set the finish line, then name and save it. Your own courses are for practice.
- **Start and finish lines** are drawn in the world (a strip on the road and a post at each end), so your car hides them and they stay put when the camera moves.
- **Map courses** (Project Dream): TRC start and finish objects in the map make a downhill course and its uphill twin. TRC road objects stop a run when the whole car leaves the road for 0.5 s (in the air above the road still counts as on it). Start and finish signs hidden in the map appear while their course runs. **Record path** learns a map course's road by driving it once.

**New: battles**
- **Battle** (in the Time attack tab): challenge another TRC Tune Loader player in the room to a **side-by-side race** or a **cat and mouse** on a course of the map. A course of your own goes to them with the challenge.
- Both line up in their spot (or teleport to it); when both are ready, **3-2-1-GO** shows on both screens at the same moment. A jump start calls the countdown off for both.
- Race: the faster time wins (closer than 0.1 s is a dead heat). Cat and mouse: the chaser wins by passing and staying ahead to the finish, the leader by finishing 1.5 s or more ahead; otherwise swap places and go again.
- **TRC spec** battles need both cars TRC spec on the same tune set with no flagged mods; **Open** battles take any setup. Every battle is saved in `battles.csv`.

**New: catching KINO's air suspension and hydraulics**
- The engine & drivetrain check now also compares the suspension (spring travel, stiffness, fast and slow bump and rebound, front and rear) with CarX's own data plus the tune. In the test, KINO's air suspension and hydraulics changed the front stiffness, bump and rebound.
- The stance check now looks at all four wheels' heights, not just one, so a single lowered corner shows up too.
- The live check also compares each wheel's suspension in the physics engine.

**Fixed**
- Joining a room (or switching car in one) without pressing Check in the garage first left the car "not fully checked". The live check now records what to compare with by itself: on the main garage screen, or a few seconds after the car reaches the track.

**New: About tab**
- The story of TRC and WingsX, the TRC Ultimate Grip Tunes, Project Dream and what's coming, with a button to the mod's page.
- The tour starts with a short word about TRC, the tunes and Project Dream. Because the tour changed (TRC intro, Time attack, battles), everyone sees it once more after updating; Skip works as always.

**Changed**
- The tour has a step for the Time attack tab.
- Stance messages say the stance isn't stock instead of blaming a body kit, since KINO can change it too.
- The mod now writes its own log, `kino\mods\TRCTuneLoader\TRCTuneLoader.log` (the last session's is kept as `TRCTuneLoader.previous.log`). Everything still goes to KSL's `kino\output.log` too.

## 0.9.0 (Oct 22 2026)

Tested in game. New options, picked by TRC.

**New**
- **TRC spec badge** while driving: a small tag in a corner of the screen. Green "TRC SPEC" with your set, amber when the car you're driving hasn't been checked yet, red "NOT TRC SPEC". On by default; turn it off or pick its corner in Options.
- **Check my car on the track**: a few seconds after you reach the track, and whenever your car changes while you drive, it's checked. If something isn't TRC spec, a short message pops up saying what (click it or press the shortcut for details). Nothing pops up when you're spec. On by default.
- **Window size** in Options: 80% to 150%, for the window, the badge and the pop-up.
- **Backup clean-up** in Options: keep all backups, or only the last 5, 10 or 20 for each car and setup. Older ones are deleted when a new backup is saved. Clean up now shows how many would go and asks again before deleting. Keeping all stays the default.
- **Only problems** filter on the Players page: lists just the players worth a look (not TRC spec, not checkable, or their own report flags something like Unity Explorer). The choice is remembered.

**Changed**
- On the Players page, a player with a CarX engine swap is now red instead of grey, and a player whose report has flagged mods says so in their row.

## 0.8.0 (Oct 22 2026)

Tested in game.

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

Never released on its own; included in 0.7.0. Tested in game.

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
