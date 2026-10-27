# Release notes

All versions of TRC Tune Loader, newest first. "Not tested in game yet" means the version builds and its automatic tests pass, but nobody has tried it in CarX yet.

## 0.14.2 (Oct 27 2026)

Not tested in game yet. Tidier files, thanks to a player's report.

**Fixed**
- The mod's files (times, courses, backups, logs) now always go in one folder, `kino\mods\TRCTuneLoader`, wherever KSL installed the mod. Before, when the mod was installed loose in `kino\mods`, its TimeAttack, Courses and Backups folders were made loose there too. They're moved into `kino\mods\TRCTuneLoader` by themselves the first time you start this version, with nothing lost.
- Updates no longer leave a second copy of `TRCTuneLoader.ksm` behind. If you already have two, the Home tab says which one to delete.

## 0.14.1 (Oct 26 2026)

Not tested in game yet. A small update; works with 0.14.0 and 0.13.0 players as before.

**Changed**
- **TRC spec badge** and **Check my car on the track** are now off by default. Turn them on in Options (While driving) if you want them.
- **Home's Go to** is grouped into the four parts of the mod, each in its colour: Tunes & TRC spec, Time attack & course builder, Battles, races & tournaments, and Speed traps, room games & ratings. New buttons for Tune sets, Players, Speed traps and My ratings.
- The window's header shows what the mod does: official TRC tunes, time attack, course builder, battles & races.

## 0.14.0 (Oct 26 2026)

Not tested in game yet. A Home tab, the room board, sharing courses, wheel buttons, Run it back and Report a bug. Battles and races work with 0.13.0 players as before; the room board and shared courses need 0.14.0.

**New: Home**
- The window opens on **Home**: buttons to every part of the mod (practice, challenge someone, races and room games, my times and ratings, make a course, put a TRC tune on), and anything waiting for you: challenges, and races you can join.
- **What's new** shows on Home after an update. The window opens by itself once, in the garage.
- **New tour** covering everything since 0.10 (Options, Take the tour). New players get it the first time.

**New: room board**
- See what every TRC Tune Loader player in the room is doing: in the garage, free roam, time attack, battling, racing, in a race lobby, playing a room game, or making a course.
- Tick **I'm up for a battle** so others know. **Challenge** or **Join** them in one click.

**New: share a course**
- In a course's **Edit**, **Share with the room** sends it, with its speed traps and grid, to everyone in the room. It shows up on their Home, ready to save.

**New: wheel and controller buttons** (Options)
- A **TRC action** button that does what fits right now: accepts a challenge, joins a race that just opened, runs it back, or restarts your time attack.
- A button that opens and closes the window.
- Pick buttons CarX doesn't use: CarX still sees every press.

**New: Run it back**
- After a battle, **Run it back** challenges the same driver again with the same course and format. After a race, the host's **Run it back** starts the same race again with the same drivers.

**New: Report a bug** (Home and Options)
- Say what happened and press **Make a bug report**: a zip lands on your Desktop with the mod's logs, what you were doing, your options and your mods. Send it to bskftw.

**Changed**
- Your times, battles and races are also saved, ready for the TRC server when it comes.

## 0.13.0 (Oct 25 2026)

Not tested in game yet. Speed traps, ratings and licenses, your best line on the road, race control, room games and highway battles. Everyone needs 0.13.0, for races and for battles.

**New: speed traps**
- **Free roam:** drive through a speed trap anywhere on the map and your speed flashes on screen, NFS Most Wanted style: **purple** for a new best on that trap, **green** close to it, **amber** slower. Every trap keeps your best speed.
- **Courses here, Speed traps:** your traps and bests on this map, **Free-roam traps on / Off**, and **Add speed trap here** to place your own where your car is.
- **Traps on a course:** in a course's **Edit**, add speed traps along the road. They flash during time attacks, battles and races, and your trap speeds are saved with the time.
- **Speed trap races:** the host can score a race on the total of everyone's trap speeds instead of the time.
- Maps can have their own speed traps, on the road or on a course.

**New: ratings and licenses**
- An **Elo rating** for touge, highway and circuit, moved by TRC spec battles and races.
- A **Safety Rating** like iRacing: clean kilometres raise it, incidents lower it (resets, hits, possible contact, leaving the road, jump starts).
- **Licenses:** Rookie, D, C, B, A. Keep your Safety Rating at 3.00 or more over enough events to go up; below 1.00 you go down one.
- **My times, Ratings** shows yours and the latest changes. Other TRC Tune Loader players see your license and Elo on the Players page and in races.
- Kept on your PC for now. They'll be checked and shared once the TRC server is up.

**New: your best line**
- During a time attack, where you drove on your best run (or best lap) in this car shows as dots on the road, coloured by speed (blue slow to red fast) or in one colour.
- **Options, Best line:** speed colours, one colour, or off.

