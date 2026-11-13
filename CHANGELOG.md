# Release notes

All versions of TRC Tune Loader, newest first. "Not tested in game yet" means the version builds and its automatic tests pass, but nobody has tried it in CarX yet.

## 0.18.4 (Nov 12 2026)

Partly tested in game: faster map loading and the traffic start. The rest builds and passes its automatic tests, but nobody has tried it in CarX yet. (0.18.3 was never released; its changes are here.)

- Time attack: your best times now carry over between cars. In a car with no time on a track yet, the live delta, the sector pop-ups and the best line compare with your best on that track in any car. Before, they showed nothing.
- Time attack: a bar under the timer shows the live delta (green when you're ahead, red when you're behind), with your predicted time beside the number.
- New: telemetry (Options, While driving). A small panel over the track with your own car's throttle, brake, handbrake, clutch and steering, gear and RPM, a g-force dot, and each wheel's slip (green gripping, amber past its best grip, red well past it). Each part is off until you turn it on, and it can be moved with Arrange the screen.
- New: Timing detail (Options, While driving). Simple works as before. Detailed also shows your best in the car you're in, each sector's gap to your best in any car and in this car, and at speed traps the fastest speed ever with the car that set it.
- Speed traps: the Speed traps page shows which car set each trap's fastest speed. A sign set to "Fastest in this car" no longer shows another car's speed after you switch cars.
- Traffic lab: five new traffic cars: AE86, RX7 FC, RX7 FD, Subaru and a van.
- Traffic lab: joining someone's traffic is safer. Lanes far too big to be real are refused instead of freezing your game, and much bigger lane networks can be shared than before.
- Traffic lab: joining someone with different lanes is about 10 times quicker (their lanes are sent much smaller). Everyone in the room needs 0.18.4.
- Traffic lab: maps can now carry their own traffic lanes (see the map makers' guide).
- Battles, 1v1 and Specs, Check my car: no more stutter every couple of seconds in a room.
- Traffic lab: starting traffic no longer freezes the game for several seconds. The cars load one at a time, with a line saying which, and Cancel works while they do.
- Time attack: maps with lots of tracks, like Project Dream, load several seconds faster from the second time on. The mod remembers each track's road, and reads it again only if the map changed. "Look for course objects again" always reads everything fresh.
- Time attack: no more long freeze after Record path or Lay out grid (about 7 seconds on Project Dream).
- Traffic lab: starting traffic again, or joining someone's, no longer stutters for a couple of seconds (your traffic cars stay loaded while you're on the same map).
- Traffic lab: if the player whose traffic you follow crashes or leaves, their traffic stops on your screen after 5 seconds instead of driving on forever.
- Traffic lab: joining someone's lanes is quicker, and the traffic page is lighter while it's open.
- Smoother overall: less work every frame (speed traps, the race status, wheel buttons, My times), and the log file is written in batches.
- New: search on Home. Type a page or a setting (ghosts, shortcut, colours) and press what it finds.
- New: pin any page to Home from its title, up to 8.
- New: Compare tunes (TRC spec). Two tunes for your car, or your car against a set, showing only what differs.
- New: when you leave a map, Home shows your last session: time, distance, top speed, runs, laps, speed traps, battles and races.
- Options is now four short pages in Tools: Keys and buttons, Look, While driving, Sharing and backups.
- Players page: each report says how old it is, and an old one is greyed out.
- Traffic lab: with two players running traffic, someone joining stays with one of them, and a host can swap to the other's with one press.
- Traffic lab: crashes are sent twice, so a lost message no longer leaves players seeing different traffic.
- Traffic lab: the page says when someone's traffic is on another version of the mod, and warns a follower when the host's updates are arriving unevenly.
- Messages from other players only count from someone in the room, and floods of messages are cut.
- The TRC Tune Loader window no longer opens by itself when you start CarX. After an update (or the first time) a short message on the garage screen says which key opens it.

## 0.18.2 (Nov 7 2026)

Not tested in game yet. Fixes from the first traffic test with three players.

- Traffic lab: a car someone hits is one wreck for everyone, not one per player.
- Traffic lab: other players' cars no longer knock traffic out on your screen. Only the player who hits a car decides, so cars don't vanish from under the host.
- Traffic lab: following someone's traffic is smoother.

## 0.18.1 (Nov 6 2026)

Not tested in game yet.

- Traffic lab: traffic is shared with the room again. In 0.18.0 nobody's traffic was sent, so "Traffic in this room" stayed empty. Everyone needs 0.18.1.

## 0.18.0 (Nov 6 2026)

