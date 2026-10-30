# TRC Tune Loader: a guide for map makers

TRC Tune Loader finds courses that are built into a map. Name a few objects in your map the way this guide shows, and every player with the mod gets them in **Time attack → Tracks**. That means time attacks with sectors, battles, races with grids, speed traps and the road check, with no one having to build the course by hand first.

Everything here is plain Unity objects with the right names. There's no script or package to add.

## The rules for names

- Each name starts with `TRC`, then what the object is, then the course or road name: `TRC Start Akina Downhill`.
- Capitals, spaces and underscores don't matter. `TRC_Start_Akina_Downhill`, `trc start akina downhill` and `TRC Start Akina Downhill` are all the same.
- Copies Unity numbers, like `TRC Mainroad Akina (1)`, or Blender's `.001` and `.002`, are fine.
- The course name is whatever comes after the word: `Akina Downhill`, `Tsukuba Circuit`, `Wangan Highway`.
  - **"Circuit"** in the name makes it a circuit (a loop).
  - **"Highway"** makes it a highway course.
  - Anything else is a touge course.

## Setting up a line

Starts, finishes and speed traps are lines across the road. Each one is an **empty object** (a marker is enough):

1. Put it in the **middle of the road**, on the line.
2. Turn it so its **blue arrow (Z)** points the way you drive.
3. Set its **X scale** to the road width in metres, for example 14. Under 2 means the default, 12 m.

An object that's turned off still counts, so you can use your own start and finish signs and hide them. The mod shows them while someone is running that course, and puts them back after.

## The objects

| What | Name | Notes |
| --- | --- | --- |
| Start line | `TRC Start Akina Downhill` | Needed. Set up as above. |
| Finish line | `TRC Finish Akina Downhill` | Needed, except on a circuit: its start line is also the finish line. |
| Main road | `TRC Mainroad Akina` (or `TRC Main road Akina`) | The road mesh itself, **with its collider**. Name the object that has the collider. A road split into pieces can be `TRC Mainroad Akina.001`, `.002` and so on. Every course whose name starts with "Akina" uses it: if the whole car is off this road for half a second, the run stops. Flying above it counts as on it, so jumps along the road are fine. |
| Path | `TRC Path Akina Downhill` | Strongly recommended: the line down the middle of the road. See **The path** below. |
| Sectors | `TRC Akina Downhill Sector 1`, `Sector 2`, … | Optional. An empty object where each sector ends, in driving order. The last sector ends at the finish. Without them the course is split evenly. |
| Race grid | `TRC Akina Downhill Grid 1` … `Grid 16` | Optional. An empty object where each car lines up, 1 being pole, blue arrow the way the cars face. |
| Speed traps on a course | `TRC Akina Downhill Trap 1`, `Trap 2`, … | Optional. Set up like a line, numbered in driving order. They show during a time attack and can score a speed trap race. |
| Free-roam speed trap | `TRC Speed Trap Akina Bridge` | Optional. Anywhere on the map, set up like a line. Cars are caught going either way. Name them like `TRC Speed Trap Akina 1`, `TRC Speed Trap Akina 2` and they're grouped under "Akina" in the mod. |

### You get the other direction for free

- **Downhill:** a course whose name ends in "Downhill" also gets its uphill twin. "Akina Downhill" makes "Akina Uphill", starting at the finish and ending at the start, with the path and sectors reversed. To make your own uphill instead, add `TRC Start Akina Uphill` and the rest.
- **Circuits:** "Tsukuba Circuit" also gets "Tsukuba Circuit Reverse", driven the other way from the same line. Race grids aren't flipped: the twin needs its own grid objects.

## The path

The path is a line down the middle of the road, from the start line to the finish line. With it, the mod knows exactly where the road goes. It uses that to:

- catch shortcuts
- show how far along everyone is in battles and races
- measure the live delta properly through hairpins

A course in your map that has its path is ready to use from the moment the map loads. Without one, someone has to drive it once with **Record path** first.

### How to make it

The easiest way is a line of vertices down the middle of the road, turned into a **very thin strip**:

1. In Blender (or any 3D program), make a line of vertices along the middle of the road, from the start line to the finish line. A vertex every few metres is plenty: more on bends, fewer on straights. Keep every gap **under 25 m**.
2. **Make it a strip, not just a line.** Unity throws away edges that have no faces when it imports a model, so a bare line of vertices disappears. Select the whole line and extrude it sideways by about 0.3 m (in Blender: select all, press `E`, then move it across the road). You get a thin ribbon of faces. Keep it **under 1 m wide**: the mod merges the two edges into one centre line.
3. Lift it a little above the road, or leave it on the road. Height doesn't matter much.
4. Import it into Unity, and in the model's import settings turn on **Read/Write** (the mod can't read the shape without it).
5. Name the object `TRC Path Akina Downhill`, turn its **Mesh Renderer off** (so nobody sees it) and give it **no collider**.

One path is enough for both directions: "Akina Uphill" uses it reversed.

**Another way:** an empty object named `TRC Path Akina Downhill` with **child objects in driving order**, each an empty placed down the middle of the road. This is slower to place than a strip, but it needs no model.

Line Renderers don't survive the map upload, so don't use one for the path.

## A full example

For a touge road called Akina, run downhill and uphill, with three sectors, a race grid and two speed traps:

```
TRC Mainroad Akina              (the road mesh, with its collider)
TRC Start Akina Downhill        (empty: top of the hill, blue arrow downhill, X scale 12)
TRC Finish Akina Downhill       (empty: bottom of the hill, blue arrow downhill, X scale 12)
TRC Path Akina Downhill         (thin strip down the middle, renderer off, Read/Write on)
TRC Akina Downhill Sector 1     (empty, where sector 1 ends)
TRC Akina Downhill Sector 2     (empty, where sector 2 ends)
TRC Akina Downhill Grid 1 ... 8 (empties where each car lines up)
TRC Speed Trap Akina 1          (empty across the road, X scale the width)
TRC Speed Trap Akina 2
```

Players get "Akina Downhill" and "Akina Uphill", both with sectors and both ready to race, plus the two speed traps (grouped as "Akina") in free roam.

## Checking your map

1. Load your map in CarX with TRC Tune Loader installed and drive onto it.
2. Open the mod's window (Ctrl + T) and go to **Time attack → Tracks**. Your courses are listed there, with any problems under them in amber. Some examples:
   - "No 'TRC Finish …' object"
   - "Can't read the path: turn on Read/Write"
   - "The path stops 40 m before the finish"
3. Changed something? Press **Look for course objects again**.
4. The mod also writes what it found to `CarX Drift Racing Online\kino\mods\TRCTuneLoader\TRCTuneLoader.log`: every course, every road and how many pieces it found, and every problem.

## Official courses

Times on any map are saved for the player. Courses only count as **official TRC courses** (for TRC leaderboards and ranked events) on maps TRC has added to the mod. If you'd like your map's courses to count, contact TRC.