**New: race control**
- **Incidents:** a sudden stop next to another car is logged as possible contact, anywhere else as a hit. The host sees each driver's incidents.
- **Host penalties:** in the results the host can give +5 s or +10 s, disqualify, or clear it.
- **Flags:** yellow when a car ahead is stopped or off the road, blue when a faster car a lap ahead is right behind you.

**New: room games** (Battle, Races & tournaments, What to play)
- **Cops and runners:** a runner stopped next to a cop for 3 s is busted and becomes a cop.
- **Tag** and **Infected:** touch to pass it on, or to spread it.
- **King of the hill:** stay alone in the zone.
- **Keep-up:** stay with the host or you're out.
- **Convoy cruise:** follow the host, no winner.
- **Highway cut-up:** points for close, fast passes on other players, with combos up to x10. A hit loses the combo.
- They're played anywhere on the map, no course needed, in rounds of 3 to 15 minutes.

**New: highway battles**
- A third 1v1 format, Tokyo Xtreme Racer style: drive alongside each other, a rolling 3-2-1, then whoever is behind loses SP, faster the bigger the gap. A hit costs SP too. Empty SP, or 600 m behind, loses. No course needed.

**Changed**
- Battles and races changed format: they need 0.13.0 on every player.

## 0.12.0 (Oct 25 2026)

Not tested in game yet. Open races and tournaments for up to 16 drivers. Everyone needs 0.12.0, for races and for battles.

**New: open races (Battle, Races & tournaments)**
- **Host a race** on any course of the map, point to point or circuit, for up to 16 drivers. Everyone in the room with TRC Tune Loader sees a pop-up and the race in their list, and joins with one click. The host starts it when everyone's in.
- **Grid order:** order joined, random, each driver's best time on the course, or **qualifying**.
- **Grid:** pole on the left or the right, two-wide staggered or a single-file line-up, along the course's own grid. **Teleport to my spot** puts you in your place.
- **3-2-1-GO** for everyone at the same moment once all are lined up, or when the host says go.
- **Jump starts:** +5 s penalty, or disqualified (the host picks). The race goes on for everyone else.
- **Live positions:** your place (P3/12), the lap, and how far you are from the cars just ahead and behind. With more than 8 drivers the positions update twice a second instead of five times, except at the start and in the last 150 m. Lag never changes the result: every mod times its own car to the thousandth.
- **The end:** when everyone has finished, or when the host ends it, in 60 s or straight away. Anyone still driving then doesn't finish.
- **Results** with times, gaps and best laps, the same on every screen, even if the host leaves mid-race. Your results are saved in `TimeAttack\races.csv`.
- **Elimination races** on circuits: the last car each lap is out, until one is left.
- Resets don't end your race (unless they put you further along), like battles.

**New: qualifying**
- The host opens a qualifying session (5 to 20 minutes, unlimited runs or 1 to 3 each). Everyone drives time attacks on the course; the best times set the grid, or the seeding of a tournament.

**New: tournaments**
- **Points series:** several races (the same course or different ones), scoring 25, 20, 16, 13, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 1, plus 1 for the fastest lap on circuits. Most points wins; ties go to the most wins, then the last race. Rounds can use a **reverse grid**.
- **Knockout:** 1v1 battles in a bracket seeded 1 v 16, 2 v 15..., with byes for the top seeds. Pick side by side or cat and mouse and best of 1 or 3, separately for the final, and a battle for 3rd. The host calls each battle; the two drivers' mods challenge and accept by themselves.
- **Heats and final:** heats of 4 to 8, the top of each heat goes through to one final race.
- **Shootout:** a time attack session; the best time wins.
- The standings show in the race lobby for everyone.

**New: race grids**
- **Lay out grid** (Courses here): drive the road up to a course's start line, down the middle, and through the line. Races on it line up along that road, two-wide or single file.
- Maps can place exact grid spots: `TRC <course> Grid 1` to `Grid 16`.

**New: sector times that stay on screen**
- Your sectors stay on the timer for the whole run or lap, coloured as you finish each one: **purple** great pace (faster than that sector has ever been driven), **green** on pace (as good as your best in this car), **amber** below pace. The sectors still to come are greyed out.
- The result at the finish shows them in the same colours, and **My times** shows each best time's sectors and every recent time's sectors, coloured against your other times.
- **Options:** Show or Hide sector times.

**New: private tunes**
- On the Players page, a player with a modified TRC tune shows only **what** is changed ("Front spring stiffness: changed"), not the values. The same goes for their stance, engine, gearbox and suspension. Your own car shows every number.
- **Options → Share my tune values** (off by default): turn it on if you don't mind other TRC Tune Loader users seeing your real numbers.

**Changed**
- The Battle view has two parts: 1v1 battles, and Races & tournaments.
- Battles changed format for tournament matches: battles need 0.12.0 on both sides.