Not tested in game yet. The traffic lab was tried with four players on Project Dream; room sharing is fixed in this version but not tried again yet.

- **Traffic lab (very early testing)**, in Tools: AI traffic on Project Dream's highways and on any track's path. Pick how busy, the speed limit, driving on the left or right, and a realistic or cut-up style. Cars merge, change lanes, take exits, and crash when you hit them. In a room, one player starts the traffic and the others press Join next to their name, so everyone sees the same cars and the same crashes. Expect bugs and big changes.
- Traffic cars: "Generic passenger car pack" by Comrade1280 and "LCT 3000 '95 - Low poly model" by Daniel Zhabotinsky, from Sketchfab, CC BY 4.0, changed for the TRC traffic.
- Time attack, Tracks: every track with a path has a **Random speed traps** button, the map's own and official tracks too. It opens Speed traps with that track picked.
- Time attack, Speed traps: the page no longer lags with lots of traps and speeds saved.

## 0.17.1 (Nov 3 2026)

Partly tested in game: the Specs pages, the Home cards and the diff type picker. Checking other players against your specs and regulations needs two players and passes its automatic tests.

**New**
- **Specs → Check my car.** Like TRC Tunes, but for your specs and regulations. Pick a spec to see whether your car is on it, checked the same way as TRC spec (the saved setup, the live car, the stance, engine or drivetrain swaps), with anything different listed, and put it on from there. Below it, each regulation: met, or what breaks it.
- **Specs → Players.** Everyone in the room against your specs and regulations, checked from what CarX shares of their car, so it doesn't rest on what their mod says. Their mod also says which spec and regulations it's on, and a claim your check disagrees with shows in red. A spec you don't have can't be checked, so install it first.

**Changed**
- **My car is now called TRC Tunes**, and stays just for the TRC tunes.
- **Regulations: the diff type is picked by name.** Allow Open, Welded, Viscous or Clutch type by clicking them, instead of a least and a most number. A broken rule says, for example, "Diff type Welded isn't allowed (only Open, Clutch type)". An older regulation with a number limit on the diff type turns into the named types when you edit it.

## 0.17.0 (Nov 3 2026)

Partly tested in game: the new menu and Home, Look, the gantry signs, My times, the best line, and keeping clicks off the game. The controller, specs, manual sectors and the start key build and pass their automatic tests.

**New**
- **A new menu.** Home is a big card for each part of the mod in its colour: TRC spec, Specs, Time attack, Battles and Free roam, each with a line about what's going on and buttons to its pages. Inside, the parts are across the top, their pages are down the left, and the logo takes you back to Home. The tools (Mod check, Suspension logger, Options, About, Arrange screen) are along the bottom of Home, with Report a bug. A pill at the top right says whether your car is TRC spec.
- **Controller in the menu.** The D-pad moves between buttons, A presses one, B goes back to Home (or closes the window on Home), and LB and RB change part. While the menu has the controller, the car keeps still. A bar along the bottom shows the buttons. Both can be turned off in Options.
- **Specs.** Make your own spec: a tune for each car, like TRC's V1, V2, V3 and Revival sets. Save your car's tune into it, put a spec's tune on (your own tune is backed up first), and share it as a zip that lands on your Desktop. To install someone's spec, drop their zip (or its folder) in your specs folder and press Look again. Each spec is its own folder in `specs`. TRC spec stays its own part, as before.
- **Race to a spec.** A regulation can require one of your specs, so battles, races and tournaments can be run to it. Every driver needs the spec installed, and 0.17.0.
- **Time attack to a spec.** Time attack's rules have a fourth choice, Player spec, with its own times.
- **Manual sectors.** Open a sprint or circuit in Tracks and press **Sectors**, then drive to where a sector should end and press **Add sector end here**. Manual sectors replace the automatic ones. On your own courses they're saved in the course file, so they go with it when you share it. On map courses, **Copy sectors code** and **Paste sectors code** share them. Options can turn manual sectors off. Changing sectors doesn't change the course code.
- **Start key** (Ctrl + G, change it in Options): starts a time attack on the track open in Tracks (or the last one you drove) and puts you on its start line, flying or standing as picked. The TRC wheel button does the same when there's nothing else for it to do.
- **The selected track** in Tracks shows its name big, its kind, length and sectors, your best, your theoretical best and how many runs you've done, with **Teleport & start**.

**Changed**
- On CarX's minimap and map, the start and finish show as a line across the road, and speed traps as a little camera.
- The window is a bit wider, to fit the page list down the left.
- **Look** in Options is redone for the new menu. Background, text and second text; accent and button colours; and a colour for each part of the menu (its name across the top and its Home card). Type a hex code or use sliders. **Reset all colours** puts everything back.
- Home's cards are all the same size, with TRC spec across the top, and each fades from its colour in one corner to dark in the other.

