# TRC Tune Loader

A KSL mod for CarX Drift Racing Online, made for TRC (Touge Racing Community). It puts the official TRC spec tunes on your car in one click, gives every tune a short code the TRC Discord bot uses to verify leaderboard runs, and checks your car, and every car in the room, for anything that isn't TRC spec. It also times your runs on TRC courses with a 3-2-1-GO time attack.

**Version 0.10.1** (Oct 23 2026). What changed in each version: [CHANGELOG.md](CHANGELOG.md).

## Installing

1. You need [KSL](https://github.com/trbflxr/ksl) (the CarX mod loader) installed.
2. Download `TRCTuneLoader.zip` from the [latest release](https://github.com/Bskftw/TRCTuneLoader/releases/latest) and unzip it into `CarX Drift Racing Online\kino\mods\`, so you end up with `kino\mods\TRCTuneLoader\TRCTuneLoader.ksm`.
3. Start CarX. KSL only looks for mods when the game starts.

After that, KSL keeps the mod up to date by itself: new versions install when you start the game. TRC Tune Loader is also listed in KSL's ModDB.

## Opening it

- Press **Ctrl + T** (you can change this in the Options tab), or
- open the KSL menu and click **TRCTuneLoader**, then **Open the TRC Tune Loader window**.

The window has eight tabs: **My car**, **Tune sets**, **Players**, **Mod check**, **Backups**, **Time attack**, **Options** and **About**. While your mouse is over it, clicks don't go through to the game.

**First time?** The first time you reach the garage after installing, the window opens with a short tour: a quick word about TRC, the tunes and Project Dream, then each tab. You can skip it, and take it again any time from Options.

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
| Live car | Every physics value of the car you're driving (suspension, tyres, engine, gearbox, diff, brakes, weight, downforce, and each wheel's suspension in the physics engine) still matches what CarX produced from the official tune. This catches edits made with tools like Unity Explorer. The mod records what to compare with by itself, a few seconds after your car reaches the garage or the track. |
| Stance | No body kit (including widebody kits) changes the track width, axle position or ride height compared with the stock body, and no single wheel sits at another height. Any change of 0.5 mm or more counts. |
| Workshop bodies | A Steam Workshop (or mod.io) body is fine if it only changes the looks. One that moves the wheels is not spec. |
| Engine | No engine swap: neither CarX's own engine swap nor a KINO engine swap. |
| Gearbox & drivetrain | Gear ratios, number of gears, final drive and drivetrain (RWD/AWD/4WD) match CarX's own data plus the official tune. This catches KINO gearbox swaps and other mods. |
| Suspension | Spring travel, spring stiffness, and fast and slow bump and rebound, front and rear, match CarX's own data plus the official tune. This catches KINO's air suspension and hydraulics, on your car and on other players' cars when you run KINO. |

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
- **Engine, drivetrain & suspension**: compared with CarX's own data. Another player's KINO swap or air suspension only shows here if you run KINO too, because KINO is what puts it on their car on your PC.
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

### Time attack

The Time attack tab shows the map you're driving on and has four parts: Courses here, Battle, My times and Course builder.

**Courses here** lists every course on this map:

| Kind of course | Where it comes from | Counts as TRC spec time |
| --- | --- | --- |
| Official TRC course | Built into the mod | Yes |
| Official (map objects) | Start, finish and path placed in the map by its maker, on maps TRC trusts | Yes |
| Map objects (not official) | The same, on other maps | Practice only |
| Your course | Made with the Course builder on your PC | Practice only |

Each course shows its kind (touge or highway), its length, its course code (like `CRS-1A2B-3C4D`, the same for everyone on the same course) and your best time, with the car and tune set. A downhill course from the map also comes as an uphill course, the same road the other way.

A map course that doesn't know its road yet shows **Record path**: drive through its start line and down the middle of the road to its finish line, and the mod remembers the road (on your PC, for practice). Official courses come with it already.

**Running a time attack:**

1. Press **Time attack** next to a course. The window closes. The start line (green) and finish line (pink) show on the road, with a post at each end, when you're within 100 m of them. On maps with their own start and finish signs, the signs appear instead.
2. Drive to the start and stop behind the start line, between its ends, facing the way the course goes. Or open the window and press **Teleport to start**: CarX's own placement puts you just behind the line.
3. Hold still: a **3-2-1-GO** countdown starts. Moving before GO is a **jump start**, and you stop and try again.
4. The clock starts at GO. A small timer shows your time, with a thin line under it for how far along the course you are.
5. Cross the finish line to stop the clock. Your time, how it compares with your best in the same car on the same tune set, and whether it counts show for a few seconds. Then the run is ready for another go. **Teleport to start** restarts any time.

A run is stopped if you take a shortcut, leave the course, reset the car, change car or the game speed changes. On maps that mark their roads (like Project Dream), the whole car leaving the road for half a second stops it too. Part of the car still over the road, or flying over it off a jump, counts as on the road. **Stop time attack** ends it.

**Battle** challenges another player in the same online room who runs TRC Tune Loader 0.10.1 or newer:

1. Pick the opponent, a course on this map, the format and the mode, and press **Challenge**. They see the challenge on screen and press the shortcut to answer it. A course of your own, or your recorded path, is sent to them with the challenge. You see when it gets to them, and why if they can't accept it. If you both challenge each other at once to the same course, format and mode, it becomes one battle by itself.
2. Both drive to the start, or press **Teleport to my spot**. In a race you line up side by side (the challenger on the left); in cat and mouse the leader is just behind the line and the chaser further back.
3. Stop in your spot. When both are ready, **3-2-1-GO** shows on both screens at the same moment (the mods measure the connection between you). Moving before GO calls the countdown off for both.
4. Each mod times its own car from GO to the finish line, with the same rules as time attack (shortcuts, leaving the road and resets stop your run).

| Format | Who wins |
| --- | --- |
| Side-by-side race | The faster time. Closer than 0.1 s is a dead heat: run it again. |
| Cat and mouse | The chaser wins a run by passing and staying ahead to the finish. The leader wins by finishing far enough ahead: 1.5 s while you both have your first lead, 1 s for your second, then 0.5 s. Otherwise you swap places and go again; the first run with a winner wins. |

A driver who doesn't finish loses the run to one who does. **TRC spec** battles need both cars TRC spec on the same tune set, with no flagged mods, at every start and finish; **Open** battles take any setup and don't count. After ten runs without a winner it's a draw. If the other driver leaves or stops answering for 10 seconds, the battle ends. Every battle is saved in `kino\mods\TRCTuneLoader\TimeAttack\battles.csv`; ratings come later.

**Moving the timer:** while a time attack runs, open the window and drag the timer anywhere on the screen. It stays there; Options can put it back at the top.

A time counts as **TRC spec** when your car is TRC spec at the start and at the finish (the same check as My car) and no mod is flagged. Other times are kept too, marked as not counted.

**My times** lists, for every course you've driven, your best time for each car and TRC tune set (a car on TRC Revival and the same car on V3.0 have separate times), your fastest time that didn't count, and how many runs you've done. Every finished run is saved in `kino\mods\TRCTuneLoader\TimeAttack\runs.csv`, with the tune set it used.

**Course builder** makes your own practice courses on any map, one step at a time:

1. **Start**: pick how wide the start and finish lines are, stop with the middle of your car on the start line, and press **Set start here**.
2. **Drive**: close the window and drive the course down the middle of the road. The route is recorded as you go; resetting the car loses it.
3. **Finish**: stop with the middle of your car on the finish line and press **Set finish here**.
4. **Save**: name it (the car is stopped, so typing doesn't move it), pick Touge or Highway, and press **Save course**.

Your courses are saved in `kino\mods\TRCTuneLoader\Courses` and can be deleted from Courses here.

### Options

- **Shortcut**: press **Change shortcut**, then the keys you want (for example Ctrl + T or F8). Ctrl, Shift and Alt can be combined with any key. **Reset** goes back to Ctrl + T. The shortcut doesn't fire while you're typing in CarX's chat.
- **Window size**: 80% to 150%. Also sizes the badge and the pop-up.
- **TRC spec badge** (on by default): a small tag in a corner while you drive. Green "TRC SPEC" with your set, amber for the few seconds before the car you're driving has been checked, red "NOT TRC SPEC". Pick the corner it sits in.
- **Time attack timer**: drag it while the window is open during a run. **Put the timer back at the top** resets it.
- **Check my car on the track** (on by default): a few seconds after you reach the track, and whenever your car changes while you drive, it's checked. A short message pops up only if something isn't TRC spec. Click it or press the shortcut for details.
- **Backups**: keep all of them, or only the last 5, 10 or 20 for each car and setup. Older ones are deleted when a new backup is saved. **Clean up now** shows how many would go and asks again before deleting.
- **Take the tour**: the first-run tour again.

Options are saved with KSL, so they stay when you restart the game or update the mod.

### About

The story behind the mod: TRC and its founder WingsX, the TRC Ultimate Grip Tunes and why they're balanced the way they are, Project Dream and how its roads came together, and what's coming next. There's also a button to this mod's page.

## Tune codes (for the Discord bot)

The code is made from the car's ID, its setup (for example the AE86's Ultimate setup) and every tune value rounded to 3 decimals, sorted by name. The same tune always gives the same code; change any value and the code changes. Sound-only settings don't count. It looks like `TRC-XXXX-XXXX`.

- `tools/tune_code.py` makes the same code in Python for the bot. It reads `.trctune` files, KINO `.knd` files and zips of them.
- The Tune sets tab shows each official tune's code, and every tune's code is written to the mod's log when it starts.

## Log

The mod writes what it does to its own log, `kino\mods\TRCTuneLoader\TRCTuneLoader.log` (the last session's is kept as `TRCTuneLoader.previous.log`), and to KSL's log, `kino\output.log`. If something goes wrong, those files help find out why.

## Honest limits

- No check that runs on a player's own PC is cheat-proof. Someone who edits the game can also edit this mod. Replays, the mod list and the setup CarX shares with the room are the backstops.
- Reports from other TRC Tune Loader users are self-reported.
- A KINO swap or air suspension on another player's car only shows if you run KINO. A Workshop body you don't have can't be checked.
- KINO is closed code, so the mod sees what a KINO swap does to the car, not the swap's name.
- Taking a KINO swap off can leave values behind (seen with the clutch). Press Apply to put CarX's own values back.
- CarX's paint screen briefly changes some engine values, so a Check while it's open can show differences.
- Time attack times are saved on your own PC only, for now.

## Feature status

| Feature | Since | Tested in game |
| --- | --- | --- |
| Apply, Check saved setup, backups and restore, real numbers | 0.2 | Yes |
| Built-in tunes, Players page, shared reports between TRC Tune Loader users | 0.3 | Yes, with a second player |
| Five tune sets with a set picker | 0.4 | Yes |
| TRC window | 0.5 | Yes |
| Live car check | 0.3 | Yes |
| Body kit stance (widebody) check | 0.5 | Yes |
| Load into the dyno, also in online rooms | 0.6 | Yes (found the V3.0 diff issue fixed in 0.6.1) |
| Unused diff skipped in the live check | 0.6.1 | Yes |
| Workshop bodies | 0.7 | Not yet: no Workshop car to test with |
| Engine & drivetrain check | 0.7 | Yes: KINO gearbox swap, KINO drivetrain change (FWD and AWD) and KINO engine swap were all caught |
| First-run tour, Options tab, changeable shortcut | 0.8 | Yes |
| TRC spec badge, track check pop-up, window size, backup clean-up, Players filter | 0.9 | Yes |
| Time attack tab: 3-2-1-GO, My times, Course builder, courses from map objects | 0.10 | Course builder and a time attack on Project Dream: yes. Teleport, movable timer, step-by-step builder, map objects: not yet |
| Suspension check (KINO air suspension and hydraulics), live check recorded by itself | 0.10 | KINO air and hydraulics caught by the live check: yes. Suspension check from CarX data: not yet |
| Battles: side-by-side race and cat and mouse, synced 3-2-1-GO | 0.10 | A side-by-side race on Akina Downhill: yes. Cat and mouse: not yet |
| Battle challenges on courses with a path, challenging each other at once | 0.10.1 | Not yet |