## 0.11.0 (Oct 24 2026)

Not tested in game yet. Circuits, laps and sectors. Both drivers need 0.11.0 to battle.

**New: circuits**
- A third kind of course next to touge and highway: a loop whose start line is also the finish line.
- **Laps:** pick 1, 3, 5, 10 or **Unlimited** above the course list. The timer shows the lap time, the lap ("Lap 2/5"), the total and your best lap, and each lap time flashes as you cross the line.
- **Unlimited laps is practice:** a lap with a shortcut, off the road or with a reset just doesn't count (shown in red), and you keep driving. Only your best lap is kept.
- **Lap summary** at the end: how many laps, the best, the average and how even they were.
- **Course builder:** pick Point to point or Circuit. For a circuit, set the start line, drive once round (at least 200 m) and back through the start line: the loop closes by itself.
- Maps can have circuits too (`TRC Start <name> Circuit`, no finish object), and each gets a reverse circuit, driven the other way.
- Battles on circuits: pick the laps (1, 2, 3 or 5) with the challenge.

**New: flying start**
- Pick **Flying** instead of Standing: the clock starts as you cross the start line, at any speed. For a flying lap of a circuit, Teleport to start puts you a bit before the line for a run-up.
- Flying times are kept apart from standing ones.

**New: sectors and the live delta**
- Every course is split into sectors: 3, plus one for every 2 km over 6 km. Maps can mark their own (`TRC <course> Sector 1`, `Sector 2`, ...).
- Each sector's time flashes as you finish it: purple for the best it's ever been driven, green for your best in this car, red when slower. Your sectors show with the result.
- **Live delta:** once you have a best time in this car on this tune set, the timer shows how far ahead (green) or behind (red) of it you are right now.
- **Theoretical best:** in My times, your best sectors added up, when that's faster than your best time.

**New: restart key**
- **Ctrl + R** during a time attack sends your car back to the start line, ready to go again, without opening the window. Change it in Options.

**New: course builder and course editing**
- What you record shows as light-blue dots on the road. **Show path** in Courses here shows any course's road the same way.
- **Edit** on your own courses: rename, change the line width, flip the direction (a circuit: drive it the other way), record the path again, or delete.
- If a point-to-point course ends back at its start, the builder tells you to make it a circuit.
- After a battle on someone else's course, **Save this course** keeps it to practise on.

**Changed: My times**
- Three views: **Best times** (by map and course; open a course for a row per car and tune set), **Best laps** (circuits) and **Recent** (your latest 40 times). **TRC spec only** hides times that don't count.

**Changed: battles**
- A reset no longer ends your run in a battle: mess up, reset and keep going, and you can still win if the other driver messes up too. A reset that puts you further along the course still stops it.
- When the other driver's run stops (off the road, a shortcut...), you're told straight away: finish to win.
- Side-by-side race spots are 1 m closer together on each side.
- Cat and mouse: the chaser lines up from 5 m behind the start line (was 10 m), and the leader within 5 m, so the chaser can't line up in front.

## 0.10.1 (Oct 23 2026)

Not tested in game yet. Fixes battle challenges. In 0.10.0 a battle worked in game on Akina Downhill (a side-by-side race), but only on courses without a path. Both drivers need 0.10.1 to battle.

**Fixed**
- A challenge on a course with a path (a map course whose path you recorded, or a course of your own) never got to the other driver. Challenges are now about a quarter of the size and go in small pieces, and every new copy fills in any pieces the last one lost.
- Challenging each other at the same moment declined both challenges. Now both show, and when you both picked the same course, format and mode, they become one battle by themselves.
- A challenge didn't show on screen while the other driver was in a time attack or recording a course. It now shows under the timer.
- Accepting a challenge while your own was still waiting didn't take yours back on the other driver's screen.
- Names coloured letter by letter were cut off in challenges. They now show as plain text.

**Changed**
- Cat and mouse: the leader needs a 1.5 s lead to win while you both have your first lead, then 1 s for your second, then 0.5 s until someone wins (TRC's call). A battle without a winner is a draw after 10 runs instead of 6.
- The "Ahead by" / "Behind by" gap in a battle no longer jumps around: the other car's position is worked out for the same moment as yours (it used to be up to a fifth of a second old, several metres at speed) and the number moves smoothly. "Side by side" shows only when you're within 2 m.
- The challenger now sees whether the challenge got there: "got your challenge, waiting for them to accept", "can't accept it" with the reason, or "still sending".
- Opening the window while a challenge is waiting goes straight to Time attack, Battle.
- A battle no longer ends the moment your car is out of reach (up to 3 s is fine, like during a teleport).
- When a battle starts, other challenges still waiting are answered "already in a battle".
- The mod's log records every step of a battle (sent, got there, accepted, lined up, countdown), so problems can be found.

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