**Fixed**
- Your best line no longer stays on the road after a time attack. It shows during the countdown and the run, and on your way back to the start line.
- Speed trap and gantry signs no longer show on top of the open window.
- While the window is open, clicks no longer go through to the game, and the mouse wheel no longer zooms the game's camera.
- Gantry signs are fixed to the beam now, facing both ways, so they can be seen from either side and no longer break into squares when the camera moves. Like the rest of the gantry, trees and walls can hide them.
- Circuits no longer get a note saying their path stops thousands of metres before the finish. A circuit's path is now checked to come back round to the start line.

## 0.16.1 (Nov 3 2026)

Partly tested in game (the gantries, signs, tracks list and Lake Akina); the rest builds and passes its automatic tests.

**New**
- **Start and finish gantries.** The start has a STAGE START sign whose four lamps light up with the countdown (red at 3 and 2, amber at 1, green at GO). The finish has a sign that shows **STAGE CLEAR** with your time, top speed and rank, and **NEW PERSONAL BEST** when it is one. A circuit has one gantry, showing the lap you're on. The signs show in front of trees and other signs, fixed on the gantry, from about 140 m away. Options can switch back to the plain lines.
- **Time attack rules:** TRC spec (what counts for TRC), Open (any setup) or to one of your regulations. Each keeps its own times, bests and best line, and My times shows them apart.
- **Icons on CarX's minimap and map:** the start (green), finish (pink), sectors (lime) and speed traps (amber) of the course you're driving, or of the track you have open in Tracks, and the map's free-roam speed traps. Options can turn them off.
- **FINISH on screen** when you cross the finish line of a time attack, race, battle or circuit, big like GO at the start, with your time under it (and your place in a race, and "new PB" when it is one).
- **Sector pop-up:** at each sector, a pop-up like the speed trap one shows the sector's time in its colour and how far ahead of or behind your PB you are. Move it with Arrange the screen.
- **Random speed traps** on any track with a path, the map's own included: pick how many, and they're spread along it as a group of their own ("Akina Downhill Random"). The Course builder can add them when you save. Delete this group takes a whole group away.
- **Regulations: power to weight is a most, in hp per kg.** Set 0.22 and no car may go over 0.22 hp for each kg (with the same small leeway as the other limits). Regulations made with the old "least kg per hp" still check the same and keep their codes.
- **Speed trap signs** can show your fastest ever too, and every speed caught is saved with the rules it was driven to (TRC spec, Open or a regulation) as well as the car.
- **Checkpoint gantries** where each sector ends, in lime green, with SECTOR banners up the towers. When you pass one, its sign shows how far ahead of or behind your PB you are there ("-0.842 AHEAD OF PB"), or the sector's time in races and battles.
- **Single file on narrow roads.** Where two cars won't fit side by side (Yabitsu, Maze Pass), races and 1v1 battles line up one behind the other by themselves. Each driver's clock starts when they cross the start line, so lining up further back costs nothing. The race list says when a course is single file.

**Changed**
- **Side by side, cars line up closer together:** 3 m apart, whatever the width of the start line.
- **Circuit races line up along the end of the lap**, round the last corner, instead of straight back from the line.
- **Opening a track closes the one you had open** in Tracks.
- **The path is easier to see:** chevrons pointing the way to drive, lying along the slope, out to 150 m (the flat squares sank out of sight going uphill). The best line uses them too.
- **Home's Go to** has everything: Regulations, the suspension logger, mod check, Arrange the screen and Options, in five groups.

**Fixed**
- **Lag in Tracks, Races and 1v1** on maps with lots of tracks (Project Dream's 107): only the rows you can see are drawn, best times are worked out once, and Races measures only the road you pick. The Races and 1v1 course lists have a Find box.
- **Fun games**: the last game (Highway cut-up) was cut off; the games now wrap onto two rows.
- **Faster map scans**: a path shared by several courses is read once.

**For map makers**
- Circuits are named like the rest: `TRC Start Akina Circuit` and `TRC Path Akina Circuit`, and a map with several layouts numbers them (`Okutama Circuit 1`, `Okutama Circuit 2`). On a road with a downhill too, a circuit uses only a path named with "Circuit"; on a road with just a circuit, the plain path works. See [MAP_MAKERS.md](https://github.com/Bskftw/TRCTuneLoader/blob/main/MAP_MAKERS.md).
- Copies that skip a number (`.001`, then `.003`) are taken in order. Speed traps copied that way are listed as 1, 2, 3 in order.
- A path that doesn't run far enough back from the start for a full grid gets a note under the course in Tracks, saying how far to run it on. Races use what fits meanwhile.

## 0.16.0 (Nov 1 2026)

Not tested in game yet. (0.15.2 was never released: its changes are in here too.)

**New**
- **Regulations** (new tab), for communities that tune their own cars to rules. Make one with any of: which setups (Race, Ultimate, Ultimate Pro...), which cars, the most power, weight, power to weight, CarX and KINO swaps, KINO weight and air suspension, widebody kits, a least and a most for any tune value, and tyre widths by weight class. **Check my car** shows what your car breaks; it also checks everyone in the room. Values are checked the way CarX shows them, so 600.4 hp counts as 600 and a tyre stretch of 10.3 (shown as 10) counts as 10. Share it with the room or one player, or **Copy for Discord** and **Paste from clipboard**. 1v1 battles, races and tournaments can be run to a regulation, and each regulation has its own Elo.
- **Just for fun**: any battle, race or tournament, TRC spec ones too, can be set to change nobody's ratings.
- **Console players**: a PC host can add PlayStation, Xbox and Switch players to a race, or race one in 1v1 battles. The host's mod times them from where their car is, and the host's headlights show them the start (on, off at 3, 2 and 1, off at GO). Their jump starts are caught too. The host can also run a race without driving in it.
- **Jump starts** can recall the start (everyone lines up again and the countdown starts over) or go unpunished, and tournaments can have **no penalties**.
- **No driver limit** for races and tournaments, for custom servers with more than 16 players. Over 16, positions are sent less often.
- **Ghosts**: every run is recorded. Options picks what's kept: your three best time attacks per car and course, every run, or nothing. Name any ghost, and pick how see-through ghosts are. **Race it** puts a ghost on the road, with the live delta and sectors against it. Ghosts have no collider or physics, so they can't give a slipstream; a time driven with one still counts, and it's noted in `runs.csv`.
- **Suspension** (new tab): suspension data logging, off unless you turn it on. A live overlay shows your car from above, each tyre filling up as it compresses, with its load, damper speed and whether it's on the road. After a run (or for the whole session): travel used, bottoming, packing down over bumps, contact lost (and whether right after a compression), load spikes, a damper speed histogram, and plain-language notes on what to change. Compare against a baseline, export to CSV, and see each corner's spring frequency and damping ratios.
- **Share a run** (its time, sectors and ghost) with the room or one player, and race theirs.
- **Share your speed traps** (Time attack, Speed traps, **Share...**): send the traps you placed on a map to everyone in the room or to one player, all of them or one group. They show up in the other player's Speed traps section, and **Add them** puts them with their own. A trap where they already have one is skipped. The other player needs 0.16.0 or newer.

**Changed**
- **Arrange the screen** (Options): drag the timer, the speed trap flash, pop-up notices, the TRC spec badge and the suspension overlay wherever you want, all at once, without starting anything first.
- **Speed trap signs** can show your fastest speed through each trap instead of your last (Options).
- Starting a **time attack** (or racing a ghost, or Record path) keeps the window open: **Teleport to start** is right there on the track, and it closes the window.
- **Tracks** fold to one line each, like My times: click a track to open it. Maps with lots of tracks get a **Find a track** box.
- **Races** and **Tournaments** are separate sections in Battles now.
- **For map makers:** only a course's start and finish need "Downhill" or "Uphill" in their names. A path and sectors named after just the road (`TRC Path Akina`, `TRC Akina Sector 1`) work for both directions. Older names still work. A path that runs on past the start line is where races line up (no grid objects to place), and past the finish, where the other direction lines up. A path can be the road's centre faces duplicated (flat, any width) or a line extruded up or down, and can be in pieces. Copies named `.001` or `(1)` are read as the same object, the unnumbered one first. See [MAP_MAKERS.md](https://github.com/Bskftw/TRCTuneLoader/blob/main/MAP_MAKERS.md).
- **Your ratings file is sealed.** Elo, Safety Rating and license can't be changed by editing the file any more: a changed file, or someone else's, makes your ratings start again from the beginning. Your current ratings carry over when you update.

## 0.15.1 (Oct 30 2026)

Not tested in game yet. Fixes from testing 0.15.0, and speed traps the way they were meant to be.

**New**
- **Speed trap battles** in 1v1 battles, for fun, like Most Wanted: side by side, and the higher total of every trap on the course wins. You have to finish for yours to count. Both of you need 0.15.1; it doesn't change your Elo.
- **Live speed traps** in races and battles: every trap flashes your speed and your total so far, and the timer shows your total, your place on the traps and the top three, live. Before, you only saw the totals at the end.
- **Second text colour** (Options, Look): the grey notes and labels can have their own colour now. "Reset" makes them follow your text colour again.

**Changed**
- **Races and battles keep going** after a reset or going off the road. That lap doesn't count for a best lap, but your race goes on to the finish. Only a shortcut, or a jump of over 100 m further along the course, stops it.
- **Free-roam traps always record**, also after a reset or when you're out of a race.

**Fixed**
- **KINO's weight and distribution menu** wasn't caught. A lower centre of gravity or extra weight now makes the car not TRC spec, from the moment it's on, even if it was already on when you reached the track.
- **The speed trap race menu** cost a lot of fps for players with many speed traps. It's now as light as the rest of the window.
- **A speed trap race's own traps** didn't flash or get saved while you raced (only a time attack's did), and the host's own trap total was missing from the live standings.

## 0.15.0 (Oct 29 2026)

Not tested in game yet. Built from a night of testing with a second player: tidier tabs, much better speed traps, and fixes for what went wrong. Works with 0.13 and 0.14 players, but update together for the fixes to battles and speed trap races.

**Turn on automatic updates for TRC Tune Loader in KSL.** If they're off, KSL only says an update is available and keeps the old version: a tester stayed on 0.14.1 that way. From this version, the mod tells you when someone in your room has a newer version.

**New**
- **Tidier window.** Time attack lists its sections down the left: **Tracks** (was "Courses here"), **Speed traps**, **My times** and **Course builder**. **Battles** is now its own tab, with **1v1 battles**, **Races & tournaments**, **Fun games** (the room games) and **My ratings**. **Backups** moved into **My car**, in a column beside the tune sets. Course rows have their buttons on a line of their own.
- **Speed traps, reworked.** They have their own section. Name each trap and pick how wide it is (4 to 30 m). Traps named like "Tsubaki Speed Trap 1, 2, 3" fold under **Tsubaki**. Edit renames a trap, changes its width or moves it.
- **SPEED CHECK signs.** Every trap has a floating neon sign over the road with your last speed through it. The line across the road only shows while you place or edit a trap, so you can see how wide it is.
- **Free-roam traps work all the time** when they're on, during time attacks, battles and races too.
- **Speed trap races**, like Most Wanted: **Race for: Speed traps** when hosting. The highest total of every trap on the course wins (every lap on a circuit), and you have to finish. A course without traps of its own uses the free-roam traps along its road, and everyone in the race now scores the same traps.
- **Your own colours and text size** (Options, Look): ready looks (TRC, Darker, Midnight, Grey, High contrast, Light) or your own background, accent and text colours, and text from 90% to 150%.
- **Delete times** in My times: one run, a car's times on a course, or a whole course (asks twice; the old file is kept).
- **Reverse on save:** the Course builder can also save your course the other way ("Akina Downhill" gives "Akina Uphill").
- **A guide for map makers** ([MAP_MAKERS.md](https://github.com/Bskftw/TRCTuneLoader/blob/main/MAP_MAKERS.md)): every object name the mod looks for, and how to model the road's path so no one has to drive it first.
- A challenge that arrives while the window is open shows at the top of every tab, with **Answer**.
- **Newer version in the room:** when another player runs a newer TRC Tune Loader, a notice and a card on Home tell you to turn on automatic updates in KSL and restart CarX.

**Fixed**
- **Highway battles where only one driver started.** When the countdown didn't reach the other driver, one side raced alone and the result counted for one driver only. The countdown is now sent again until it gets there. If it never does, the run is called off, and a run the other driver never started counts for nobody.
- **Challenges on long courses that never arrived.** A course is sent in pieces, and on long ones the same pieces kept getting lost. They now go out slower and in a new order each time, and the challenge waits up to 90 s, saying it's still sending.
- **The live delta jumping to +50 s** in hairpins on map courses without a recorded path. It now follows your best run's own line.
- **Names with colour codes** (KINO's "[00D928]" colours and gradient names) showed cut off with the codes in them. They now show plain.
- Closing the window lets go of the keyboard and any held click.

**Changed**
- **Ratings are being tested**, and the window says so: they're not official, and Elo and Safety Rating will be reset before they count.
- The **Safety Rating** has new rules. It moves only in TRC spec battles and races that were really driven, no longer in time attacks.
  - Light touches in close racing don't count. Only hard contact, hard hits and resets do, and one event can take at most 0.30.
  - Your Safety Rating and license were reset once for the new rules; Elo was kept.

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
